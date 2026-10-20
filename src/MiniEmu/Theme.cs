using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MiniEmu;

/// <summary>Claro u oscuro, igual que la barra de tareas (como los paneles de Windows 11).</summary>
static class Theme
{
    public static bool Dark { get; private set; }

    public static void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        Dark = key?.GetValue("SystemUsesLightTheme") is int light && light == 0;
    }
}

/// <summary>
/// Los colores del panel. Viven en los recursos de la app y los estilos los usan
/// con DynamicResource: al cambiar el tema se actualizan solos.
/// </summary>
static class Palette
{
    public static void Apply(ResourceDictionary resources)
    {
        bool dark = Theme.Dark;
        void Set(string key, uint darkColor, uint lightColor) => resources[key] = Brush(dark ? darkColor : lightColor);

        Set("TextPrimary", 0xFFFFFFFF, 0xFF1A1A1A);
        Set("TextSecondary", 0xFFC5C5C5, 0xFF5C5C5C);
        // Semitransparentes: dejan ver el fondo acrílico.
        Set("CardBackground", 0x0DFFFFFF, 0xB3FFFFFF);
        Set("CardHover", 0x18FFFFFF, 0xE6FFFFFF);
        Set("CardStroke", 0x14FFFFFF, 0x0F000000);
        Set("SubtleHover", 0x14FFFFFF, 0x0A000000);
        Set("SubtlePressed", 0x0AFFFFFF, 0x06000000);
        Set("Divider", 0x15FFFFFF, 0x0F000000);
        Set("Success", 0xFF6CCB5F, 0xFF0F7B0F);
        Set("Danger", 0xFFFF99A4, 0xFFC42B1C);
        Set("Accent", 0xFF3DDC84, 0xFF0E8A4F);
        Set("IconTile", 0x263DDC84, 0x1F3DDC84);
        Set("MenuBackground", 0xFF2C2C2C, 0xFFF9F9F9);
        Set("MenuStroke", 0xFF3D3D3D, 0xFFE0E0E0);
        Set("MenuHover", 0xFF3D3D3D, 0xFFEAEAEA);
        // Solo si Windows no da fondo acrílico (Windows 10 o 11 anterior a 22H2).
        Set("FallbackBackground", 0xFF202020, 0xFFF3F3F3);
    }

    static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Códigos de Segoe Fluent Icons (los mismos que Segoe MDL2 Assets).</summary>
static class Glyphs
{
    public const string Play = "";
    public const string Stop = "";
    public const string Window = "";
    public const string Copy = "";
    public const string Refresh = "";
    public const string Mute = "";
    public const string Accessibility = "";
    public const string Paste = "";
    public const string Terminal = "";
    public const string Upload = "";
    public const string Delete = "";
    public const string Settings = "";
    public const string Power = "";
    public const string Info = "";
    public const string Check = "";
    public const string Bolt = "";
    public const string Warning = "";
    public const string Phone = "";
    public const string Tablet = "";
    public const string Tv = "";
}
