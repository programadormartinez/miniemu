using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MiniEmu;

/// <summary>
/// El estado de la app (ajustes, SDK, dispositivos) y todas las acciones. La
/// bandeja y el panel solo muestran este estado y llaman a estos métodos.
/// </summary>
sealed class AppController
{
    const string RepoUrl = "https://github.com/programadormartinez/miniemu";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly TrayIcon tray = new();
    readonly HotkeyWindow hotkey = new();
    FlyoutWindow? flyout;
    DateTime settingsWrittenAt;
    AndroidSdk? sdk;
    string? sdkProblem;
    string? refreshError;

    /// <summary>Cambió la lista de dispositivos o empezó/terminó una actualización.</summary>
    public event Action? Changed;

    public Settings Settings { get; private set; } = new();
    public IReadOnlyList<Device> Devices { get; private set; } = [];
    public string? Problem => sdkProblem ?? refreshError;
    public bool Refreshing { get; private set; }
    public bool RefreshedOnce { get; private set; }

    public AppController()
    {
        Settings = LoadSettings();
        tray.Clicked += () => Flyout.Toggle();
        hotkey.Pressed += () => Flyout.Toggle();
        RegisterHotkey();
        SystemEvents.UserPreferenceChanged += (_, _) => Application.Current.Dispatcher.InvokeAsync(OnThemeMaybeChanged);
        _ = RefreshAsync();
    }

    FlyoutWindow Flyout => flyout ??= new FlyoutWindow(this);

    int RunningEmulators => Devices.Count(d => d.Kind == DeviceKind.Emulator && d.IsRunning);

    void OnThemeMaybeChanged()
    {
        bool wasDark = Theme.Dark;
        Theme.Refresh();
        if (Theme.Dark == wasDark) return;
        Palette.Apply(Application.Current.Resources);
        tray.Update(RunningEmulators);
        flyout?.ApplyTheme();
    }

    // ------------------------------------------------------------------ ajustes

    Settings LoadSettings()
    {
        try
        {
            var loaded = Settings.Load();
            settingsWrittenAt = File.GetLastWriteTimeUtc(Settings.FilePath);
            return loaded;
        }
        catch (Exception ex)
        {
            // No volver a avisar hasta que el archivo cambie de nuevo.
            settingsWrittenAt = File.GetLastWriteTimeUtc(Settings.FilePath);
            ShowError("No pude leer los ajustes", $"{Settings.FilePath}\n\n{ex.Message}\n\nSigo con los ajustes anteriores.");
            return Settings;
        }
    }

    void ReloadSettingsIfChanged()
    {
        if (!File.Exists(Settings.FilePath) || File.GetLastWriteTimeUtc(Settings.FilePath) == settingsWrittenAt) return;
        var previousHotkey = Settings.Hotkey;
        Settings = LoadSettings();
        if (Settings.Hotkey != previousHotkey) RegisterHotkey();
    }

    void RegisterHotkey()
    {
        if (!HotkeyWindow.TryParse(Settings.Hotkey, out var modifiers, out var key))
        {
            hotkey.Unregister();
            Notify($"No entiendo el atajo \"{Settings.Hotkey}\". Usá algo como Alt+Shift+E.");
            return;
        }
        if (!hotkey.Register(modifiers, key))
            Notify($"No pude registrar {Settings.Hotkey}: probablemente lo usa otra app.");
    }

    public void EditSettings()
    {
        if (!File.Exists(Settings.FilePath)) Settings.Save();
        Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { Settings.FilePath } });
    }

    public void OpenAbout() => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });

    public bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("MiniEmu") is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("MiniEmu", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("MiniEmu", throwOnMissingValue: false);
        }
    }

    public IReadOnlyList<CustomCommand> CommandsFor(Device device) =>
        Settings.Commands.Where(c => (!c.NeedsRunningDevice || device.IsRunning) && (!c.BootsDevice || !device.IsRunning)).ToList();

    // ------------------------------------------------------------------ dispositivos

    public async Task RefreshAsync()
    {
        if (Refreshing) return;
        ReloadSettingsIfChanged();
        Refreshing = true;
        Changed?.Invoke();

        var located = AndroidSdk.Locate(Settings.AndroidHome, out var problem);
        sdk = located;
        sdkProblem = problem;
        try
        {
            if (located is null) Devices = [];
            else Devices = await Task.Run(() => located.GetDevicesAsync());
            refreshError = null;
        }
        catch (Exception ex)
        {
            refreshError = ex.Message;
        }
        finally
        {
            Refreshing = false;
            RefreshedOnce = true;
        }
        tray.Update(RunningEmulators);
        Changed?.Invoke();
    }

    async Task RefreshAfter(TimeSpan delay)
    {
        await Task.Delay(delay);
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ acciones

    public void OpenOrFocus(Device device)
    {
        if (!device.IsRunning)
        {
            Launch(device);
            return;
        }
        // La ventana del emulador se llama "Android Emulator - <AVD>:<puerto>".
        if (!Native.FocusWindowWithTitlePrefix($"Android Emulator - {device.Name}:"))
            Notify($"{device.Name} está encendido pero no encontré su ventana (¿corre dentro de Android Studio?).");
    }

    public void Launch(Device device, params string[] extra) => Run(async active =>
    {
        var args = Settings.LaunchParameters
            .SelectMany(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Concat(extra)
            .ToList();
        using var process = active.Launch(device.Name, args);
        Notify($"Iniciando {device.Name}…");

        // Si se cierra enseguida con error, casi siempre es un flag inválido o
        // falta la aceleración por hardware (WHPX / AEHD).
        if (await Task.Run(() => process.WaitForExit(TimeSpan.FromSeconds(6))) && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"El emulador se cerró al arrancar (código {process.ExitCode}). Probalo desde una terminal para ver el error:\n\n" +
                $"\"{active.Emulator}\" @{device.Name} {string.Join(' ', args)}");

        // adb tarda unos segundos en ver el emulador nuevo.
        _ = RefreshAfter(TimeSpan.FromSeconds(8));
    });

    public void Stop(Device device) => Run(async active =>
    {
        if (device.Serial is not string serial) return;
        await active.KillAsync(serial);
        await RefreshAfter(TimeSpan.FromSeconds(1.5));
    }, $"{device.Name} apagado");

    public void Copy(string text, string message)
    {
        try
        {
            Clipboard.SetText(text);
            Notify($"{message}: {text}");
        }
        catch (Exception ex)
        {
            ShowError("No pude copiar", ex.Message);
        }
    }

    public void ToggleTalkBack(string serial) => Run(async active =>
        Notify(await active.ToggleTalkBackAsync(serial) ? "TalkBack activado" : "TalkBack desactivado"));

    public void PasteClipboard(string serial)
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (text.Length == 0)
        {
            Notify("El portapapeles no tiene texto.");
            return;
        }
        Run(active => active.PasteTextAsync(serial, text), "Texto enviado al dispositivo");
    }

    public void OpenLogcat(string serial) => Run(active =>
    {
        active.OpenLogcat(serial);
        return Task.CompletedTask;
    });

    public void Upload(string serial)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Subir a la carpeta Descargas del dispositivo" };
        if (dialog.ShowDialog() != true) return;
        var files = dialog.FileNames;
        Run(async active =>
        {
            var destination = await active.UploadAsync(serial, files);
            var what = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} archivos";
            Notify($"Subí {what} a {destination}.");
        });
    }

    public void Delete(Device device)
    {
        // Con Shift apretado no pregunta, igual que en MiniSim.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            var delete = new Forms.TaskDialogButton("Eliminar");
            var page = new Forms.TaskDialogPage
            {
                Caption = "MiniEmu",
                Heading = $"¿Eliminar {device.Name}?",
                Text = "Se borran el emulador y todos sus datos. No se puede deshacer.\n\nTip: Shift + click elimina sin preguntar.",
                Icon = Forms.TaskDialogIcon.Warning,
                Buttons = { delete, Forms.TaskDialogButton.Cancel },
                DefaultButton = Forms.TaskDialogButton.Cancel,
            };
            if (Forms.TaskDialog.ShowDialog(page) != delete) return;
        }

        Run(async active =>
        {
            if (device.Serial is string serial) await active.KillAsync(serial);
            await AndroidSdk.DeleteAvdAsync(device.Name);
            await RefreshAsync();
        }, $"{device.Name} eliminado");
    }

    public void RunCustom(CustomCommand command, Device device) => Run(async active =>
    {
        var result = await active.RunCustomAsync(command, device);
        if (result is { ExitCode: not 0 })
            throw new InvalidOperationException(
                $"\"{command.Name}\" terminó con código {result.ExitCode}.\n\n{(result.Error + result.Output).Trim()}");
    }, $"{command.Name}: listo");

    /// <summary>Corre una acción contra el SDK y muestra su error, si lo hay.</summary>
    async void Run(Func<AndroidSdk, Task> action, string? success = null)
    {
        var active = sdk;
        if (active is null)
        {
            ShowError("No encontré el Android SDK", sdkProblem ?? "");
            return;
        }
        try
        {
            await action(active);
            if (success is not null) Notify(success);
        }
        catch (Exception ex)
        {
            ShowError("Algo salió mal", ex.Message);
        }
    }

    void Notify(string text) => tray.Notify(text);

    public static void ShowError(string heading, string message) =>
        Forms.TaskDialog.ShowDialog(new Forms.TaskDialogPage
        {
            Caption = "MiniEmu",
            Heading = heading,
            Text = message.Length > 800 ? message[..800] + "…" : message,
            Icon = Forms.TaskDialogIcon.Error,
            Buttons = { Forms.TaskDialogButton.OK },
        });

    public void Quit()
    {
        tray.Dispose();
        hotkey.Dispose();
        flyout?.CloseForGood();
        Application.Current.Shutdown();
    }
}
