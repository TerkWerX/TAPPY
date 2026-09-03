using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Tappy.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText.Text = $"Tappy {version} · by TerkWerX.com";
        CopyrightText.Text = $"© {DateTime.Now.Year} TerkWerX";
    }

    public event EventHandler? Dismissed;

    private void Dismiss_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);

    private void Dismiss_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }
}
