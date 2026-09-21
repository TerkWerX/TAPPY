using System.Buffers.Binary;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class ControllerPhotoCatalogTests
{
    [Fact]
    public void Targus_front_photo_has_disjoint_key_faces_and_one_visual_per_physical_key()
    {
        var photo = ControllerPhotoCatalog.Find("raw-input", 0x05A4, 0x9862)!;
        Assert.EndsWith("targus-pauk10u-front.png", photo.AssetUri);
        Assert.Equal(670, photo.Width);
        Assert.Equal(880, photo.Height);
        var faces = photo.Hotspots.Values.DistinctBy(face => face.EffectiveVisualId).ToArray();
        Assert.Equal(19, faces.Length);
        foreach (var face in faces)
        {
            Assert.InRange(face.Left, 0, photo.Width - face.Width);
            Assert.InRange(face.Top, 0, photo.Height - face.Height);
            foreach (var other in faces.Where(other => other != face))
            {
                Assert.False(face.Left < other.Left + other.Width && other.Left < face.Left + face.Width &&
                    face.Top < other.Top + other.Height && other.Top < face.Top + face.Height,
                    $"Overlapping Targus photo faces: {face.ControlId}, {other.ControlId}");
            }
        }
        var enter = photo.Hotspots[TargusKeypadLayout.Key(0x1C, true)];
        Assert.Equal(221, enter.Height);
        Assert.Equal(574, enter.Top);
        var plainEnter = photo.Hotspots[TargusKeypadLayout.Key(0x1C)];
        Assert.Equal(enter.EffectiveVisualId, plainEnter.EffectiveVisualId);
        Assert.Equal(enter.Left, plainEnter.Left);
        Assert.Equal(enter.Top, plainEnter.Top);
        foreach (ushort scan in new ushort[] { 0x47, 0x48, 0x49, 0x4B, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53 })
        {
            Assert.Equal(photo.Hotspots[TargusKeypadLayout.Key(scan)].EffectiveVisualId,
                photo.Hotspots[TargusKeypadLayout.Key(scan, true)].EffectiveVisualId);
        }
    }

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
    public void Exact_Fire_name_has_one_bounded_hotspot_per_known_control()
    {
        var photo = Assert.IsType<ControllerPhotoDefinition>(ControllerPhotoCatalog.Find(
            WinMmMidiInputProvider.ProviderId,
            1,
            2,
            "MIDI: FL STUDIO FIRE"));

        Assert.Equal("akai-fire-v1-neutral-photo-v1", photo.Id);
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: FL STUDIO FIRE"));
        Assert.Equal(99, model.Controls.Count);
        // Every model control — the 20 buttons, 64 pads, and now the five
        // encoders' 15 controls (touch + increase + decrease each) — has a
        // measured photo hotspot. See docs/AKAI_FIRE.md.
        Assert.Equal(99, photo.Hotspots.Count);
        Assert.Equal("/Tappy;component/Assets/Controllers/akai-fire-neutral.png", photo.AssetUri);
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

        // Sibling controls at the same physical knob (rotate clockwise, rotate
        // counter-clockwise, touch) intentionally occupy the identical spot —
        // they are the same visual hotspot, not overlapping distinct ones — so
        // this check is scoped to distinct visual spots, matching how the APC
        // MINI's fader increase/decrease pair is checked.
        var visualSpots = photo.Hotspots.Values.DistinctBy(hotspot => hotspot.EffectiveVisualId).ToArray();
        foreach (var other in visualSpots)
        {
            var overlapping = visualSpots.Where(candidate => candidate != other &&
                candidate.Left < other.Left + other.Width && other.Left < candidate.Left + candidate.Width &&
                candidate.Top < other.Top + other.Height && other.Top < candidate.Top + candidate.Height);
            Assert.Empty(overlapping);
        }
    }

    [Fact]
    public void Fire_encoders_share_one_rotary_visual_spot_across_touch_and_both_rotation_directions()
    {
        var photo = Assert.IsType<ControllerPhotoDefinition>(ControllerPhotoCatalog.Find(
            WinMmMidiInputProvider.ProviderId,
            1,
            2,
            "MIDI: FL STUDIO FIRE"));

        var rotaryHotspots = photo.Hotspots.Values
            .Where(hotspot => hotspot.Kind == ControllerPhotoControlKind.RotaryEncoder)
            .ToArray();
        Assert.Equal(10, rotaryHotspots.Length); // 5 encoders x (clockwise + counter-clockwise)
        var visualGroups = rotaryHotspots.GroupBy(hotspot => hotspot.EffectiveVisualId).ToArray();
        Assert.Equal(5, visualGroups.Length);
        Assert.All(visualGroups, group =>
        {
            Assert.Equal(2, group.Count());
            var first = group.First();
            Assert.All(group, hotspot =>
            {
                Assert.Equal(ControllerPhotoHotspotShape.Ellipse, hotspot.Shape);
                Assert.Equal(first.Left, hotspot.Left);
                Assert.Equal(first.Top, hotspot.Top);
                Assert.Equal(first.Width, hotspot.Width);
                Assert.Equal(first.Height, hotspot.Height);
                // An endless relative encoder never has a known absolute
                // position, so its indicator angle must come entirely from
                // ApplyEncoderDelta, not a calibrated min/max sweep.
                Assert.Equal(0, hotspot.MinimumIndicatorLeft);
                Assert.Equal(0, hotspot.MaximumIndicatorLeft);
                Assert.Equal(0, hotspot.MinimumIndicatorTop);
                Assert.Equal(0, hotspot.MaximumIndicatorTop);
            });

            // The matching touch control shares the same visual spot too, as a
            // Button rather than a RotaryEncoder — it is a discrete press, not
            // a rotation, but still lives at the same physical knob.
            var touch = Assert.Single(photo.Hotspots.Values, hotspot =>
                hotspot.Kind == ControllerPhotoControlKind.Button &&
                hotspot.EffectiveVisualId == first.EffectiveVisualId);
            Assert.Equal(first.Left, touch.Left);
            Assert.Equal(first.Top, touch.Top);
        });
    }

    [Fact]
    public void Embedded_Fire_asset_is_the_neutral_rgba_source_at_its_processed_canvas_size()
    {
        var bytes = File.ReadAllBytes(SourcePath(
            "src", "Tappy.App", "Assets", "Controllers", "akai-fire-neutral.png"));

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
        Assert.Equal(1600, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(854, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
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
