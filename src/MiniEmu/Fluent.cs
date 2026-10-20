using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace MiniEmu;

/// <summary>Colores y fuentes de Windows 11, en claro u oscuro según la barra de tareas.</summary>
static class Theme
{
    public static bool Dark { get; private set; }

    public static Color Background => Dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
    public static Color Hover => Dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(234, 234, 234);
    public static Color Text => Dark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(26, 26, 26);
    public static Color SecondaryText => Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(108, 108, 108);
    public static Color Separator => Dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(229, 229, 229);
    public static Color Border => Dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(214, 214, 214);
    public static Color Running => Dark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(15, 123, 15);
    public static Color Danger => Dark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28);
    public static readonly Color AndroidGreen = Color.FromArgb(61, 220, 132);

    public static readonly Font MenuFont = new(PickFamily("Segoe UI Variable Text", "Segoe UI"), 9f);
    public static readonly Font HeaderFont = new(MenuFont.FontFamily, 7.5f, FontStyle.Bold);
    public static readonly string IconFamily = PickFamily("Segoe Fluent Icons", "Segoe MDL2 Assets");

    /// <summary>Windows 11 redondea los menús con DWM; Windows 10 no sabe hacerlo.</summary>
    public static bool RoundedCorners => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Lee el tema actual. El menú sigue a la barra de tareas, no a las apps.</summary>
    public static void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        Dark = key?.GetValue("SystemUsesLightTheme") is int light && light == 0;
    }

    static string PickFamily(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        var families = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(families.Contains) ?? "Segoe UI";
    }
}

/// <summary>Códigos de Segoe Fluent Icons (comparte los de Segoe MDL2 Assets).</summary>
static class Glyph
{
    public const char Play = '';
    public const char Window = '';
    public const char Copy = '';
    public const char Refresh = '';
    public const char Mute = '';
    public const char Accessibility = '';
    public const char Paste = '';
    public const char Terminal = '';
    public const char Upload = '';
    public const char Delete = '';
    public const char Settings = '';
    public const char Power = '';
    public const char Info = '';
    public const char Check = '';
    public const char Bolt = '';
    public const char Warning = '';
    public const char Phone = '';
    public const char ChevronRight = '';
}

/// <summary>Íconos dibujados con la fuente de íconos del sistema: nítidos a cualquier escala.</summary>
static class Icons
{
    static readonly Dictionary<(char Glyph, int Color, int Size), Bitmap> GlyphCache = new();
    static readonly Dictionary<(int Color, bool Filled, int Size), Bitmap> DotCache = new();

    public static Bitmap Glyph(char glyph, Color color, int size)
    {
        var key = (glyph, color.ToArgb(), size);
        if (GlyphCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            DrawGlyph(g, glyph, color, new RectangleF(0, 0, size, size), size * 0.8f);
        }
        return GlyphCache[key] = bitmap;
    }

    /// <summary>Punto de estado: lleno y verde si está encendido, un aro gris si no.</summary>
    public static Bitmap Dot(Color color, bool filled, int size)
    {
        var key = (color.ToArgb(), filled, size);
        if (DotCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float diameter = size * 0.5f;
            var circle = new RectangleF((size - diameter) / 2, (size - diameter) / 2, diameter, diameter);
            if (filled)
            {
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, circle);
            }
            else
            {
                using var pen = new Pen(color, Math.Max(1f, size / 12f));
                g.DrawEllipse(pen, circle);
            }
        }
        return DotCache[key] = bitmap;
    }

    /// <summary>
    /// El ícono de la bandeja: un teléfono del color de la barra de tareas, con un
    /// punto verde cuando hay algún emulador encendido.
    /// </summary>
    public static Icon Tray(bool anyRunning)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // MiniEmu.Glyph: dentro de Icons, "Glyph" a secas es el método de arriba.
            DrawGlyph(g, MiniEmu.Glyph.Phone, Theme.Dark ? Color.White : Color.FromArgb(26, 26, 26),
                new RectangleF(0, 0, size, size), size * 0.95f);
            if (anyRunning)
            {
                float diameter = size * 0.42f;
                using var brush = new SolidBrush(Theme.AndroidGreen);
                g.FillEllipse(brush, size - diameter, size - diameter, diameter, diameter);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    static void DrawGlyph(Graphics g, char glyph, Color color, RectangleF bounds, float pixelSize)
    {
        // AntiAliasGridFit respeta la transparencia del bitmap (ClearType no).
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(Theme.IconFamily, pixelSize, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        format.FormatFlags |= StringFormatFlags.NoClip;
        g.DrawString(glyph.ToString(), font, brush, bounds, format);
    }
}

/// <summary>Marca el ítem de un dispositivo: guarda el nombre a mostrar sin el "&1" del atajo.</summary>
sealed record DeviceTag(string Name);

/// <summary>Pinta los menús como Windows 11: fondo plano, hover redondeado, chevrons y separadores finos.</summary>
sealed class FluentMenuRenderer : ToolStripRenderer
{
    public const string Header = "header";
    public const string Danger = "danger";

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (Theme.RoundedCorners) return; // en Windows 11 el borde redondeado lo dibuja DWM
        using var pen = new Pen(Theme.Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Sin franja gris para los íconos: el fondo es uno solo.
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        float scale = Scale(e.ToolStrip);
        var rect = new RectangleF(0, 1 * scale, e.Item.Width, e.Item.Height - 2 * scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(rect, 4 * scale);
        using var brush = new SolidBrush(Theme.Hover);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        // El texto real es "&1Pixel_8" para que el 1 funcione como atajo; mostramos solo el nombre.
        var text = item.Tag is DeviceTag device && e.Text == item.Text ? device.Name : e.Text;
        var color = item.Tag switch
        {
            Header => Theme.SecondaryText,
            Danger => Theme.Danger,
            _ => item.Enabled ? Theme.Text : Theme.SecondaryText,
        };
        // Centrado sobre el alto completo del ítem. El rectángulo que calcula
        // WinForms ignora el Padding vertical y deja el texto más arriba que el ícono.
        var bounds = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, item.Height);
        TextRenderer.DrawText(e.Graphics, text, e.TextFont, bounds, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.HidePrefix | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        float scale = Scale(e.ToolStrip);
        var rect = e.ArrowRectangle;
        rect.Offset((int)(-6 * scale), 0); // aire contra el borde derecho
        using var font = new Font(Theme.IconFamily, 7f);
        TextRenderer.DrawText(e.Graphics, Glyph.ChevronRight.ToString(), font, rect, Theme.SecondaryText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float scale = Scale(e.ToolStrip);
        int y = e.Item.Height / 2;
        int inset = (int)(8 * scale);
        using var pen = new Pen(Theme.Separator, Math.Max(1f, scale));
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
    }

    static float Scale(ToolStrip? strip) => (strip?.DeviceDpi ?? 96) / 96f;

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Aplica el estilo Fluent a un menú o submenú.</summary>
static class MenuStyle
{
    static readonly FluentMenuRenderer Renderer = new();

    public static void Apply(ToolStripDropDown dropDown)
    {
        dropDown.Renderer = Renderer;
        dropDown.Font = Theme.MenuFont;
        dropDown.BackColor = Theme.Background;
        int icon = dropDown.LogicalToDeviceUnits(16);
        dropDown.ImageScalingSize = new Size(icon, icon);
        // Margen interno: el hover y los separadores no tocan el borde redondeado.
        int inset = dropDown.LogicalToDeviceUnits(4);
        dropDown.Padding = new Padding(inset, inset, inset, inset);
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowCheckMargin = false;
            menu.ShowImageMargin = true;
        }

        dropDown.HandleCreated -= OnHandleCreated;
        dropDown.HandleCreated += OnHandleCreated;
        if (dropDown.IsHandleCreated) Decorate(dropDown.Handle);
    }

    static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control) Decorate(control.Handle);
    }

    static void Decorate(IntPtr handle)
    {
        if (!Theme.RoundedCorners) return;
        Native.SetRoundedCorners(handle);
        Native.SetBorderColor(handle, Theme.Border);
    }
}
