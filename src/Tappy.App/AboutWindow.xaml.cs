using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace Tappy.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"}";
        CopyrightText.Text = $"© {DateTime.Now.Year} TerkWerX. All rights reserved.";
    }

    private static void Open(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void TerkWerX_OnClick(object sender, RoutedEventArgs e) => Open("https://www.terkwerx.com/");
    private void GitHub_OnClick(object sender, RoutedEventArgs e) => Open("https://github.com/TerkWerX/TAPPY");
    private void Support_OnClick(object sender, RoutedEventArgs e) => Open("https://github.com/TerkWerX/TAPPY/issues");
    private void PayPal_OnClick(object sender, RoutedEventArgs e) => Open("https://paypal.me/Terkinstein?locale.x=en_US&country.x=US");
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
