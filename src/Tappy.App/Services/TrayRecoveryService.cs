using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Tappy.App.Services;

public sealed class TrayRecoveryService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;

    public TrayRecoveryService(Action show, Action emergencyStop, Action exit)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(emergencyStop);
        ArgumentNullException.ThrowIfNull(exit);

        _ownedIcon = CreatePlaceholderIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Tappy", null, (_, _) => show());
        menu.Items.Add("Emergency stop — release Tappy output", null, (_, _) => emergencyStop());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Tappy", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon,
            Text = "Tappy — Device-aware pass-through",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void ShowBalloon(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon.Dispose();
    }

    private static Icon CreatePlaceholderIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(255, 23, 105, 170));
        using var accent = new SolidBrush(Color.FromArgb(255, 102, 227, 196));
        using var textBrush = new SolidBrush(Color.White);
        graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 7);
        graphics.FillEllipse(accent, 21, 3, 8, 8);
        using var font = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString("T", font, textBrush, 7, 5);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
