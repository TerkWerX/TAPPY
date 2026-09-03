using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class LogitechG13ReportDecoderTests
{
    private static readonly int[] ExpectedButtonBits =
    [
        .. Enumerable.Range(0, 22),
        .. Enumerable.Range(24, 12),
        37,
    ];

    [Fact]
    public void CatalogHasCanonicalUniqueProviderAndPhysicalControlIds()
    {
        var controls = LogitechG13InputProvider.SupportedControls;

        Assert.Equal(39, controls.Count);
        Assert.Equal(39, controls.Select(control => control.Control).Distinct().Count());
        Assert.Equal(39, controls.Select(control => control.ControlId).Distinct().Count());
        Assert.Equal(ExpectedButtonBits, controls
            .Where(control => control.ButtonBitIndex is not null)
            .Select(control => control.ButtonBitIndex!.Value));
        Assert.All(controls, definition =>
        {
            Assert.StartsWith("raw-hid-g13:g13:upff00:u0000:r01:", definition.ControlId.Value);
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
        });
    }

    [Fact]
    public void EveryDocumentedButtonBitProducesItsNamedPressAndRelease()
    {
        foreach (var definition in LogitechG13ReportDecoder.Controls.Where(
                     definition => definition.ButtonBitIndex is not null))
        {
            var decoder = new LogitechG13ReportDecoder();
            var bit = definition.ButtonBitIndex!.Value;

            var press = Assert.Single(decoder.Process(Packet(1UL << bit), out _));
            var release = Assert.Single(decoder.Process(Packet(0), out _));

            Assert.Equal(definition, press.Definition);
            Assert.Equal(ControlSignalKind.Press, press.Kind);
            Assert.Equal(definition, release.Definition);
            Assert.Equal(ControlSignalKind.Release, release.Kind);
        }
    }

    [Fact]
    public void ReservedStatusAndUndefinedBitsNeverBecomeControls()
    {
        var decoder = new LogitechG13ReportDecoder();
        const ulong excluded = (1UL << 22) | (1UL << 23) | (1UL << 36) | (1UL << 38) | (1UL << 39);

        Assert.Empty(decoder.Process(Packet(excluded), out _));
        Assert.Empty(decoder.Process(Packet(0), out _));
    }

    [Fact]
    public void IdenticalFramesNeverInventRepeatTransitions()
    {
        var decoder = new LogitechG13ReportDecoder();

        var first = decoder.Process(Packet(1), out _);
        var identical = decoder.Process(Packet(1), out _);

        Assert.Equal(ControlSignalKind.Press, Assert.Single(first).Kind);
        Assert.Empty(identical);
    }

    [Fact]
    public void SimultaneousButtonsAndDirectionsAreAllPreserved()
    {
        var decoder = new LogitechG13ReportDecoder();
        var bits = (1UL << 0) | (1UL << 21) | (1UL << 24) | (1UL << 35) | (1UL << 37);

        var transitions = decoder.Process(Packet(bits, x: 0, y: 255), out _);

        Assert.Equal(
        [
            LogitechG13Control.G1,
            LogitechG13Control.G22,
            LogitechG13Control.LcdNextPage,
            LogitechG13Control.JoystickPress,
            LogitechG13Control.Lights,
            LogitechG13Control.StickLeft,
            LogitechG13Control.StickDown,
        ],
            transitions.Select(transition => transition.Definition.Control));
        Assert.All(transitions, transition => Assert.Equal(ControlSignalKind.Press, transition.Kind));
    }

    [Fact]
    public void XAxisUsesWideEntryAndNarrowReleaseHysteresis()
    {
        var decoder = new LogitechG13ReportDecoder();

        Assert.Empty(decoder.Process(Packet(0, x: 128), out _));
        Assert.Empty(decoder.Process(Packet(0, x: 65), out _));
        Assert.Equal(LogitechG13Control.StickLeft, Assert.Single(
            decoder.Process(Packet(0, x: 64), out _)).Definition.Control);
        Assert.Empty(decoder.Process(Packet(0, x: 95), out _));
        Assert.Equal(ControlSignalKind.Release, Assert.Single(
            decoder.Process(Packet(0, x: 96), out _)).Kind);
        Assert.Empty(decoder.Process(Packet(0, x: 190), out _));
        Assert.Equal(LogitechG13Control.StickRight, Assert.Single(
            decoder.Process(Packet(0, x: 191), out _)).Definition.Control);
        Assert.Empty(decoder.Process(Packet(0, x: 160), out _));
        Assert.Equal(ControlSignalKind.Release, Assert.Single(
            decoder.Process(Packet(0, x: 159), out _)).Kind);
    }

    [Fact]
    public void YAxisMapsLowToUpHighToDownAndSupportsDirectReversal()
    {
        var decoder = new LogitechG13ReportDecoder();

        var up = Assert.Single(decoder.Process(Packet(0, y: 0), out _));
        var reversal = decoder.Process(Packet(0, y: 255), out _);

        Assert.Equal(LogitechG13Control.StickUp, up.Definition.Control);
        Assert.Collection(
            reversal,
            release =>
            {
                Assert.Equal(LogitechG13Control.StickUp, release.Definition.Control);
                Assert.Equal(ControlSignalKind.Release, release.Kind);
            },
            press =>
            {
                Assert.Equal(LogitechG13Control.StickDown, press.Definition.Control);
                Assert.Equal(ControlSignalKind.Press, press.Kind);
            });
    }

    [Fact]
    public void CenterFrameInitializesNeutralAndAnalogChangesRemainAvailable()
    {
        var decoder = new LogitechG13ReportDecoder();

        var first = decoder.Process(Packet(0, x: 128, y: 128), out var firstAnalogChanged);
        var second = decoder.Process(Packet(0, x: 129, y: 127), out var secondAnalogChanged);

        Assert.Empty(first);
        Assert.False(firstAnalogChanged);
        Assert.True(decoder.HasObservedFrame);
        Assert.Empty(second);
        Assert.True(secondAnalogChanged);
        Assert.Equal(new LogitechG13AnalogState(129, 127), decoder.AnalogState);
    }

    private static RawHidInputPacket Packet(
        ulong buttons,
        byte x = 128,
        byte y = 128) =>
        new(new nint(101), LogitechG13Protocol.InputReportId, x, y, buttons);
}
