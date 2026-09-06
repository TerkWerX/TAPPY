using System.Buffers.Binary;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class ControllerPhotoCatalogTests
{
    [Fact]
    public void Exact_physical_G13_identity_has_one_bounded_hotspot_per_supported_control()
    {
        var photo = Assert.IsType<ControllerPhotoDefinition>(ControllerPhotoCatalog.Find(
            "raw-hid-g13",
            LogitechG13Protocol.VendorId,
            LogitechG13Protocol.ProductId));

        Assert.Equal("logitech-g13-tight-neutral-photo-v2", photo.Id);
        Assert.Equal(39, photo.Hotspots.Count);
        Assert.Equal(
            LogitechG13InputProvider.SupportedControls.Select(item => item.ControlId.Value).Order(),
            photo.Hotspots.Keys.Order());
        Assert.All(photo.Hotspots.Values, hotspot =>
        {
            Assert.True(hotspot.Width > 0);
            Assert.True(hotspot.Height > 0);
            Assert.InRange(hotspot.Left, 0, photo.Width - hotspot.Width);
            Assert.InRange(hotspot.Top, 0, photo.Height - hotspot.Height);
        });
    }

    [Fact]
    public void Exact_APC_MINI_name_has_one_bounded_hotspot_per_known_input_direction()
    {
        var photo = Assert.IsType<ControllerPhotoDefinition>(ControllerPhotoCatalog.Find(
            WinMmMidiInputProvider.ProviderId,
            1,
            2,
            "MIDI: APC MINI"));

        Assert.Equal("akai-apc-mini-v1-tight-neutral-photo-v2", photo.Id);
        Assert.Equal(99, photo.Hotspots.Count);
        Assert.Equal("/Tappy;component/Assets/Controllers/akai-apc-mini-v1-neutral.png", photo.AssetUri);
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: APC MINI"));
        Assert.Equal(
            model.Controls.Select(item => item.ControlId.Value).Order(),
            photo.Hotspots.Keys.Order());
        Assert.All(photo.Hotspots.Values, hotspot =>
        {
            Assert.True(hotspot.Width > 0);
            Assert.True(hotspot.Height > 0);
            Assert.InRange(hotspot.Left, 0, photo.Width - hotspot.Width);
            Assert.InRange(hotspot.Top, 0, photo.Height - hotspot.Height);
        });

        var faderDirections = photo.Hotspots.Values
            .Where(hotspot => hotspot.Kind == ControllerPhotoControlKind.Fader)
            .ToArray();
        Assert.Equal(18, faderDirections.Length);
        Assert.Equal(9, faderDirections.Select(hotspot => hotspot.EffectiveVisualId).Distinct().Count());
        Assert.All(faderDirections.GroupBy(hotspot => hotspot.EffectiveVisualId), group =>
        {
            Assert.Equal(2, group.Count());
            Assert.All(group, hotspot =>
            {
                Assert.Equal(ControllerPhotoControlOrientation.Vertical, hotspot.Orientation);
                Assert.True(hotspot.IndicatorHeight < hotspot.Height);
                Assert.True(hotspot.MinimumIndicatorTop > hotspot.MaximumIndicatorTop);
            });
        });
    }

    [Fact]
    public void Embedded_APC_MINI_asset_has_the_reviewed_tight_neutral_square_PNG_canvas()
    {
        var bytes = File.ReadAllBytes(SourcePath(
            "src", "Tappy.App", "Assets", "Controllers", "akai-apc-mini-v1-neutral.png"));

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
        Assert.Equal(1254, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1254, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(8, bytes[24]);
    }

    [Fact]
    public void Reusable_analog_visuals_support_calibrated_pots_and_endless_rotary_motion()
    {
        var potTile = new ControlTileViewModel
        {
            ControlId = "pot",
            Label = "Pot",
            AnalogRawAtMinimum = 10,
            AnalogRawAtCenter = 40,
            AnalogRawAtMaximum = 110,
            AnalogRawValue = 40,
        };
        using var pot = new ControllerPhotoHotspotViewModel(
            potTile,
            new ControllerPhotoHotspotDefinition(
                "pot", 0, 0, 80, 80,
                ControllerPhotoHotspotShape.Ellipse,
                ControllerPhotoControlKind.Potentiometer));

        Assert.True(pot.IsRotary);
        Assert.Equal(0.5, potTile.AnalogPosition);
        Assert.Equal(0, pot.IndicatorAngle);

        var encoderTile = new ControlTileViewModel
        {
            ControlId = "encoder:cw",
            Label = "Encoder clockwise",
            EncoderDegreesPerStep = 15,
        };
        using var encoder = new ControllerPhotoHotspotViewModel(
            encoderTile,
            new ControllerPhotoHotspotDefinition(
                "encoder:cw", 0, 0, 80, 80,
                ControllerPhotoHotspotShape.Ellipse,
                ControllerPhotoControlKind.RotaryEncoder,
                "encoder"));

        encoderTile.ApplyEncoderDelta(1);
        Assert.Equal(15, encoder.IndicatorAngle);
        encoderTile.EncoderReversed = true;
        encoderTile.ApplyEncoderDelta(1);
        Assert.Equal(0, encoder.IndicatorAngle);
        encoderTile.ApplyEncoderDelta(-1);
        Assert.Equal(15, encoder.IndicatorAngle);
    }

    [Fact]
    public void Photo_catalog_refuses_similar_or_unknown_controller_identities()
    {
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x046D, 0xC21C));
        Assert.Null(ControllerPhotoCatalog.Find("raw-hid-g13", 0x046D, 0xC232));
        Assert.Null(ControllerPhotoCatalog.Find("raw-hid-g13", 0x1532, 0x0201));
        Assert.Null(ControllerPhotoCatalog.Find(null, null, null));
    }

    [Fact]
    public void Embedded_G13_asset_is_the_tight_neutral_rgba_source_instead_of_an_opaque_checkerboard()
    {
        var bytes = File.ReadAllBytes(SourcePath(
            "src", "Tappy.App", "Assets", "Controllers", "logitech-g13-tight-neutral.png"));

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
        Assert.Equal(1174, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1339, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tappy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. segments]);
    }
}
