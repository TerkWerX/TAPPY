using Tappy.Windows.Input;

namespace Tappy.App.Services;

public enum ControllerPhotoHotspotShape
{
    RoundedRectangle,
    Ellipse
}

public enum ControllerPhotoControlKind
{
    Button,
    Fader,
    Potentiometer,
    RotaryEncoder
}

public enum ControllerPhotoControlOrientation
{
    Vertical,
    Horizontal
}

public sealed record ControllerPhotoHotspotDefinition(
    string ControlId,
    double Left,
    double Top,
    double Width,
    double Height,
    ControllerPhotoHotspotShape Shape = ControllerPhotoHotspotShape.RoundedRectangle,
    ControllerPhotoControlKind Kind = ControllerPhotoControlKind.Button,
    string? VisualId = null,
    ControllerPhotoControlOrientation Orientation = ControllerPhotoControlOrientation.Vertical,
    double IndicatorWidth = 0,
    double IndicatorHeight = 0,
    double MinimumIndicatorLeft = 0,
    double MinimumIndicatorTop = 0,
    double MaximumIndicatorLeft = 0,
    double MaximumIndicatorTop = 0,
    double RotaryStartAngle = -135,
    double RotarySweepAngle = 270)
{
    public string EffectiveVisualId => string.IsNullOrWhiteSpace(VisualId) ? ControlId : VisualId;
}

public sealed record ControllerPhotoDefinition(
    string Id,
    string AccessibleName,
    string AssetUri,
    double Width,
    double Height,
    IReadOnlyDictionary<string, ControllerPhotoHotspotDefinition> Hotspots);

public static class ControllerPhotoCatalog
{
    public const double LogitechG13PhotoWidth = 1174;
    public const double LogitechG13PhotoHeight = 1339;
    public const double AkaiApcMiniV1PhotoWidth = 1254;
    public const double AkaiApcMiniV1PhotoHeight = 1254;

    private static readonly ControllerPhotoDefinition LogitechG13 = CreateLogitechG13();
    private static readonly ControllerPhotoDefinition AkaiApcMiniV1 = CreateAkaiApcMiniV1();

    public static ControllerPhotoDefinition? Find(
        string? providerId,
        ushort? vendorId,
        ushort? productId,
        string? displayName = null)
    {
        if (string.Equals(providerId, "raw-hid-g13", StringComparison.Ordinal) &&
            vendorId == LogitechG13Protocol.VendorId &&
            productId == LogitechG13Protocol.ProductId)
        {
            return LogitechG13;
        }

        return MidiControllerModelCatalog.Find(providerId, displayName)?.Id ==
               MidiControllerModelCatalog.AkaiApcMiniV1Id
            ? AkaiApcMiniV1
            : null;
    }

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
            "logitech-g13-tight-neutral-photo-v2",
            "Logitech G13 visual control locator",
            "/Tappy;component/Assets/Controllers/logitech-g13-tight-neutral.png",
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
            new(
                controls[control].ControlId.Value,
                90 + (left * 1.16),
                top * 1.05,
                width * 1.16,
                height * 1.05,
                shape);
    }

    private static ControllerPhotoDefinition CreateAkaiApcMiniV1()
    {
        var hotspots = new List<ControllerPhotoHotspotDefinition>();
        var padCentersX = new[] { 128d, 253d, 380d, 507d, 634d, 761d, 888d, 1015d };
        var padCentersY = new[] { 194d, 262d, 330d, 399d, 466d, 535d, 604d, 672d };
        for (var row = 0; row < 8; row++)
        {
            var noteStart = 56 - (row * 8);
            for (var column = 0; column < 8; column++)
            {
                hotspots.Add(Hotspot(
                    Note(noteStart + column),
                    padCentersX[column] - 52,
                    padCentersY[row] - 25,
                    104,
                    50));
            }
        }

        var bottomCentersX = new[] { 128d, 253d, 380d, 507d, 634d, 761d, 888d, 1015d };
        for (var index = 0; index < bottomCentersX.Length; index++)
        {
            hotspots.Add(Hotspot(
                Note(64 + index),
                bottomCentersX[index] - 27,
                716,
                54,
                56,
                ControllerPhotoHotspotShape.Ellipse));
        }

        var sceneCentersY = new[] { 190d, 259d, 326d, 394d, 463d, 532d, 601d, 669d };
        for (var index = 0; index < sceneCentersY.Length; index++)
        {
            hotspots.Add(Hotspot(
                Note(82 + index),
                1117,
                sceneCentersY[index] - 27,
                54,
                54,
                ControllerPhotoHotspotShape.Ellipse));
        }

        hotspots.Add(Hotspot(Note(98), 1113, 716, 60, 60));

        var faderCentersX = new[] { 128d, 253d, 380d, 507d, 634d, 761d, 888d, 1015d, 1143d };
        for (var index = 0; index < faderCentersX.Length; index++)
        {
            var left = faderCentersX[index] - 38;
            var visualId = $"apc-mini-v1:fader-{index + 1}";
            hotspots.Add(FaderHotspot(
                ControlChange(48 + index, increase: true), visualId, left, 802));
            hotspots.Add(FaderHotspot(
                ControlChange(48 + index, increase: false), visualId, left, 802));
        }

        return new ControllerPhotoDefinition(
            "akai-apc-mini-v1-tight-neutral-photo-v2",
            "Akai Professional APC MINI v1 visual control locator",
            "/Tappy;component/Assets/Controllers/akai-apc-mini-v1-neutral.png",
            AkaiApcMiniV1PhotoWidth,
            AkaiApcMiniV1PhotoHeight,
            hotspots.ToDictionary(item => item.ControlId, StringComparer.Ordinal));

        static ControllerPhotoHotspotDefinition Hotspot(
            string controlId,
            double left,
            double top,
            double width,
            double height,
            ControllerPhotoHotspotShape shape = ControllerPhotoHotspotShape.RoundedRectangle) =>
            new(controlId, left, top, width, height, shape);

        static ControllerPhotoHotspotDefinition FaderHotspot(
            string controlId,
            string visualId,
            double left,
            double top) =>
            new(
                controlId,
                left,
                top,
                76,
                252,
                ControllerPhotoHotspotShape.RoundedRectangle,
                ControllerPhotoControlKind.Fader,
                visualId,
                ControllerPhotoControlOrientation.Vertical,
                IndicatorWidth: 64,
                IndicatorHeight: 42,
                MinimumIndicatorLeft: 6,
                MinimumIndicatorTop: 202,
                MaximumIndicatorLeft: 6,
                MaximumIndicatorTop: 8);

        static string Note(int note) =>
            Tappy.Core.Input.ControlId.Create(
                WinMmMidiInputProvider.ProviderId,
                $"channel-1:note-{note}").Value;

        static string ControlChange(int control, bool increase) =>
            Tappy.Core.Input.ControlId.Create(
                WinMmMidiInputProvider.ProviderId,
                $"channel-1:cc-{control}:{(increase ? "increase" : "decrease")}").Value;
    }
}
