using Tappy.Windows.Input;

namespace Tappy.App.Services;

public enum ControllerPhotoHotspotShape
{
    RoundedRectangle,
    Ellipse
}

public sealed record ControllerPhotoHotspotDefinition(
    string ControlId,
    double Left,
    double Top,
    double Width,
    double Height,
    ControllerPhotoHotspotShape Shape = ControllerPhotoHotspotShape.RoundedRectangle);

public sealed record ControllerPhotoDefinition(
    string Id,
    string AccessibleName,
    double Width,
    double Height,
    IReadOnlyDictionary<string, ControllerPhotoHotspotDefinition> Hotspots);

public static class ControllerPhotoCatalog
{
    public const double LogitechG13PhotoWidth = 853;
    public const double LogitechG13PhotoHeight = 1275;

    private static readonly ControllerPhotoDefinition LogitechG13 = CreateLogitechG13();

    public static ControllerPhotoDefinition? Find(
        string? providerId,
        ushort? vendorId,
        ushort? productId) =>
        string.Equals(providerId, "raw-hid-g13", StringComparison.Ordinal) &&
        vendorId == LogitechG13Protocol.VendorId &&
        productId == LogitechG13Protocol.ProductId
            ? LogitechG13
            : null;

    private static ControllerPhotoDefinition CreateLogitechG13()
    {
        var controls = LogitechG13InputProvider.SupportedControls
            .ToDictionary(item => item.Control);
        var hotspots = new[]
        {
            Hotspot(LogitechG13Control.LcdNextPage, 188, 178, 56, 56, ControllerPhotoHotspotShape.Ellipse),
            Hotspot(LogitechG13Control.LcdMenuLeft, 282, 181, 70, 34),
            Hotspot(LogitechG13Control.LcdMenu2, 363, 181, 70, 34),
            Hotspot(LogitechG13Control.LcdMenu3, 445, 181, 70, 34),
            Hotspot(LogitechG13Control.LcdMenuRight, 526, 181, 70, 34),
            Hotspot(LogitechG13Control.Lights, 615, 178, 56, 56, ControllerPhotoHotspotShape.Ellipse),

            Hotspot(LogitechG13Control.M1, 205, 235, 99, 30),
            Hotspot(LogitechG13Control.M2, 307, 235, 99, 30),
            Hotspot(LogitechG13Control.M3, 409, 235, 99, 30),
            Hotspot(LogitechG13Control.Mr, 511, 235, 99, 30),

            Hotspot(LogitechG13Control.G1, 91, 300, 86, 78),
            Hotspot(LogitechG13Control.G2, 181, 305, 86, 78),
            Hotspot(LogitechG13Control.G3, 276, 306, 86, 78),
            Hotspot(LogitechG13Control.G4, 374, 308, 86, 78),
            Hotspot(LogitechG13Control.G5, 472, 306, 86, 78),
            Hotspot(LogitechG13Control.G6, 570, 305, 86, 78),
            Hotspot(LogitechG13Control.G7, 668, 300, 89, 78),

            Hotspot(LogitechG13Control.G8, 78, 388, 98, 82),
            Hotspot(LogitechG13Control.G9, 177, 394, 92, 82),
            Hotspot(LogitechG13Control.G10, 274, 398, 92, 82),
            Hotspot(LogitechG13Control.G11, 372, 400, 92, 82),
            Hotspot(LogitechG13Control.G12, 470, 399, 92, 82),
            Hotspot(LogitechG13Control.G13, 568, 394, 92, 82),
            Hotspot(LogitechG13Control.G14, 665, 387, 106, 82),

            Hotspot(LogitechG13Control.G15, 120, 480, 126, 88),
            Hotspot(LogitechG13Control.G16, 255, 486, 116, 88),
            Hotspot(LogitechG13Control.G17, 371, 488, 99, 88),
            Hotspot(LogitechG13Control.G18, 476, 486, 112, 88),
            Hotspot(LogitechG13Control.G19, 587, 479, 132, 88),

            Hotspot(LogitechG13Control.G20, 198, 574, 150, 94),
            Hotspot(LogitechG13Control.G21, 354, 580, 98, 94),
            Hotspot(LogitechG13Control.G22, 464, 574, 154, 94),

            Hotspot(LogitechG13Control.JoystickLeftSide, 641, 700, 67, 144),
            Hotspot(LogitechG13Control.JoystickBottomSide, 704, 830, 116, 82),
            Hotspot(LogitechG13Control.JoystickPress, 704, 674, 116, 116, ControllerPhotoHotspotShape.Ellipse),
            Hotspot(LogitechG13Control.StickLeft, 697, 710, 48, 60, ControllerPhotoHotspotShape.Ellipse),
            Hotspot(LogitechG13Control.StickRight, 779, 710, 48, 60, ControllerPhotoHotspotShape.Ellipse),
            Hotspot(LogitechG13Control.StickUp, 738, 669, 60, 48, ControllerPhotoHotspotShape.Ellipse),
            Hotspot(LogitechG13Control.StickDown, 738, 775, 60, 48, ControllerPhotoHotspotShape.Ellipse),
        };

        return new ControllerPhotoDefinition(
            "logitech-g13-user-photo-v1",
            "Logitech G13 visual control locator",
            LogitechG13PhotoWidth,
            LogitechG13PhotoHeight,
            hotspots.ToDictionary(item => item.ControlId, StringComparer.Ordinal));

        ControllerPhotoHotspotDefinition Hotspot(
            LogitechG13Control control,
            double left,
            double top,
            double width,
            double height,
            ControllerPhotoHotspotShape shape = ControllerPhotoHotspotShape.RoundedRectangle) =>
            new(controls[control].ControlId.Value, left, top, width, height, shape);
    }
}
