using Tappy.Core.Input;
using Tappy.Windows.Input;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class RawInputKeyboardProviderTests
{
    private static readonly SanitizedDeviceDescriptor First =
        DeviceDescriptorSanitizer.CreateKeyboard(new nint(101), @"\\?\HID#VID_1111&PID_0001#PORT_A");

    private static readonly SanitizedDeviceDescriptor Second =
        DeviceDescriptorSanitizer.CreateKeyboard(new nint(202), @"\\?\HID#VID_2222&PID_0002#PORT_B");

    private static readonly SanitizedDeviceDescriptor Composite =
        DeviceDescriptorSanitizer.CreateKeyboardGroup(
            new Guid("D511038E-2418-41EA-9A9E-8FDDC34AC62C"),
            [
                new RawKeyboardDeviceCandidate(new nint(401), @"\\?\HID#VID_1A2C&PID_2D43&COL01#A", 0x1A2C, 0x2D43, null),
                new RawKeyboardDeviceCandidate(new nint(402), @"\\?\HID#VID_1A2C&PID_2D43&COL02#B", 0x1A2C, 0x2D43, null),
                new RawKeyboardDeviceCandidate(new nint(403), @"\\?\HID#VID_1A2C&PID_2D43&COL03#C", 0x1A2C, 0x2D43, null),
                new RawKeyboardDeviceCandidate(new nint(404), @"\\?\HID#VID_1A2C&PID_2D43&COL04#D", 0x1A2C, 0x2D43, null),
            ]);

    [Fact]
    public async Task StartEnumeratesButNeverAutoSelectsAKeyboard()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);

        await provider.StartAsync();

        Assert.True(host.IsRunning);
        Assert.Null(provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
    }

    [Fact]
    public async Task IdentificationIsTargetOnlyAndCannotReachNormalMapping()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<NormalizedKeyboardInput> identification = [];
        List<NormalizedKeyboardInput> mapped = [];
        List<ControlSignal> coreSignals = [];
        provider.IdentificationInputReceived += (_, args) => identification.Add(args.Input);
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        provider.SignalReceived += coreSignals.Add;

        host.Emit(Press(First.SessionHandle, makeCode: 0x4F, virtualKey: 0x23));
        Assert.Empty(identification);
        Assert.Empty(mapped);
        Assert.Empty(coreSignals);

        Assert.True(provider.SetCaptureTarget(First.SessionHandle));
        host.Emit(Press(Second.SessionHandle, makeCode: 0x4F, virtualKey: 0x23));
        Assert.Empty(identification);
        Assert.Empty(mapped);
        Assert.Empty(coreSignals);

        host.Emit(Press(First.SessionHandle, makeCode: 0x4F, virtualKey: 0x23));
        host.Emit(Release(First.SessionHandle, makeCode: 0x4F, virtualKey: 0x23));

        Assert.Collection(
            identification,
            item => Assert.Equal(ControlSignalKind.Press, item.Signal.Kind),
            item => Assert.Equal(ControlSignalKind.Release, item.Signal.Kind));
        Assert.Empty(mapped);
        Assert.Empty(coreSignals);
        Assert.True(provider.IsCaptureTargetNeutral);
    }

    [Fact]
    public async Task ConfirmationRequiresMatchingPersistentIdAndNeutralTarget()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        Assert.True(provider.SetCaptureTarget(First.SessionHandle));

        Assert.False(provider.SetConfirmedPersistentId(Second.PersistentId));
        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));
        Assert.False(provider.IsCaptureTargetNeutral);
        Assert.False(provider.SetConfirmedPersistentId(First.PersistentId));

        host.Emit(Release(First.SessionHandle, 0x1E, 0x41));
        Assert.True(provider.SetConfirmedPersistentId(First.PersistentId));
        Assert.True(provider.IsCaptureConfirmed);
    }

    [Fact]
    public async Task ConfirmedTargetPublishesCoreSignalsAndMarksHardwareRepeat()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<NormalizedKeyboardInput> mapped = [];
        List<NormalizedKeyboardInput> identification = [];
        List<ControlSignal> coreSignals = [];
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        provider.IdentificationInputReceived += (_, args) => identification.Add(args.Input);
        provider.SignalReceived += coreSignals.Add;
        Assert.True(provider.SetCaptureTarget(First.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(First.PersistentId));

        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));
        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));
        host.Emit(Release(First.SessionHandle, 0x1E, 0x41));

        Assert.Empty(identification);
        Assert.Equal(3, coreSignals.Count);
        Assert.Equal(mapped.Select(item => item.Signal), coreSignals);
        Assert.Collection(
            mapped,
            first =>
            {
                Assert.Equal(ControlSignalKind.Press, first.Signal.Kind);
                Assert.False(first.IsRepeat);
            },
            second =>
            {
                Assert.Equal(ControlSignalKind.Repeat, second.Signal.Kind);
                Assert.True(second.IsRepeat);
            },
            third => Assert.Equal(ControlSignalKind.Release, third.Signal.Kind));
        Assert.All(mapped, item => Assert.Equal(new ControllerSessionId(First.SessionId), item.ControllerSessionId));
    }

    [Fact]
    public async Task SelfInjectedPacketsAreRejectedBeforeIdentificationOrMapping()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        var identificationCount = 0;
        var mappedCount = 0;
        provider.IdentificationInputReceived += (_, _) => identificationCount++;
        provider.InputReceived += (_, _) => mappedCount++;
        Assert.True(provider.SetCaptureTarget(First.SessionHandle));

        host.Emit(Press(First.SessionHandle, 0x1E, 0x41) with
        {
            ExtraInformation = InjectedInputMarker.Value,
        });
        Assert.Equal(0, identificationCount);
        Assert.True(provider.SetConfirmedPersistentId(First.PersistentId));

        host.Emit(Press(First.SessionHandle, 0x1E, 0x41) with
        {
            ExtraInformation = InjectedInputMarker.Value,
        });
        Assert.Equal(0, mappedCount);
    }

    [Fact]
    public async Task RemovalPublishesSanitizedContractAndDeactivatesCapture()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        KeyboardDeviceChangedEventArgs? removal = null;
        var mappedCount = 0;
        provider.DeviceChanged += (_, args) => removal = args;
        provider.InputReceived += (_, _) => mappedCount++;
        Assert.True(provider.SetCaptureTarget(First.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(First.PersistentId));
        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));

        host.EmitDeviceChange(First.SessionHandle, RawInputDeviceChangeKind.Removal);
        host.Emit(Release(First.SessionHandle, 0x1E, 0x41));

        Assert.NotNull(removal);
        Assert.Equal(RawInputDeviceChangeKind.Removal, removal.Kind);
        Assert.True(removal.WasCaptureTarget);
        Assert.Equal(First.PersistentId, removal.Descriptor?.PersistentId);
        Assert.Null(provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
        Assert.Equal(2, mappedCount);
    }

    [Fact]
    public async Task LockClearsRepeatStateAndForwardsLifecycleSignal()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<NormalizedKeyboardInput> mapped = [];
        WindowsLifecycleSignal? observed = null;
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        provider.LifecycleChanged += (_, args) => observed = args.Signal;
        Assert.True(provider.SetCaptureTarget(First.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(First.PersistentId));
        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));

        host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);
        host.Emit(Press(First.SessionHandle, 0x1E, 0x41));

        Assert.Equal(WindowsLifecycleSignal.SessionLocked, observed);
        Assert.Equal(2, mapped.Count);
        Assert.All(mapped, item => Assert.False(item.IsRepeat));
    }

    [Fact]
    public async Task CompositeMembersRouteThroughOneLogicalControllerIdentity()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = new RawInputKeyboardProvider(
            new FakeRawInputDeviceEnumerator(Composite),
            host,
            timestampProvider: () => 12345);
        List<NormalizedKeyboardInput> mapped = [];
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        _ = provider.EnumerateKeyboards();

        Assert.Single(provider.ConnectedControllers);
        Assert.True(provider.SetCaptureTarget(Composite.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(Composite.PersistentId));

        host.Emit(Press(new nint(401), 0x1E, 0x41));
        host.Emit(Release(new nint(401), 0x1E, 0x41));
        host.Emit(Press(new nint(404), 0x30, 0x42));
        host.Emit(Release(new nint(404), 0x30, 0x42));

        Assert.Equal(4, mapped.Count);
        Assert.All(mapped, input =>
        {
            Assert.Equal(Composite.SessionId, input.ControllerSessionId.Value);
            Assert.Equal(Composite.PersistentId, input.PersistentDeviceId);
        });
        Assert.Equal(new nint(401), mapped[0].SessionDeviceHandle);
        Assert.Equal(new nint(404), mapped[2].SessionDeviceHandle);
    }

    [Fact]
    public async Task MirroredCrossCollectionTransitionsAreStatefullyCoalesced()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = new RawInputKeyboardProvider(
            new FakeRawInputDeviceEnumerator(Composite),
            host,
            timestampProvider: () => 12345);
        List<NormalizedKeyboardInput> mapped = [];
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        _ = provider.EnumerateKeyboards();
        Assert.True(provider.SetCaptureTarget(Composite.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(Composite.PersistentId));

        host.Emit(Press(new nint(401), 0x1E, 0x41));
        host.Emit(Press(new nint(402), 0x1E, 0x41));
        host.Emit(Press(new nint(402), 0x1E, 0x41));
        host.Emit(Press(new nint(401), 0x1E, 0x41));
        host.Emit(Release(new nint(401), 0x1E, 0x41));
        host.Emit(Release(new nint(402), 0x1E, 0x41));

        Assert.Collection(
            mapped,
            input => Assert.Equal(ControlSignalKind.Press, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Repeat, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Release, input.Signal.Kind));
    }

    [Fact]
    public async Task RemovingOneCompositeMemberKeepsConfirmedLogicalControllerActive()
    {
        var host = new FakeRawInputMessageHost();
        var enumerator = new FakeRawInputDeviceEnumerator(Composite);
        await using var provider = new RawInputKeyboardProvider(enumerator, host, timestampProvider: () => 12345);
        List<KeyboardDeviceChangedEventArgs> changes = [];
        List<NormalizedKeyboardInput> mapped = [];
        provider.DeviceChanged += (_, args) => changes.Add(args);
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        _ = provider.EnumerateKeyboards();
        Assert.True(provider.SetCaptureTarget(Composite.SessionHandle));
        Assert.True(provider.SetConfirmedPersistentId(Composite.PersistentId));

        host.Emit(Press(new nint(401), 0x1E, 0x41));
        host.EmitDeviceChange(new nint(401), RawInputDeviceChangeKind.Removal);
        host.Emit(Press(new nint(404), 0x30, 0x42));
        host.Emit(Release(new nint(404), 0x30, 0x42));

        var change = Assert.Single(changes);
        Assert.Equal(RawInputDeviceChangeKind.MembershipChanged, change.Kind);
        Assert.True(change.WasCaptureTarget);
        Assert.Equal(3, change.Descriptor?.InterfaceCount);
        Assert.True(provider.IsCaptureConfirmed);
        Assert.Equal(Composite.SessionHandle, provider.CaptureTarget);
        Assert.Collection(
            mapped,
            input => Assert.Equal(ControlSignalKind.Press, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Release, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Press, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Release, input.Signal.Kind));
    }

    private static RawInputKeyboardProvider CreateProvider(FakeRawInputMessageHost host) =>
        new(new FakeRawInputDeviceEnumerator(First, Second), host, timestampProvider: () => 12345);

    private static RawKeyboardPacket Press(nint handle, ushort makeCode, ushort virtualKey) =>
        new(handle, makeCode, RawKeyboardFlags.Make, 0, virtualKey, 0x0100, 0);

    private static RawKeyboardPacket Release(nint handle, ushort makeCode, ushort virtualKey) =>
        new(handle, makeCode, RawKeyboardFlags.Break, 0, virtualKey, 0x0101, 0);
}
