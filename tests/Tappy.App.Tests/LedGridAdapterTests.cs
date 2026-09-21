using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class LedGridAdapterTests
{
    [Fact]
    public void FireAdapterPadAspectRatioMatchesItsOwnMeasuredPhotoHotspot()
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiFireV1Id)!;
        var photo = ControllerPhotoCatalog.Find(WinMmMidiInputProvider.ProviderId, 1, 2, "MIDI: FL STUDIO FIRE")!;
        var controlId = adapter.ControlIdAt(0, 0)!.Value.Value;
        var hotspot = photo.Hotspots[controlId];

        // Two independently-derived numbers (the adapter's constant and the
        // photo's own measured hotspot) must agree, so a future re-measurement
        // of one can't silently drift from the other.
        Assert.Equal(hotspot.Width / hotspot.Height, adapter.PadAspectRatio, 3);
        Assert.True(adapter.PadAspectRatio < 1, "The Fire's pads are tall/portrait — width should be less than height.");
    }

    [Fact]
    public void ApcMiniAdapterPadAspectRatioMatchesItsOwnMeasuredPhotoHotspot()
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiApcMiniV1Id)!;
        var photo = ControllerPhotoCatalog.Find(WinMmMidiInputProvider.ProviderId, 1, 2, "MIDI: APC MINI")!;
        var controlId = adapter.ControlIdAt(0, 0)!.Value.Value;
        var hotspot = photo.Hotspots[controlId];

        Assert.Equal(hotspot.Width / hotspot.Height, adapter.PadAspectRatio, 3);
        Assert.True(adapter.PadAspectRatio > 1, "The APC MINI's pads are wide/landscape — width should be greater than height.");
    }


    [Fact]
    public void FireAdapterCellsRoundTripThroughTheExistingPadIndexParser()
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiFireV1Id);
        Assert.NotNull(adapter);
        Assert.Equal(16, adapter!.Columns);
        Assert.Equal(4, adapter.Rows);

        for (var row = 0; row < adapter.Rows; row++)
        {
            for (var column = 0; column < adapter.Columns; column++)
            {
                var controlId = adapter.ControlIdAt(column, row);
                Assert.NotNull(controlId);
                var padIndex = DeviceAwareControllerRuntime.TryParseAkaiFirePadIndex(controlId!.Value.Value);
                Assert.Equal((row * 16) + column, padIndex);
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(16, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    public void FireAdapterReturnsNullOutsideTheGrid(int column, int row)
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiFireV1Id)!;
        Assert.Null(adapter.ControlIdAt(column, row));
    }

    [Fact]
    public void ApcMiniAdapterCellsRoundTripThroughTheExistingLedNoteParser()
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiApcMiniV1Id);
        Assert.NotNull(adapter);
        Assert.Equal(8, adapter!.Columns);
        Assert.Equal(8, adapter.Rows);

        for (var row = 0; row < adapter.Rows; row++)
        {
            for (var column = 0; column < adapter.Columns; column++)
            {
                var controlId = adapter.ControlIdAt(column, row);
                Assert.NotNull(controlId);
                Assert.NotNull(DeviceAwareControllerRuntime.TryParseApcMiniLedNote(controlId!.Value.Value));
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(8, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 8)]
    public void ApcMiniAdapterReturnsNullOutsideTheGrid(int column, int row)
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiApcMiniV1Id)!;
        Assert.Null(adapter.ControlIdAt(column, row));
    }

    [Theory]
    [InlineData("green", "Green")]
    [InlineData("teal", "Green")]
    [InlineData("red", "Red")]
    [InlineData("pink", "Red")]
    [InlineData("amber", "Amber")]
    [InlineData("orange", "Amber")]
    [InlineData("#FF0000", "Red")]
    [InlineData("#00FF00", "Green")]
    [InlineData("#000000", "Default")]
    public void ApcMiniAdapterDowngradesToItsFourColorPalette(string requested, string expected)
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiApcMiniV1Id)!;
        Assert.Equal(expected, adapter.ResolveColor(requested, supportsFreeformColor: false));
    }

    [Fact]
    public void FireAdapterPassesArbitraryColorsThroughUnchanged()
    {
        var adapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiFireV1Id)!;
        Assert.Equal("#123456", adapter.ResolveColor("#123456", supportsFreeformColor: true));
    }

    [Theory]
    [InlineData("akai-fire-v1")]
    [InlineData("akai-apc-mini-v1")]
    public void CatalogResolvesKnownModelIds(string modelId) =>
        Assert.NotNull(LedGridAdapterCatalog.Find(modelId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("logitech-g13-global-rgb")]
    [InlineData("razer-tartarus-original")]
    [InlineData("some-unknown-model")]
    public void CatalogHasNoAdapterForNonGridModels(string? modelId) =>
        Assert.Null(LedGridAdapterCatalog.Find(modelId));
}
