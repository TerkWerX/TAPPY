using System.Text.Json;
using Tappy.Windows.Diagnostics;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class InputDiagnosticAggregateTests
{
    [Fact]
    public void SnapshotContainsCountsButNoKeySequenceOrRawPath()
    {
        const string rawPath = @"\\?\HID#VID_046D&PID_C31C#SENSITIVE-PATH";
        var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(new nint(88), rawPath);
        var aggregate = new InputDiagnosticAggregate();
        var press = KeyboardPacketNormalizer.Normalize(
            new RawKeyboardPacket(descriptor.SessionHandle, 0x1E, RawKeyboardFlags.Make, 0, 0x41, 0x100, 0),
            descriptor,
            isRepeat: false,
            timestamp: 1);
        var repeat = press with
        {
            IsRepeat = true,
            Signal = press.Signal with { Kind = Tappy.Core.Input.ControlSignalKind.Repeat },
        };
        var release = KeyboardPacketNormalizer.Normalize(
            new RawKeyboardPacket(descriptor.SessionHandle, 0x1E, RawKeyboardFlags.Break, 0, 0x41, 0x101, 0),
            descriptor,
            isRepeat: false,
            timestamp: 2);

        aggregate.Observe(press);
        aggregate.Observe(repeat);
        aggregate.Observe(release);
        aggregate.ObserveDisconnect(descriptor.PersistentId);

        var snapshot = Assert.Single(aggregate.Snapshot());
        Assert.Equal(1, snapshot.PressCount);
        Assert.Equal(1, snapshot.RepeatCount);
        Assert.Equal(1, snapshot.ReleaseCount);
        Assert.Equal(1, snapshot.DisconnectCount);
        Assert.Equal(0, snapshot.CurrentlyHeldCount);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain(rawPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENSITIVE-PATH", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(press.ControlId.Value, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DisplayName", json, StringComparison.Ordinal);
    }
}
