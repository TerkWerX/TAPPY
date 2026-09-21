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
    public const double AkaiFireV1PhotoWidth = 1600;
    public const double AkaiFireV1PhotoHeight = 854;

    private static readonly ControllerPhotoDefinition LogitechG13 = CreateLogitechG13();
    private static readonly ControllerPhotoDefinition AkaiApcMiniV1 = CreateAkaiApcMiniV1();
    private static readonly ControllerPhotoDefinition AkaiFireV1 = CreateAkaiFireV1();
    private static readonly ControllerPhotoDefinition TargusPauk10u = CreateTargus();
    private static readonly ControllerPhotoDefinition MicrosoftNumberPad = CreateMicrosoftNumberPad();
    private static readonly ControllerPhotoDefinition RazerTartarus = CreateRazerTartarus();

    public static ControllerPhotoDefinition? Find(
        string? providerId,
        ushort? vendorId,
        ushort? productId,
        string? displayName = null,
        string? presentationModelId = null)
    {
        if (RazerTartarusLayout.Matches(providerId, vendorId, productId)) { return RazerTartarus; }
        if (providerId == "raw-input" && presentationModelId == KeyboardPresentationModels.MicrosoftBluetoothNumberPad)
        { return MicrosoftNumberPad; }
        if (TargusKeypadLayout.Matches(providerId, vendorId, productId)) { return TargusPauk10u; }
        if (string.Equals(providerId, "raw-hid-g13", StringComparison.Ordinal) &&
            vendorId == LogitechG13Protocol.VendorId &&
            productId == LogitechG13Protocol.ProductId)
        {
            return LogitechG13;
        }

        var midiModel = MidiControllerModelCatalog.Find(providerId, displayName)?.Id;
        if (midiModel == MidiControllerModelCatalog.AkaiApcMiniV1Id) { return AkaiApcMiniV1; }
        if (midiModel == MidiControllerModelCatalog.AkaiFireV1Id) { return AkaiFireV1; }
        return null;
    }

    private static ControllerPhotoDefinition CreateRazerTartarus()
    {
        // Owner's original black-key photograph, cropped only at render time (x=320).
        // Inset faces follow the photographed perspective; no fake white/RGB pads.
        ushort[] scans = [0x0F, 0x10, 0x11, 0x12, 0x13, 0x3A, 0x1E, 0x1F, 0x20, 0x21, 0x2A, 0x2C, 0x2D, 0x2E, 0x2F];
        (double X, double Y)[] centers = [(415,137), (500,105), (595,95), (684,86), (778,79),
            (423,220), (510,189), (603,180), (691,173), (780,170),
            (435,308), (520,284), (608,274), (691,265), (780,257)];
        var spots = scans.Select((scan, i) => new ControllerPhotoHotspotDefinition(RazerTartarusLayout.Key(scan),
            centers[i].X - 320 - 31, centers[i].Y - 29, 62, 58)).ToList();
        spots.Add(new(RazerTartarusLayout.Key(0x38), 538, 385, 45, 28));
        spots.Add(new(RazerTartarusLayout.Key(0x39), 590, 555, 68, 45));
        spots.Add(new(RazerTartarusLayout.Key(0x4B, true), 572, 436, 18, 18, ControllerPhotoHotspotShape.Ellipse));
        spots.Add(new(RazerTartarusLayout.Key(0x48, true), 597, 450, 18, 18, ControllerPhotoHotspotShape.Ellipse));
        spots.Add(new(RazerTartarusLayout.Key(0x4D, true), 580, 479, 18, 18, ControllerPhotoHotspotShape.Ellipse));
        spots.Add(new(RazerTartarusLayout.Key(0x50, true), 555, 465, 18, 18, ControllerPhotoHotspotShape.Ellipse));
        return new("razer-tartarus-original", "Razer Tartarus RZ07-0103 — factory key assignments",
            "/Tappy;component/Assets/Controllers/razer-tartarus-original.png", 740, 880,
            spots.ToDictionary(h => h.ControlId, StringComparer.Ordinal));
    }

    private static ControllerPhotoDefinition CreateMicrosoftNumberPad()
    {
        // Original owner-supplied PNG is untouched. Coordinates reference its
        // x=330 render viewport; black key faces remain black, not recolored white.
        double[] x = [100, 232, 363, 501];
        double[] y = [135, 265, 393, 521, 650];
        var hotspots = MicrosoftNumberPadLayout.Create().Rows.SelectMany(row => row.Controls).Select(key =>
        {
            var col = (int)(key.X!.Value / 120);
            var row = (int)(key.Y!.Value / 98);
            return new ControllerPhotoHotspotDefinition(key.ControlId!.Value.Value, x[col], y[row],
                key.Width * 134 > 200 ? 250 : 108, key.Height * 106 > 100 ? 226 : 105);
        }).ToDictionary(h => h.ControlId, StringComparer.Ordinal);
        void Alias(ushort scan, bool fromExtended, bool toExtended)
        {
            var face = hotspots[MicrosoftNumberPadLayout.Key(scan, fromExtended)];
            var id = MicrosoftNumberPadLayout.Key(scan, toExtended);
            hotspots[id] = face with { ControlId = id, VisualId = face.ControlId };
        }
        Alias(0x45, false, true);
        Alias(0x1C, true, false);
        foreach (ushort scan in new ushort[] { 0x47, 0x48, 0x49, 0x4B, 0x4C, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53 })
        { Alias(scan, false, true); }
        return new("microsoft-bluetooth-number-pad", "Microsoft Bluetooth Number Pad (X821504-001)",
            "/Tappy;component/Assets/Controllers/microsoft-bluetooth-number-pad.png", 720, 880, hotspots);
    }

    private static ControllerPhotoDefinition CreateTargus()
    {
        var keys = TargusKeypadLayout.CreatePhysical().Rows.SelectMany(row => row.Controls).ToArray();
        // Inset key-face rectangles measured on the owner's straight-on ITEM NO
        // 10 photo, relative to its x=350 crop. Do not apply perspective offsets.
        double[] left = [64, 204, 346, 487];
        double[] top = [210, 331, 453, 574, 696];
        var hotspots = keys.Select(key =>
        {
            var column = (int)(key.X!.Value / 120);
            var row = (int)(key.Y!.Value / 98);
            return new ControllerPhotoHotspotDefinition(key.ControlId!.Value.Value,
                left[column], top[row], 115, key.Height > 1 ? 221 : 99);
        }).ToDictionary(key => key.ControlId, StringComparer.Ordinal);
        // Num Lock can be reported without E0 by keyboard firmware.
        var numLock = hotspots[TargusKeypadLayout.Key(0x45, true)];
        hotspots[TargusKeypadLayout.Key(0x45)] = numLock with
        { ControlId = TargusKeypadLayout.Key(0x45), VisualId = numLock.ControlId };
        // TESTBED's saved PAUK10U reports Enter without E0. Both encodings
        // represent the same photographed Enter key, never another rectangle.
        var enter = hotspots[TargusKeypadLayout.Key(0x1C, true)];
        hotspots[TargusKeypadLayout.Key(0x1C)] = enter with
        { ControlId = TargusKeypadLayout.Key(0x1C), VisualId = enter.ControlId };
        // PAUK10U Num Lock-off reports navigation keys with E0. These are
        // alternate reports from the same faces, not extra physical buttons.
        foreach (ushort scan in new ushort[] { 0x47, 0x48, 0x49, 0x4B, 0x4C, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53 })
        {
            var face = hotspots[TargusKeypadLayout.Key(scan)];
            var alias = TargusKeypadLayout.Key(scan, true);
            hotspots[alias] = face with { ControlId = alias, VisualId = face.ControlId };
        }
        return new ControllerPhotoDefinition("targus-pauk10u", "Targus PAUK10U numeric keypad",
            "/Tappy;component/Assets/Controllers/targus-pauk10u-front.png", 670, 880, hotspots);
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

    /// <summary>
    /// Every control in the Phase 1a input model in MidiControllerModelCatalog:
    /// buttons, pads, and the five rotary encoders. Coordinates were measured
    /// directly on the processed photo (column/row brightness-peak detection
    /// for the regular 64-pad grid and for the header text sitting directly
    /// above each encoder; visual measurement, confirmed by overlaying a
    /// candidate circle/rectangle on the photo and checking it against the
    /// actual control, for the irregularly placed buttons and the encoders'
    /// own low-contrast knob silhouettes). None of this has been physically
    /// confirmed against the hardware yet — see docs/AKAI_FIRE.md.
    /// </summary>
    private static ControllerPhotoDefinition CreateAkaiFireV1()
    {
        var hotspots = new List<ControllerPhotoHotspotDefinition>();
        double[] padColumnX = [197, 283, 370, 456, 543, 629, 716, 802, 888, 975, 1061, 1147, 1233, 1319, 1405, 1491];
        double[] padRowY = [292, 401, 511, 620];
        const double padWidth = 58, padHeight = 81;
        for (var row = 0; row < 4; row++)
        {
            var noteStart = 0x36 + (row * 16);
            for (var column = 0; column < 16; column++)
            {
                hotspots.Add(new ControllerPhotoHotspotDefinition(
                    Note(noteStart + column),
                    padColumnX[column] - (padWidth / 2),
                    padRowY[row] - (padHeight / 2),
                    padWidth,
                    padHeight));
            }
        }

        Button(0x1A, 86, 197, 65, 26);    // Modes / Bank
        Button(0x20, 855, 96, 55, 30);    // Pattern ▲
        Button(0x1F, 855, 183, 55, 30);   // Pattern ▼
        Button(0x21, 1168, 140, 78, 60);  // Browser
        Button(0x22, 1395, 140, 85, 30);  // Grid Left
        Button(0x23, 1507, 140, 85, 30);  // Grid Right
        for (var row = 0; row < 4; row++)
        {
            // Circular button, not the separate rectangular bank-LED indicator
            // immediately to its right (photo x≈130-145) — measured and
            // confirmed against the photo 2026-09-21; the two are visually
            // adjacent but distinct elements. See docs/AKAI_FIRE.md.
            const double radius = 23;
            hotspots.Add(new ControllerPhotoHotspotDefinition(
                Note(0x24 + row), 78 - radius, padRowY[row] - radius, radius * 2, radius * 2,
                ControllerPhotoHotspotShape.Ellipse));
        }

        double[] bottomLeftX = [88, 204, 320, 438, 554, 672];
        for (var index = 0; index < bottomLeftX.Length; index++)
        {
            Button(0x2C + index, bottomLeftX[index], 745, 66, 56); // Step..Alt
        }

        double[] bottomRightX = [1156, 1274, 1390, 1506];
        for (var index = 0; index < bottomRightX.Length; index++)
        {
            Button(0x32 + index, bottomRightX[index], 745, 66, 56); // Pattern Song..Record
        }

        // Centers found by column-brightness-peak detection on the header text
        // directly above each knob (VOLUME/PAN/LOW EQ/HIGH EQ, which reliably
        // contrasts against the near-black chassis, unlike the knobs' own low-
        // contrast gradient), then confirmed by drawing a candidate circle over
        // the photo and visually checking it against the actual knob silhouette
        // for all five. Each of the three controls an encoder exposes (rotate
        // clockwise, rotate counter-clockwise, touch) shares this one circular
        // spot via a common VisualId — the clockwise control is the canonical
        // entry other two point back to — matching the alias pattern the Targus
        // NumLock/Enter hotspots already use elsewhere in this file. Endless
        // relative encoders never set AnalogRawValue (see
        // MidiControllerModelCatalog's CreateAkaiFireV1 for why), so
        // RotaryEncoder's indicator angle comes entirely from ApplyEncoderDelta,
        // not the Minimum/MaximumIndicator fields a calibrated Potentiometer
        // would need — none of those are set here.
        (string Label, int TouchNote, int RotationCc, double X, double Y, double Radius)[] encoders =
        [
            ("Volume", 0x10, 0x10, 215, 138, 52),
            ("Pan", 0x11, 0x11, 391, 138, 52),
            ("Filter", 0x12, 0x12, 567, 138, 52),
            ("Resonance", 0x13, 0x13, 743, 138, 52),
            ("Select", 0x19, 0x76, 1280, 138, 48),
        ];
        foreach (var (_, touchNote, rotationCc, x, y, radius) in encoders)
        {
            var clockwise = ControlChange(rotationCc, increase: true);
            var diameter = radius * 2;
            var left = x - radius;
            var top = y - radius;
            hotspots.Add(new ControllerPhotoHotspotDefinition(
                clockwise, left, top, diameter, diameter,
                ControllerPhotoHotspotShape.Ellipse, ControllerPhotoControlKind.RotaryEncoder));
            hotspots.Add(new ControllerPhotoHotspotDefinition(
                ControlChange(rotationCc, increase: false), left, top, diameter, diameter,
                ControllerPhotoHotspotShape.Ellipse, ControllerPhotoControlKind.RotaryEncoder, clockwise));
            hotspots.Add(new ControllerPhotoHotspotDefinition(
                Note(touchNote), left, top, diameter, diameter,
                ControllerPhotoHotspotShape.Ellipse, ControllerPhotoControlKind.Button, clockwise));
        }

        return new ControllerPhotoDefinition(
            "akai-fire-v1-neutral-photo-v1",
            "Akai Professional Fire visual control locator",
            "/Tappy;component/Assets/Controllers/akai-fire-neutral.png",
            AkaiFireV1PhotoWidth,
            AkaiFireV1PhotoHeight,
            hotspots.ToDictionary(item => item.ControlId, StringComparer.Ordinal));

        void Button(int note, double centerX, double centerY, double width, double height) =>
            hotspots.Add(new ControllerPhotoHotspotDefinition(
                Note(note), centerX - (width / 2), centerY - (height / 2), width, height));

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
