using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniEmu;

/// <summary>
/// El ícono de la bandeja: un teléfono del color de la barra de tareas, con un
/// punto verde cuando hay algún emulador encendido.
/// </summary>
sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon = new() { Text = "MiniEmu", Visible = true };
    bool? renderedDark;
    int renderedRunning = -1;

    /// <summary>Click izquierdo o derecho: abre o cierra el panel.</summary>
    public event Action? Clicked;

    public TrayIcon()
    {
        Update(running: 0);
        icon.MouseUp += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) Clicked?.Invoke();
        };
    }

    public void Update(int running)
    {
        if (renderedDark == Theme.Dark && renderedRunning == running) return;
        renderedDark = Theme.Dark;
        renderedRunning = running;

        var previous = icon.Icon;
        icon.Icon = Render(Theme.Dark, running > 0);
        previous?.Dispose();
        icon.Text = running switch
        {
            0 => "MiniEmu",
            1 => "MiniEmu · 1 emulador encendido",
            _ => $"MiniEmu · {running} emuladores encendidos",
        };
    }

    public void Notify(string text) => icon.ShowBalloonTip(2500, "MiniEmu", text, ToolTipIcon.None);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.Dispose();
    }

    static Icon Render(bool dark, bool anyRunning)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // AntiAliasGridFit respeta la transparencia del bitmap (ClearType no).
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(IconFamily(), size * 0.95f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(dark ? Color.White : Color.FromArgb(26, 26, 26));
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            format.FormatFlags |= StringFormatFlags.NoClip;
            g.DrawString(Glyphs.Phone, font, brush, new RectangleF(0, 0, size, size), format);

            if (anyRunning)
            {
                float diameter = size * 0.42f;
                using var green = new SolidBrush(Color.FromArgb(61, 220, 132));
                g.FillEllipse(green, size - diameter, size - diameter, diameter, diameter);
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

    static string IconFamily()
    {
        using var installed = new InstalledFontCollection();
        return installed.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
