using Tappy.App.Services;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class MidiControllerModelCatalogTests
{
    [Fact]
    public void APC_MINI_v1_catalog_has_complete_unique_layout_and_friendly_labels()
    {
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: APC MINI"));

        Assert.Equal(MidiControllerModelCatalog.AkaiApcMiniV1Id, model.Id);
        Assert.Equal(99, model.Controls.Count);
        Assert.Equal(99, model.Controls.Select(item => item.ControlId).Distinct().Count());
        Assert.Equal(99, model.Layout.Rows.SelectMany(row => row.Controls).Count());
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-57" &&
            item.Label == "Pad R1 C2");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-98" &&
            item.Label == "Shift");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:cc-56:decrease" &&
            item.Label == "Master fader ↓");
    }

    [Theory]
    [InlineData("MIDI: APC MINI (device 2)")]
    [InlineData("midi: apc mini")]
    public void APC_MINI_duplicate_suffix_and_case_are_recognized(string displayName)
    {
        Assert.NotNull(MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName));
    }

    [Theory]
    [InlineData("MIDI: APC mini mk2")]
    [InlineData("MIDI: APC KEY 25")]
    [InlineData("MIDI: Launchpad Mini")]
    public void Similar_or_unverified_models_are_not_misidentified(string displayName)
    {
        Assert.Null(MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName));
    }
}
