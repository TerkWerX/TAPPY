using Tappy.KeypadCapture;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class KeypadCaptureSessionTests
{
    private static readonly SanitizedDeviceDescriptor Target = DeviceDescriptorSanitizer.CreateKeyboard((nint)1,
        @"\\?\HID#TARGET#PAD");

    [Fact]
    public void Capture_is_explicit_two_passes_and_retains_VK_differences_without_changing_control_ids()
    {
        var session = new KeypadCaptureSession(Target.PersistentId);
        session.Observe(Input(0x47, 0x67));
        Assert.Empty(session.Snapshot().Passes);
        session.Start();
        session.Observe(Input(0x47, 0x67));
        session.Observe(Input(0x47, 0x67, RawKeyboardFlags.Break));
        Assert.True(session.Finish(neutral: true));
        session.Observe(Input(0x45, 0x90));
        Assert.Equal(2, session.Snapshot().Passes[0].AcceptedPackets);
        session.Start();
        session.Observe(Input(0x47, 0x24));
        session.Observe(Input(0x47, 0x24, RawKeyboardFlags.Break));
        Assert.True(session.Finish(neutral: true));
        var summary = session.Snapshot();
        Assert.Equal("Completed", summary.Status);
        Assert.Equal("0x0067", Assert.Single(summary.Passes[0].Reports).VirtualKey);
        Assert.Equal("0x0024", Assert.Single(summary.Passes[1].Reports).VirtualKey);
        Assert.Equal(Input(0x47, 0x67).ControlId, Input(0x47, 0x24).ControlId);
        session.Observe(Input(0x47, 0x67));
        Assert.Equal(2, session.Snapshot().Passes[1].AcceptedPackets);
        Assert.Throws<InvalidOperationException>(session.Start);
    }

    [Fact]
    public void Other_devices_and_non_keypad_data_are_never_retained()
    {
        var session = new KeypadCaptureSession(Target.PersistentId);
        session.Start();
        session.Observe(Input(0x47, 0x67) with { PersistentDeviceId = "other-keyboard" });
        session.Observe(Input(0x1E, 0x41));
        session.Observe(Input(0x47, 0x41));
        session.Observe(Input(0x47, 0x67, RawKeyboardFlags.E1));
        var pass = Assert.Single(session.Snapshot().Passes);
        Assert.Empty(pass.Reports);
        Assert.Equal(0, pass.AcceptedPackets);
        Assert.Equal(3, pass.DiscardedPackets); // Not even a counter for other keyboards.
        Assert.False(session.Finish(neutral: true));
    }

    [Theory]
    [InlineData(0x4C, 0x0C)]
    [InlineData(0x1C, 0x0D)]
    [InlineData(0x35, 0x6F)]
    [InlineData(0x53, 0x2E)]
    [InlineData(0x45, 0x90)]
    public void Numpad_clear_enter_divide_delete_and_numlock_are_in_scope(int scan, int vk)
    {
        var session = new KeypadCaptureSession(Target.PersistentId);
        session.Start();
        session.Observe(Input((ushort)scan, (ushort)vk, RawKeyboardFlags.E0));
        Assert.Single(Assert.Single(session.Snapshot().Passes).Reports);
    }

    [Fact]
    public void Capture_times_out_even_if_no_input_arrives_and_cannot_rearm()
    {
        var clock = new TestClock();
        var session = new KeypadCaptureSession(Target.PersistentId, clock);
        session.Start(); clock.Seconds = 120; session.Tick();
        Assert.Equal(CaptureStage.Aborted, session.Stage);
        Assert.Equal("Pass timed out", session.Snapshot().StopReason);
        Assert.Throws<InvalidOperationException>(session.Start);
    }

    [Fact]
    public void Late_input_is_rejected_before_retention()
    {
        var clock = new TestClock();
        var session = new KeypadCaptureSession(Target.PersistentId, clock);
        session.Start(); clock.Seconds = 121; session.Observe(Input(0x47, 0x67));
        Assert.Equal(CaptureStage.Aborted, session.Stage);
        Assert.Empty(session.Snapshot().Passes[0].Reports);
    }

    [Fact]
    public void Repeats_and_rejected_packets_count_toward_finite_budget()
    {
        var session = new KeypadCaptureSession(Target.PersistentId);
        session.Start();
        session.Observe(Input(0x47, 0x67));
        for (var i = 1; i < KeypadCaptureSession.PacketLimit; i++) { session.Observe(Input(0x47, 0x67) with { IsRepeat = true }); }
        session.Observe(Input(0x1E, 0x41));
        Assert.Equal(CaptureStage.Aborted, session.Stage);
        var pass = session.Snapshot().Passes[0];
        Assert.Equal(KeypadCaptureSession.PacketLimit, pass.AcceptedPackets);
        Assert.Equal(KeypadCaptureSession.PacketLimit - 1, Assert.Single(pass.Reports).Repeat);
        Assert.Equal(1, Assert.Single(pass.Reports).Press);
    }

    [Fact]
    public void Held_key_finish_or_lifecycle_abort_is_never_a_complete_capture()
    {
        var session = new KeypadCaptureSession(Target.PersistentId);
        session.Start(); session.Observe(Input(0x47, 0x67));
        Assert.False(session.Finish(neutral: false));
        Assert.Equal(CaptureStage.Aborted, session.Stage);
        session.Abort("Another reason");
        Assert.Equal("Key still held at finish", session.Snapshot().StopReason);
    }

    private static NormalizedKeyboardInput Input(ushort scan, ushort vk, RawKeyboardFlags flags = RawKeyboardFlags.Make) =>
        KeyboardPacketNormalizer.Normalize(new RawKeyboardPacket((nint)1, scan, flags, 0, vk, 0, 0), Target, false, 0);

    private sealed class TestClock : TimeProvider
    {
        internal long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }
}
