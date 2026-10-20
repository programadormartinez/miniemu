using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace MiniEmu;

/// <summary>El ícono de la bandeja, su menú y las acciones sobre cada dispositivo.</summary>
sealed class TrayApp : ApplicationContext
{
    const string RepoUrl = "https://github.com/programadormartinez/miniemu";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly ContextMenuStrip menu = new();
    readonly NotifyIcon tray;
    readonly HotkeyWindow hotkey = new();

    Settings settings = new();
    DateTime settingsWrittenAt;
    AndroidSdk? sdk;
    string? sdkProblem;
    IReadOnlyList<Device> devices = [];
    string? refreshError;
    bool refreshing;
    bool refreshedOnce;

    public TrayApp()
    {
        settings = LoadSettings();
        tray = new NotifyIcon { Icon = AppIcon.Create(), Text = "MiniEmu", ContextMenuStrip = menu, Visible = true };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        // Como MiniSim: la lista se arma con lo último conocido y se refresca al abrir.
        menu.Opening += (_, e) =>
        {
            ReloadSettingsIfChanged();
            BuildMenu();
            e.Cancel = false;
            _ = RefreshAsync();
        };

        hotkey.Pressed += ShowMenu;
        RegisterHotkey();
        BuildMenu();
        _ = RefreshAsync();
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
            ShowError($"No pude leer {Settings.FilePath}:\n{ex.Message}\n\nSigo con los ajustes anteriores.");
            return settings;
        }
    }

    void ReloadSettingsIfChanged()
    {
        if (!File.Exists(Settings.FilePath) || File.GetLastWriteTimeUtc(Settings.FilePath) == settingsWrittenAt) return;
        var previousHotkey = settings.Hotkey;
        settings = LoadSettings();
        if (settings.Hotkey != previousHotkey) RegisterHotkey();
    }

    void RegisterHotkey()
    {
        if (!HotkeyWindow.TryParse(settings.Hotkey, out var modifiers, out var key))
        {
            hotkey.Unregister();
            Notify($"No entiendo el atajo \"{settings.Hotkey}\". Usá algo como Alt+Shift+E.");
            return;
        }
        if (!hotkey.Register(modifiers, key))
            Notify($"No pude registrar {settings.Hotkey}: probablemente lo usa otra app.");
    }

    void EditSettings()
    {
        if (!File.Exists(Settings.FilePath)) settings.Save();
        Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { Settings.FilePath } });
    }

    static bool StartsWithWindows
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

    // ------------------------------------------------------------------ menú

    void ShowMenu()
    {
        // NotifyIcon.ShowContextMenu es privado, pero es el único camino que
        // también trae el foco: sin eso el menú no se cierra al hacer click afuera.
        var show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        if (show is not null) show.Invoke(tray, null);
        else menu.Show(Cursor.Position);
    }

    async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        var located = AndroidSdk.Locate(settings.AndroidHome, out var problem);
        sdk = located;
        sdkProblem = problem;
        try
        {
            if (located is null) devices = [];
            else devices = await Task.Run(() => located.GetDevicesAsync());
            refreshError = null;
        }
        catch (Exception ex)
        {
            refreshError = ex.Message;
        }
        finally
        {
            refreshing = false;
            refreshedOnce = true;
        }
        if (menu.Visible) BuildMenu();
    }

    void BuildMenu()
    {
        menu.SuspendLayout();
        menu.Items.Clear();

        if (sdkProblem is not null) AddInfo(sdkProblem);
        else if (refreshError is not null) AddInfo("Error: " + refreshError);

        var active = sdk;
        if (active is not null)
        {
            AddSection("Dispositivos Android", devices.Where(d => d.Kind == DeviceKind.Physical).ToList(), active, numbered: false);
            AddSection("Emuladores Android", devices.Where(d => d.Kind == DeviceKind.Emulator).ToList(), active, numbered: true);
            if (devices.Count == 0 && refreshError is null)
                AddInfo(refreshedOnce ? "No hay emuladores. Creá uno desde Android Studio." : "Buscando dispositivos…");
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Editar ajustes…", EditSettings));
        var startup = new ToolStripMenuItem("Iniciar con Windows") { Checked = StartsWithWindows };
        startup.Click += (_, _) => StartsWithWindows = !StartsWithWindows;
        menu.Items.Add(startup);
        menu.Items.Add(Item("Acerca de MiniEmu", () => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true })));
        menu.Items.Add(Item("Salir", Quit));

        menu.ResumeLayout();
    }

    void AddInfo(string text) => menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });

    void AddSection(string title, List<Device> section, AndroidSdk active, bool numbered)
    {
        if (section.Count == 0) return;
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(title) { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
        for (int i = 0; i < section.Count; i++)
        {
            // Los primeros diez emuladores se eligen con 1…9 y 0 con el menú abierto.
            int? shortcut = numbered && i < 10 ? (i + 1) % 10 : null;
            menu.Items.Add(DeviceItem(section[i], active, shortcut));
        }
    }

    ToolStripMenuItem DeviceItem(Device device, AndroidSdk active, int? shortcut)
    {
        var item = new ToolStripMenuItem(shortcut is int n ? $"&{n}   {device.Name}" : device.Name) { Checked = device.IsRunning };
        var actions = item.DropDownItems;

        if (device.Kind == DeviceKind.Emulator)
        {
            item.Click += (_, _) => { menu.Close(); OpenOrFocus(device, active); };
            actions.Add(Item(device.IsRunning ? "Mostrar ventana" : "Iniciar", () => OpenOrFocus(device, active)));
        }
        actions.Add(Item("Copiar nombre", () => Copy(device.Name, "Nombre copiado")));
        if (device.Serial is string serial)
        {
            actions.Add(Item("Copiar ID de ADB", () => Copy(serial, "ID de ADB copiado")));
            actions.Add(new ToolStripSeparator());
            actions.Add(Item("Activar o desactivar TalkBack", () => Run(async () =>
                Notify(await active.ToggleTalkBackAsync(serial) ? "TalkBack activado" : "TalkBack desactivado"))));
            actions.Add(Item("Pegar el portapapeles en el dispositivo", () => PasteClipboard(serial, active)));
            actions.Add(Item("Abrir logcat", () => Run(() => { active.OpenLogcat(serial); return Task.CompletedTask; })));
            actions.Add(new ToolStripSeparator());
            actions.Add(Item("Subir archivos a Descargas…", () => Upload(serial, active)));
        }
        else
        {
            actions.Add(new ToolStripSeparator());
            actions.Add(Item("Arranque en frío", () => Launch(device, active, "-no-snapshot")));
            actions.Add(Item("Iniciar sin audio", () => Launch(device, active, "-no-audio")));
        }

        var commands = settings.Commands.Where(c =>
            (!c.NeedsRunningDevice || device.IsRunning) && (!c.BootsDevice || !device.IsRunning)).ToList();
        if (commands.Count > 0) actions.Add(new ToolStripSeparator());
        foreach (var command in commands)
            actions.Add(Item(command.Name, () => RunCustom(command, device, active)));

        if (device.Kind == DeviceKind.Emulator)
        {
            actions.Add(new ToolStripSeparator());
            actions.Add(Item("Eliminar emulador…", () => Delete(device, active)));
        }
        return item;
    }

    static ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    // ------------------------------------------------------------------ acciones

    void OpenOrFocus(Device device, AndroidSdk active)
    {
        if (!device.IsRunning)
        {
            Launch(device, active);
            return;
        }
        // La ventana del emulador se llama "Android Emulator - <AVD>:<puerto>".
        if (!Native.FocusWindowWithTitlePrefix($"Android Emulator - {device.Name}:"))
            Notify($"{device.Name} está encendido pero no encontré su ventana (¿corre dentro de Android Studio?).");
    }

    void Launch(Device device, AndroidSdk active, params string[] extra) => Run(async () =>
    {
        var args = settings.LaunchParameters
            .SelectMany(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Concat(extra)
            .ToList();
        using var process = active.Launch(device.Name, args);
        Notify($"Iniciando {device.Name}…");

        // Si se cierra enseguida con error, casi siempre es un flag inválido o
        // falta la aceleración por hardware (WHPX / AEHD).
        if (await Task.Run(() => process.WaitForExit(TimeSpan.FromSeconds(6))) && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"El emulador se cerró al arrancar (código {process.ExitCode}). Probalo desde una terminal para ver el error:\n" +
                $"\"{active.Emulator}\" @{device.Name} {string.Join(' ', args)}");
    });

    void PasteClipboard(string serial, AndroidSdk active)
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (text.Length == 0)
        {
            Notify("El portapapeles no tiene texto.");
            return;
        }
        Run(() => active.PasteTextAsync(serial, text), "Texto enviado al dispositivo");
    }

    void Upload(string serial, AndroidSdk active)
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "Subir a la carpeta Descargas del dispositivo" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var files = dialog.FileNames;
        Run(async () =>
        {
            var destination = await active.UploadAsync(serial, files);
            var what = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} archivos";
            Notify($"Subí {what} a {destination}.");
        });
    }

    void Delete(Device device, AndroidSdk active)
    {
        // Con Shift apretado no pregunta, igual que en MiniSim.
        bool skipConfirmation = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        if (!skipConfirmation && MessageBox.Show(
                $"¿Eliminar el emulador {device.Name}? Se borran sus datos y no se puede deshacer.",
                "MiniEmu", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        Run(async () =>
        {
            if (device.Serial is string serial) await active.KillAsync(serial);
            await AndroidSdk.DeleteAvdAsync(device.Name);
            _ = RefreshAsync();
        }, $"{device.Name} eliminado");
    }

    void RunCustom(CustomCommand command, Device device, AndroidSdk active) => Run(async () =>
    {
        var result = await active.RunCustomAsync(command, device);
        if (result is { ExitCode: not 0 })
            throw new InvalidOperationException(
                $"\"{command.Name}\" terminó con código {result.ExitCode}.\n{(result.Error + result.Output).Trim()}");
    }, $"{command.Name}: listo");

    void Copy(string text, string message)
    {
        Clipboard.SetText(text);
        Notify($"{message}: {text}");
    }

    /// <summary>Corre una acción y muestra su error, si lo hay.</summary>
    async void Run(Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null) Notify(success);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    void Notify(string text) => tray.ShowBalloonTip(2500, "MiniEmu", text, ToolTipIcon.None);

    static void ShowError(string message) =>
        MessageBox.Show(message.Length > 600 ? message[..600] + "…" : message, "MiniEmu",
            MessageBoxButtons.OK, MessageBoxIcon.Error);

    void Quit()
    {
        tray.Visible = false;
        tray.Dispose();
        hotkey.Dispose();
        ExitThread();
    }
}
