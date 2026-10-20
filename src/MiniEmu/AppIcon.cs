using System.Drawing.Drawing2D;

namespace MiniEmu;

/// <summary>Dibuja el ícono de la bandeja (un teléfono verde Android) sin archivos de imagen.</summary>
static class AppIcon
{
    public static Icon Create()
    {
        var size = SystemInformation.SmallIconSize;
        float w = size.Width, h = size.Height;

        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(w * 0.22f, h * 0.04f, w * 0.56f, h * 0.92f);
            using var outline = RoundedRectangle(body, w * 0.12f);
            using var green = new SolidBrush(Color.FromArgb(61, 220, 132));
            g.FillPath(green, outline);

            using var screen = new SolidBrush(Color.FromArgb(32, 33, 36));
            g.FillRectangle(screen, body.X + w * 0.07f, body.Y + h * 0.1f, body.Width - w * 0.14f, body.Height - h * 0.24f);
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

    static GraphicsPath RoundedRectangle(RectangleF r, float radius)
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
