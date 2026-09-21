using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Tappy.App.Services;

/// <summary>Render-time viewport only; the supplied photograph remains byte-identical.</summary>
public sealed class ControllerPhotoSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string uri || string.IsNullOrWhiteSpace(uri)) { return DependencyProperty.UnsetValue; }
        var source = new BitmapImage(new Uri(uri.StartsWith('/') ? "pack://application:,,," + uri : uri, UriKind.Absolute));
        var viewport = uri.EndsWith("targus-pauk10u-front.png", StringComparison.OrdinalIgnoreCase) ? new Int32Rect(350, 0, 670, 880) :
            uri.EndsWith("microsoft-bluetooth-number-pad.png", StringComparison.OrdinalIgnoreCase) ? new Int32Rect(330, 0, 720, 880) :
            uri.EndsWith("razer-tartarus-original.png", StringComparison.OrdinalIgnoreCase) ? new Int32Rect(320, 0, 740, 880) : Int32Rect.Empty;
        if (viewport.IsEmpty) { return source; }
        var crop = new CroppedBitmap(source, viewport);
        crop.Freeze();
        return crop;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
