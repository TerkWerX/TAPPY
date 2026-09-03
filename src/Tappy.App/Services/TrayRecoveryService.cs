using System.Drawing;
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

        _ownedIcon = CreateApplicationIcon();
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

    private static Icon CreateApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var associated = Icon.ExtractAssociatedIcon(executablePath);
            if (associated is not null)
            {
                return associated;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
