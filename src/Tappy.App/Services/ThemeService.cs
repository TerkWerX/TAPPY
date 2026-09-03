using System.Windows;

namespace Tappy.App.Services;

public sealed class ThemeService : IDisposable
{
    private bool _isLight;

    public ThemeService() => SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

    public bool IsLight => _isLight;

    public void Toggle()
    {
        _isLight = !_isLight;
        Apply(_isLight);
    }

    public static void Apply(bool light)
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        var theme = SelectResourcePath(light, SystemParameters.HighContrast);
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(theme, UriKind.Relative)
        });
    }

    public void Dispose() => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

    internal static string SelectResourcePath(bool light, bool highContrast) => highContrast
        ? "Themes/HighContrast.xaml"
        : light ? "Themes/Light.xaml" : "Themes/Dark.xaml";

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Apply(_isLight);
        }
    }
}
