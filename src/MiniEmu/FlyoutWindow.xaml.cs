using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MiniEmu;

/// <summary>Lo que muestra la tarjeta de un dispositivo.</summary>
sealed class DeviceCardModel(Device device)
{
    public Device Device { get; } = device;
    public string Name => Device.Name;
    public bool IsRunning => Device.IsRunning;

    public string IconGlyph =>
        Name.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ? Glyphs.Tablet
        : Name.Contains("TV", StringComparison.Ordinal) ? Glyphs.Tv
        : Glyphs.Phone;

    public string Status => Device.Kind == DeviceKind.Physical ? $"Conectado · {Device.Serial}"
        : IsRunning ? $"Encendido · {Device.Serial}"
        : "Apagado";

    public string PrimaryGlyph => IsRunning ? Glyphs.Stop : Glyphs.Play;
    public string PrimaryTip => IsRunning ? "Apagar" : "Iniciar";
    public Visibility PrimaryVisibility => Device.Kind == DeviceKind.Emulator ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// El panel que se abre sobre la bandeja, como los de Windows 11: fondo acrílico,
/// una tarjeta por dispositivo y un menú "⋯" con el resto de las acciones.
/// </summary>
public partial class FlyoutWindow : Window
{
    readonly AppController app;
    readonly TranslateTransform slide = new();
    readonly RotateTransform spin = new();
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(3) };
    IReadOnlyList<Device> rendered = [];
    DateTime hiddenAt;
    int openMenus;
    bool renderPending;
    bool spinning;
    bool manualRefresh;
    bool allowClose;

    internal FlyoutWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        Root.RenderTransform = slide;
        RefreshGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
        RefreshGlyph.RenderTransform = spin;

        // Mientras está abierto, el estado se mantiene al día.
        poll.Tick += (_, _) => _ = app.RefreshAsync();
        app.Changed += () => Render();

        // Se cierra al hacer click afuera, salvo mientras hay un menú "⋯" abierto.
        Deactivated += (_, _) => { if (openMenus == 0) HideFlyout(); };
        SizeChanged += (_, _) => { if (IsVisible) Position(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            HideFlyout();
        };
    }

    // ------------------------------------------------------------------ abrir y cerrar

    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // El click en la bandeja llega justo después de que el panel se ocultó
        // por perder el foco: sin esto se volvería a abrir.
        if ((DateTime.UtcNow - hiddenAt).TotalMilliseconds < 300) return;
        ShowFlyout();
    }

    void ShowFlyout()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyTheme();
        Render(force: true);
        HotkeyText.Text = app.Settings.Hotkey.Replace("+", " + ");

        // Se ubica oculto (cloak) para que no aparezca un instante en otro lugar.
        Native.SetCloak(hwnd, true);
        Show();
        UpdateLayout();
        Position();
        Native.SetCloak(hwnd, false);
        Activate();
        Animate();

        poll.Start();
        _ = app.RefreshAsync();
    }

    void HideFlyout()
    {
        if (!IsVisible) return;
        poll.Stop();
        hiddenAt = DateTime.UtcNow;
        Hide();
    }

    internal void CloseForGood()
    {
        allowClose = true;
        poll.Stop();
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.MakeToolWindow(hwnd);
        Native.SetRoundedCorners(hwnd);
        ApplyTheme();
    }

    /// <summary>Marco oscuro o claro y fondo acrílico; sin acrílico, un fondo sólido.</summary>
    internal void ApplyTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.SetDarkMode(hwnd, Theme.Dark);
        if (Native.TrySetAcrylicBackdrop(hwnd))
        {
            // El acrílico se ve solo si WPF no pinta nada detrás del contenido.
            Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "FallbackBackground");
        }
    }

    /// <summary>En la esquina del área de trabajo que toca la barra de tareas.</summary>
    void Position()
    {
        var area = SystemParameters.WorkArea;
        const double gap = 12;
        Left = area.Left > 0 ? area.Left + gap : area.Right - ActualWidth - gap;
        Top = area.Top > 0 ? area.Top + gap : area.Bottom - ActualHeight - gap;
    }

    /// <summary>Entra deslizándose desde la barra de tareas, como los paneles de Windows 11.</summary>
    void Animate()
    {
        double from = SystemParameters.WorkArea.Top > 0 ? -14 : 14;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    // ------------------------------------------------------------------ contenido

    void Render(bool force = false)
    {
        SetSpinning(app.Refreshing && (manualRefresh || !app.RefreshedOnce));
        if (!app.Refreshing) manualRefresh = false;

        // Con un menú "⋯" abierto no se rearma la lista: el menú perdería su tarjeta.
        if (openMenus > 0)
        {
            renderPending = true;
            return;
        }

        var devices = app.Devices;
        var emulators = devices.Where(d => d.Kind == DeviceKind.Emulator).ToList();
        var physical = devices.Where(d => d.Kind == DeviceKind.Physical).ToList();

        // Solo se rearma si algo cambió: así no parpadea cada 3 segundos.
        if (force || !devices.SequenceEqual(rendered))
        {
            rendered = devices;
            EmulatorsList.ItemsSource = emulators.Select(d => new DeviceCardModel(d)).ToList();
            DevicesList.ItemsSource = physical.Select(d => new DeviceCardModel(d)).ToList();
        }
        EmulatorsHeader.Visibility = emulators.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DevicesHeader.Visibility = physical.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        string? notice = app.Problem
            ?? (devices.Count > 0 ? null
                : app.RefreshedOnce ? "No hay emuladores. Creá uno desde Android Studio y aparece acá."
                : "Buscando dispositivos…");
        Notice.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeText.Text = notice ?? "";
        NoticeGlyph.Text = app.Problem is not null ? Glyphs.Warning : Glyphs.Info;

        int running = emulators.Count(d => d.IsRunning);
        FooterText.Text = emulators.Count switch
        {
            0 => "",
            _ when running > 0 => $"{running} de {emulators.Count} encendidos",
            1 => "1 emulador",
            _ => $"{emulators.Count} emuladores",
        };
    }

    void SetSpinning(bool on)
    {
        if (on == spinning) return;
        spinning = on;
        spin.BeginAnimation(RotateTransform.AngleProperty, on
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }

    // ------------------------------------------------------------------ eventos

    static Device? DeviceOf(object sender) => ((FrameworkElement)sender).DataContext is DeviceCardModel card ? card.Device : null;

    void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DeviceOf(sender) is not { Kind: DeviceKind.Emulator } device) return;
        HideFlyout();
        app.OpenOrFocus(device);
    }

    void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is not { } device) return;
        if (device.IsRunning)
        {
            app.Stop(device);
            return;
        }
        HideFlyout();
        app.Launch(device);
    }

    void More_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is { } device) ShowMenu((FrameworkElement)sender, DeviceMenu(device));
    }

    void Refresh_Click(object sender, RoutedEventArgs e)
    {
        manualRefresh = true;
        _ = app.RefreshAsync();
    }

    void AppMenu_Click(object sender, RoutedEventArgs e) => ShowMenu((FrameworkElement)sender, AppMenu());

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
            return;
        }
        // 1…9 y 0 abren los primeros diez emuladores, como en MiniSim.
        int? index = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            Key.D0 => 9,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            Key.NumPad0 => 9,
            _ => null,
        };
        if (index is int i && EmulatorsList.ItemsSource is List<DeviceCardModel> cards && i < cards.Count)
        {
            e.Handled = true;
            HideFlyout();
            app.OpenOrFocus(cards[i].Device);
        }
    }

    // ------------------------------------------------------------------ menús "⋯"

    ContextMenu DeviceMenu(Device device)
    {
        var menu = new ContextMenu();
        if (device.Kind == DeviceKind.Emulator)
        {
            if (device.IsRunning)
            {
                menu.Items.Add(Entry("Mostrar ventana", Glyphs.Window, () => app.OpenOrFocus(device)));
            }
            else
            {
                menu.Items.Add(Entry("Arranque en frío", Glyphs.Refresh, () => app.Launch(device, "-no-snapshot")));
                menu.Items.Add(Entry("Iniciar sin audio", Glyphs.Mute, () => app.Launch(device, "-no-audio")));
            }
            AddSeparator(menu);
        }

        menu.Items.Add(Entry("Copiar nombre", Glyphs.Copy, () => app.Copy(device.Name, "Nombre copiado")));
        if (device.Serial is string serial)
        {
            menu.Items.Add(Entry("Copiar ID de ADB", Glyphs.Copy, () => app.Copy(serial, "ID de ADB copiado")));
            AddSeparator(menu);
            menu.Items.Add(Entry("TalkBack", Glyphs.Accessibility, () => app.ToggleTalkBack(serial)));
            menu.Items.Add(Entry("Pegar portapapeles", Glyphs.Paste, () => app.PasteClipboard(serial)));
            menu.Items.Add(Entry("Logcat", Glyphs.Terminal, () => app.OpenLogcat(serial)));
            menu.Items.Add(Entry("Subir archivos…", Glyphs.Upload, () => app.Upload(serial)));
        }

        var commands = app.CommandsFor(device);
        if (commands.Count > 0) AddSeparator(menu);
        foreach (var command in commands)
            menu.Items.Add(Entry(command.Name, Glyphs.Bolt, () => app.RunCustom(command, device)));

        if (device.Kind == DeviceKind.Emulator)
        {
            AddSeparator(menu);
            menu.Items.Add(Entry("Eliminar…", Glyphs.Delete, () => app.Delete(device), danger: true));
        }
        return menu;
    }

    ContextMenu AppMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry("Ajustes", Glyphs.Settings, app.EditSettings));
        bool startsWithWindows = app.StartsWithWindows;
        menu.Items.Add(Entry("Iniciar con Windows", startsWithWindows ? Glyphs.Check : "",
            () => app.StartsWithWindows = !startsWithWindows));
        menu.Items.Add(Entry("Acerca de MiniEmu", Glyphs.Info, app.OpenAbout));
        AddSeparator(menu);
        menu.Items.Add(Entry("Salir", Glyphs.Power, app.Quit));
        return menu;
    }

    MenuItem Entry(string text, string glyph, Action action, bool danger = false)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (danger) item.SetResourceReference(Control.ForegroundProperty, "Danger");
        // Como en MiniSim, elegir una acción cierra el panel (y no tapa diálogos).
        item.Click += (_, _) =>
        {
            HideFlyout();
            action();
        };
        return item;
    }

    static void AddSeparator(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is not Separator) menu.Items.Add(new Separator());
    }

    void ShowMenu(FrameworkElement target, ContextMenu menu)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        // Compensa el margen que el estilo deja para la sombra.
        menu.HorizontalOffset = -12;
        menu.VerticalOffset = -6;
        openMenus++;
        menu.Closed += (_, _) =>
        {
            openMenus--;
            if (renderPending)
            {
                renderPending = false;
                Render();
            }
            // Si se cerró por un click afuera, el panel también se va.
            Dispatcher.InvokeAsync(() => { if (!IsActive) HideFlyout(); }, DispatcherPriority.Input);
        };
        menu.IsOpen = true;
    }
}
