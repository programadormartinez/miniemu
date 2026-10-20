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
    int iconSize = 16;
    Padding itemPadding;

    public TrayApp()
    {
        Theme.Refresh();
        settings = LoadSettings();
        MenuStyle.Apply(menu);
        tray = new NotifyIcon { Icon = Icons.Tray(anyRunning: false), Text = "MiniEmu", ContextMenuStrip = menu, Visible = true };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        // Como MiniSim: la lista se arma con lo último conocido y se refresca al abrir.
        menu.Opening += (_, e) =>
        {
            Theme.Refresh();
            MenuStyle.Apply(menu); // por si cambió el tema claro/oscuro
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
            ShowError("No pude leer los ajustes", $"{Settings.FilePath}\n\n{ex.Message}\n\nSigo con los ajustes anteriores.");
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
        UpdateTrayIcon();
        if (menu.Visible) BuildMenu();
    }

    void UpdateTrayIcon()
    {
        int running = devices.Count(d => d.Kind == DeviceKind.Emulator && d.IsRunning);
        var previous = tray.Icon;
        tray.Icon = Icons.Tray(anyRunning: running > 0);
        previous?.Dispose();
        tray.Text = running switch
        {
            0 => "MiniEmu",
            1 => "MiniEmu · 1 emulador encendido",
            _ => $"MiniEmu · {running} emuladores encendidos",
        };
    }

    void BuildMenu()
    {
        iconSize = menu.LogicalToDeviceUnits(16);
        // ~30 px por ítem a 100 %, como los menús nativos de Windows 11.
        int vertical = menu.LogicalToDeviceUnits(4);
        itemPadding = new Padding(0, vertical, menu.LogicalToDeviceUnits(8), vertical);

        menu.SuspendLayout();
        menu.Items.Clear();

        if (sdkProblem is not null) AddNotice(sdkProblem);
        else if (refreshError is not null) AddNotice(refreshError);

        var active = sdk;
        if (active is not null)
        {
            AddSection("EMULADORES", devices.Where(d => d.Kind == DeviceKind.Emulator).ToList(), active, numbered: true);
            AddSection("DISPOSITIVOS", devices.Where(d => d.Kind == DeviceKind.Physical).ToList(), active, numbered: false);
            if (devices.Count == 0 && refreshError is null)
                AddNotice(refreshedOnce ? "No hay emuladores. Creá uno desde Android Studio." : "Buscando dispositivos…", Glyph.Refresh);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Ajustes", Glyph.Settings, EditSettings));
        bool startsWithWindows = StartsWithWindows;
        // Sin tilde cuando está apagado: el ícono vacío mantiene la alineación.
        var startup = Item("Iniciar con Windows", startsWithWindows ? Glyph.Check : ' ', () => StartsWithWindows = !startsWithWindows);
        menu.Items.Add(startup);
        menu.Items.Add(Item("Acerca de MiniEmu", Glyph.Info, () => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true })));
        menu.Items.Add(Item("Salir", Glyph.Power, Quit));

        menu.ResumeLayout();
    }

    void AddNotice(string text, char glyph = Glyph.Warning)
    {
        menu.Items.Add(new ToolStripMenuItem(text, Icons.Glyph(glyph, Theme.SecondaryText, iconSize))
        {
            Enabled = false,
            Padding = itemPadding,
        });
    }

    void AddSection(string title, List<Device> section, AndroidSdk active, bool numbered)
    {
        if (section.Count == 0) return;
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());

        // Un ítem deshabilitado (no un ToolStripLabel) para que el título quede en la
        // misma columna que los nombres.
        menu.Items.Add(new ToolStripMenuItem(title)
        {
            Enabled = false,
            Font = Theme.HeaderFont,
            Tag = FluentMenuRenderer.Header,
            Padding = new Padding(0, menu.LogicalToDeviceUnits(4), 0, 0),
        });
        for (int i = 0; i < section.Count; i++)
        {
            // Los primeros diez emuladores se eligen con 1…9 y 0 con el menú abierto.
            int? shortcut = numbered && i < 10 ? (i + 1) % 10 : null;
            menu.Items.Add(DeviceItem(section[i], active, shortcut));
        }
    }

    ToolStripMenuItem DeviceItem(Device device, AndroidSdk active, int? shortcut)
    {
        var item = new ToolStripMenuItem(shortcut is int n ? $"&{n}{device.Name}" : device.Name)
        {
            Image = Icons.Dot(device.IsRunning ? Theme.Running : Theme.SecondaryText, filled: device.IsRunning, iconSize),
            Tag = new DeviceTag(device.Name),
            Padding = itemPadding,
            ToolTipText = device.Serial,
        };
        MenuStyle.Apply(item.DropDown);
        var actions = item.DropDownItems;

        if (device.Kind == DeviceKind.Emulator)
        {
            item.Click += (_, _) => { menu.Close(); OpenOrFocus(device, active); };
            actions.Add(device.IsRunning
                ? Item("Mostrar ventana", Glyph.Window, () => OpenOrFocus(device, active))
                : Item("Iniciar", Glyph.Play, () => OpenOrFocus(device, active)));
        }

        if (device.Serial is null)
        {
            actions.Add(Item("Arranque en frío", Glyph.Refresh, () => Launch(device, active, "-no-snapshot")));
            actions.Add(Item("Iniciar sin audio", Glyph.Mute, () => Launch(device, active, "-no-audio")));
        }
        actions.Add(new ToolStripSeparator());
        actions.Add(Item("Copiar nombre", Glyph.Copy, () => Copy(device.Name, "Nombre copiado")));

        if (device.Serial is string serial)
        {
            actions.Add(Item("Copiar ID de ADB", Glyph.Copy, () => Copy(serial, "ID de ADB copiado")));
            actions.Add(new ToolStripSeparator());
            actions.Add(Item("TalkBack", Glyph.Accessibility, () => Run(async () =>
                Notify(await active.ToggleTalkBackAsync(serial) ? "TalkBack activado" : "TalkBack desactivado"))));
            actions.Add(Item("Pegar portapapeles", Glyph.Paste, () => PasteClipboard(serial, active)));
            actions.Add(Item("Logcat", Glyph.Terminal, () => Run(() => { active.OpenLogcat(serial); return Task.CompletedTask; })));
            actions.Add(Item("Subir archivos…", Glyph.Upload, () => Upload(serial, active)));
        }

        var commands = settings.Commands.Where(c =>
            (!c.NeedsRunningDevice || device.IsRunning) && (!c.BootsDevice || !device.IsRunning)).ToList();
        if (commands.Count > 0) actions.Add(new ToolStripSeparator());
        foreach (var command in commands)
            actions.Add(Item(command.Name, Glyph.Bolt, () => RunCustom(command, device, active)));

        if (device.Kind == DeviceKind.Emulator)
        {
            actions.Add(new ToolStripSeparator());
            var delete = Item("Eliminar…", Glyph.Delete, () => Delete(device, active), Theme.Danger);
            delete.Tag = FluentMenuRenderer.Danger;
            actions.Add(delete);
        }
        return item;
    }

    ToolStripMenuItem Item(string text, char glyph, Action onClick, Color? color = null)
    {
        var item = new ToolStripMenuItem(text, Icons.Glyph(glyph, color ?? Theme.Text, iconSize)) { Padding = itemPadding };
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
                $"El emulador se cerró al arrancar (código {process.ExitCode}). Probalo desde una terminal para ver el error:\n\n" +
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
        if (!skipConfirmation)
        {
            var delete = new TaskDialogButton("Eliminar");
            var page = new TaskDialogPage
            {
                Caption = "MiniEmu",
                Heading = $"¿Eliminar {device.Name}?",
                Text = "Se borran el emulador y todos sus datos. No se puede deshacer.\n\nTip: Shift + click elimina sin preguntar.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { delete, TaskDialogButton.Cancel },
                DefaultButton = TaskDialogButton.Cancel,
            };
            if (TaskDialog.ShowDialog(page) != delete) return;
        }

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
                $"\"{command.Name}\" terminó con código {result.ExitCode}.\n\n{(result.Error + result.Output).Trim()}");
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
            ShowError("Algo salió mal", ex.Message);
        }
    }

    void Notify(string text) => tray.ShowBalloonTip(2500, "MiniEmu", text, ToolTipIcon.None);

    static void ShowError(string heading, string message) =>
        TaskDialog.ShowDialog(new TaskDialogPage
        {
            Caption = "MiniEmu",
            Heading = heading,
            Text = message.Length > 800 ? message[..800] + "…" : message,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
        });

    void Quit()
    {
        tray.Visible = false;
        tray.Dispose();
        hotkey.Dispose();
        ExitThread();
    }
}
