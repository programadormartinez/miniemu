using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MiniEmu;

/// <summary>Las pocas llamadas a Win32 que ni WPF ni WinForms exponen.</summary>
static class Native
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    const int DWMWA_CLOAK = 13;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWCP_ROUND = 2;
    const int DWMSBT_TRANSIENTWINDOW = 3; // acrílico, el de los paneles de Windows 11

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    static int SetAttribute(IntPtr hWnd, int attribute, int value) =>
        DwmSetWindowAttribute(hWnd, attribute, ref value, sizeof(int));

    /// <summary>Esquinas redondeadas de Windows 11 (en Windows 10 la llamada se ignora).</summary>
    public static void SetRoundedCorners(IntPtr hWnd) => SetAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    /// <summary>Marco y fondo acrílico en oscuro o claro.</summary>
    public static void SetDarkMode(IntPtr hWnd, bool dark) => SetAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    /// <summary>Oculta la ventana sin sacarla de la pantalla: sirve para ubicarla antes de que se vea.</summary>
    public static void SetCloak(IntPtr hWnd, bool cloak) => SetAttribute(hWnd, DWMWA_CLOAK, cloak ? 1 : 0);

    /// <summary>Fondo acrílico del sistema. Solo existe desde Windows 11 22H2 (build 22621).</summary>
    public static bool TrySetAcrylicBackdrop(IntPtr hWnd) =>
        Environment.OSVersion.Version.Build >= 22621 &&
        SetAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) == 0;

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// <summary>Ventana de herramienta: no aparece en Alt+Tab ni en la barra de tareas.</summary>
    public static void MakeToolWindow(IntPtr hWnd)
    {
        long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr((style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Trae al frente la primera ventana visible cuyo título empieza con <paramref name="prefix"/>.</summary>
    public static bool FocusWindowWithTitlePrefix(string prefix)
    {
        IntPtr found = IntPtr.Zero;
        var title = new StringBuilder(512);
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            title.Clear();
            GetWindowText(hWnd, title, title.Capacity);
            if (!title.ToString().StartsWith(prefix, StringComparison.Ordinal)) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero) return false;
        if (IsIconic(found)) ShowWindow(found, SW_RESTORE);
        SetForegroundWindow(found);
        return true;
    }
}

/// <summary>Ventana invisible que recibe el atajo global (WM_HOTKEY).</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int HotkeyId = 1;
    bool registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(uint modifiers, Keys key)
    {
        Unregister();
        registered = Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, (uint)key);
        return registered;
    }

    public void Unregister()
    {
        if (!registered) return;
        Native.UnregisterHotKey(Handle, HotkeyId);
        registered = false;
    }

    /// <summary>Convierte "Alt+Shift+E" o "Ctrl+Win+F12" en modificadores y tecla.</summary>
    public static bool TryParse(string text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= Native.MOD_WIN; break;
                default:
                    if (key != Keys.None || !Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        return key != Keys.None && modifiers != 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
