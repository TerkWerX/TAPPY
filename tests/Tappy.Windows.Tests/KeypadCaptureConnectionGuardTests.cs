using Tappy.KeypadCapture;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class KeypadCaptureConnectionGuardTests
{
    private static readonly SanitizedDeviceDescriptor Target = DeviceDescriptorSanitizer.CreateKeyboard((nint)41,
        @"\\?\HID#{00001124-0000-1000-8000-00805F9B34FB}_VID&0002045E_PID&0764&COL01#TEST#GUID");

    [Theory]
    [InlineData(RawInputDeviceChangeKind.Arrival)]
    [InlineData(RawInputDeviceChangeKind.MembershipChanged)]
    public void Identical_registration_notifications_and_name_changes_do_not_abort(RawInputDeviceChangeKind kind)
    {
        Assert.Null(KeypadCaptureConnectionGuard.StopReason(Target,
            new KeyboardDeviceChangedEventArgs(kind, Target with { DisplayName = "Updated display-only name" }, true)));
    }

    [Fact]
    public void Removed_target_still_aborts_even_when_its_old_descriptor_matches()
    {
        Assert.Equal("Microsoft pad disconnected", KeypadCaptureConnectionGuard.StopReason(Target,
            new KeyboardDeviceChangedEventArgs(RawInputDeviceChangeKind.Removal, Target, true)));
    }

    [Fact]
    public void Other_keyboard_notifications_do_not_affect_capture()
    {
        var other = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#OTHER#KEYBOARD");
        foreach (var kind in Enum.GetValues<RawInputDeviceChangeKind>())
        {
            Assert.Null(KeypadCaptureConnectionGuard.StopReason(Target, new KeyboardDeviceChangedEventArgs(kind, other, false)));
        }
    }

    [Fact]
    public void Changed_identity_session_or_member_set_is_not_mistaken_for_registration_noise()
    {
        var changed = new[]
        {
            Target with { PersistentId = "different" }, Target with { PathFingerprintSha256 = "different" },
            Target with { SessionId = "different" }, Target with { SessionHandle = (nint)42 },
            Target with { PresentationModelId = null }, Target with { MemberSessionHandles = [(nint)41, (nint)42] },
            Target with { MemberSessionHandles = [] }
        };
        Assert.All(changed, next => Assert.NotNull(KeypadCaptureConnectionGuard.StopReason(Target,
            new KeyboardDeviceChangedEventArgs(RawInputDeviceChangeKind.MembershipChanged, next, true))));
        Assert.NotNull(KeypadCaptureConnectionGuard.StopReason(Target,
            new KeyboardDeviceChangedEventArgs(RawInputDeviceChangeKind.MembershipChanged, null, true)));
    }

    [Fact]
    public void Reordered_identical_member_handles_are_not_a_changed_connection()
    {
        var composite = Target with { MemberSessionHandles = [(nint)41, (nint)42] };
        Assert.Null(KeypadCaptureConnectionGuard.StopReason(composite, new KeyboardDeviceChangedEventArgs(
            RawInputDeviceChangeKind.MembershipChanged, composite with { MemberSessionHandles = [(nint)42, (nint)41] }, true)));
    }

    [Fact]
    public async Task Provider_startup_arrival_allows_both_sweeps_to_finish_but_real_removal_aborts()
    {
        var host = new TestHost();
        var devices = new Enumerator();
        await using var provider = new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => true);
        var session = new KeypadCaptureSession(Target.PersistentId);
        provider.IdentificationInputReceived += (_, e) => session.Observe(e.Input);
        provider.DeviceChanged += (_, e) =>
        {
            if (KeypadCaptureConnectionGuard.StopReason(Target, e) is { } reason)
            { session.Abort(reason); provider.ClearCaptureTarget(); }
        };
        await provider.StartAsync();
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(provider.SetCaptureTarget(Target.SessionHandle));
            session.Start();
            // Reproduces DEVNOTIFY delivered after Start/capture-target selection.
            host.Arrival();
            Assert.True(session.IsCapturing);
            host.Key(RawKeyboardFlags.Make, pass == 0 ? (ushort)0x67 : (ushort)0x24);
            host.Key(RawKeyboardFlags.Break, pass == 0 ? (ushort)0x67 : (ushort)0x24);
            Assert.True(session.Finish(provider.IsCaptureTargetNeutral));
            provider.ClearCaptureTarget();
        }
        Assert.Equal(CaptureStage.Completed, session.Stage);
        Assert.All(session.Snapshot().Passes, pass => Assert.Equal(2, pass.AcceptedPackets));

        session = new KeypadCaptureSession(Target.PersistentId);
        Assert.True(provider.SetCaptureTarget(Target.SessionHandle)); session.Start();
        devices.Connected = false; host.Removal();
        Assert.Equal(CaptureStage.Aborted, session.Stage);
        Assert.Null(provider.CaptureTarget);
        Assert.Equal("Microsoft pad disconnected", session.Snapshot().StopReason);
    }

    private sealed class Enumerator : IRawInputDeviceEnumerator
    {
        internal bool Connected = true;
        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => Connected ? [Target] : [];
        public SanitizedDeviceDescriptor? DescribeKeyboard(nint handle) => Connected && handle == Target.SessionHandle ? Target : null;
    }

    private sealed class TestHost : IRawInputMessageHost
    {
        public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;
        public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged { add { } remove { } }
        public event EventHandler<Exception>? Faulted { add { } remove { } }
        public bool IsRunning { get; private set; }
        public nint WindowHandle => nint.Zero;
        public Task StartAsync(CancellationToken cancellationToken = default) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
        internal void Arrival() => DeviceChanged?.Invoke(this, new(Target.SessionHandle, RawInputDeviceChangeKind.Arrival));
        internal void Removal() => DeviceChanged?.Invoke(this, new(Target.SessionHandle, RawInputDeviceChangeKind.Removal));
        internal void Key(RawKeyboardFlags flags, ushort vk) => KeyboardPacketReceived?.Invoke(this,
            new(new RawKeyboardPacket(Target.SessionHandle, 0x47, flags, 0, vk, 0, 0)));
    }
}
