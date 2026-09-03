using System.Text.Json;
using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class LogitechG13InputProviderTests
{
    private static readonly SanitizedDeviceDescriptor G13 = Descriptor(
        101,
        "g13-session",
        "raw-hid-g13:physical",
        LogitechG13Protocol.ProductId,
        LogitechG13Protocol.UsagePage,
        LogitechG13Protocol.Usage);

    private static readonly SanitizedDeviceDescriptor VirtualKeyboard = Descriptor(
        202,
        "c232-session",
        "raw-keyboard:c232",
        0xC232,
        0x0001,
        0x0006,
        RawInputDeviceKind.Keyboard);

    [Fact]
    public async Task EnumerationDoesNotAutoSelectAndRejectsC232Identity()
    {
        var host = new FakeRawInputMessageHost();
        var enumerator = new FakeLogitechG13DeviceEnumerator(G13, VirtualKeyboard);
        await using var provider = new LogitechG13InputProvider(enumerator, host);
        var signals = 0;
        provider.SignalReceived += _ => signals++;

        var controllers = provider.EnumerateControllers();
        host.Emit(Frame(G13.SessionHandle, 1));

        Assert.Single(controllers);
        Assert.Single(provider.ConnectedControllers);
        Assert.Equal("raw-hid-g13", provider.ConnectedControllers[0].ProviderId);
        Assert.Null(provider.CaptureTarget);
        Assert.Equal(0, signals);
        Assert.False(provider.SetCaptureTarget(VirtualKeyboard.SessionHandle));
    }

    [Fact]
    public async Task FirstNonNeutralReportCannotBeConfirmedUntilBalancedNeutral()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<LogitechG13Input> identification = [];
        provider.IdentificationInputReceived += (_, args) => identification.Add(args.Input);
        _ = provider.EnumerateControllers();

        Assert.True(provider.SetCaptureTarget(G13.SessionHandle));
        Assert.False(provider.IsCaptureTargetNeutral);
        Assert.False(provider.SetConfirmedPersistentId(G13.PersistentId));

        host.Emit(Frame(G13.SessionHandle, 1UL << 0));
        Assert.False(provider.IsCaptureTargetNeutral);
        Assert.False(provider.SetConfirmedPersistentId(G13.PersistentId));

        host.Emit(Frame(G13.SessionHandle, 0));
        Assert.True(provider.IsCaptureTargetNeutral);
        Assert.True(provider.SetConfirmedPersistentId(G13.PersistentId));
        Assert.Collection(
            identification,
            input => Assert.Equal(ControlSignalKind.Press, input.Signal.Kind),
            input => Assert.Equal(ControlSignalKind.Release, input.Signal.Kind));
    }

    [Fact]
    public async Task ConfirmedPhysicalControllerPublishesStableSignalsWithoutRepeat()
    {
        var host = new FakeRawInputMessageHost();
        var timestamp = 0L;
        await using var provider = new LogitechG13InputProvider(
            new FakeLogitechG13DeviceEnumerator(G13),
            host,
            () => ++timestamp);
        List<LogitechG13Input> mapped = [];
        List<ControlSignal> signals = [];
        provider.InputReceived += (_, args) => mapped.Add(args.Input);
        provider.SignalReceived += signals.Add;
        ConfirmNeutral(provider, host);

        host.Emit(Frame(G13.SessionHandle, 1UL << 21, x: 0));
        host.Emit(Frame(G13.SessionHandle, 1UL << 21, x: 0));
        host.Emit(Frame(G13.SessionHandle, 0, x: 128));

        Assert.Equal(4, mapped.Count);
        Assert.Equal(mapped.Select(input => input.Signal), signals);
        Assert.Equal(
        [
            LogitechG13Control.G22,
            LogitechG13Control.StickLeft,
            LogitechG13Control.G22,
            LogitechG13Control.StickLeft,
        ],
            mapped.Select(input => input.Control));
        Assert.Equal(
        [
            ControlSignalKind.Press,
            ControlSignalKind.Press,
            ControlSignalKind.Release,
            ControlSignalKind.Release,
        ],
            mapped.Select(input => input.Signal.Kind));
        Assert.DoesNotContain(mapped, input => input.Signal.Kind == ControlSignalKind.Repeat);
        Assert.All(mapped, input =>
        {
            Assert.Equal(new ControllerSessionId(G13.SessionId), input.ControllerSessionId);
            Assert.Equal(G13.PersistentId, input.PersistentDeviceId);
        });
    }

    [Fact]
    public async Task PacketsFromAnotherHandleNeverCrossSelectedBoundary()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        var identification = 0;
        provider.IdentificationInputReceived += (_, _) => identification++;
        _ = provider.EnumerateControllers();
        Assert.True(provider.SetCaptureTarget(G13.SessionHandle));

        host.Emit(Frame(new nint(999), 1));

        Assert.Equal(0, identification);
        Assert.False(provider.IsCaptureTargetNeutral);
    }

    [Fact]
    public async Task ClearPublishesBalancedReleaseBeforeDroppingSelection()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<ControlSignal> signals = [];
        provider.SignalReceived += signals.Add;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, 1));

        provider.ClearCaptureTarget();

        Assert.Equal([ControlSignalKind.Press, ControlSignalKind.Release], signals.Select(signal => signal.Kind));
        Assert.Null(provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
    }

    [Fact]
    public async Task LifecyclePublishesAllHeldReleasesAndRequiresFreshNeutralConfirmation()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<ControlSignal> signals = [];
        WindowsLifecycleSignal? lifecycle = null;
        provider.SignalReceived += signals.Add;
        provider.LifecycleChanged += (_, args) => lifecycle = args.Signal;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, (1UL << 0) | (1UL << 29), x: 255));

        host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);

        Assert.Equal(6, signals.Count);
        Assert.Equal(3, signals.Count(signal => signal.Kind == ControlSignalKind.Press));
        Assert.Equal(3, signals.Count(signal => signal.Kind == ControlSignalKind.Release));
        Assert.Equal(WindowsLifecycleSignal.SessionLocked, lifecycle);
        Assert.Equal(G13.SessionHandle, provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
        Assert.False(provider.IsCaptureTargetNeutral);
        Assert.False(provider.SetConfirmedPersistentId(G13.PersistentId));
    }

    [Fact]
    public async Task FaultPublishesReleaseAndFailsClosedAtProviderBoundary()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<ControlSignal> signals = [];
        Exception? observed = null;
        provider.SignalReceived += signals.Add;
        provider.Faulted += (_, exception) => observed = exception;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, 1UL << 37));
        var fault = new InvalidOperationException("synthetic fault");

        host.EmitFault(fault);

        Assert.Same(fault, observed);
        Assert.Equal([ControlSignalKind.Press, ControlSignalKind.Release], signals.Select(signal => signal.Kind));
        Assert.Null(provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
    }

    [Fact]
    public async Task Capability_failure_removes_G13_choices_without_faulting_other_host_consumers()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        var availabilityChanges = 0;
        var fatalFaults = 0;
        List<ControlSignal> signals = [];
        provider.AvailabilityChanged += (_, _) => availabilityChanges++;
        provider.Faulted += (_, _) => fatalFaults++;
        provider.SignalReceived += signals.Add;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, 1));

        host.EmitFault(new RawInputCapabilityException(
            RawInputCapability.LogitechG13,
            "G13 registration unavailable."));

        Assert.False(provider.IsAvailable);
        Assert.Contains("keyboard controllers remain available", provider.AvailabilityStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, availabilityChanges);
        Assert.Equal(0, fatalFaults);
        Assert.Empty(provider.EnumerateControllers());
        Assert.Empty(provider.ConnectedControllers);
        Assert.Null(provider.CaptureTarget);
        Assert.False(provider.SetCaptureTarget(G13.SessionHandle));
        Assert.Equal([ControlSignalKind.Press, ControlSignalKind.Release],
            signals.Select(signal => signal.Kind));
    }

    [Fact]
    public async Task Selected_membership_change_releases_state_and_requires_reconfirmation()
    {
        var logical = G13 with
        {
            SessionHandle = new nint(900),
            SessionId = "g13-container-session",
            PersistentId = "raw-hid-g13:container",
            MemberSessionHandles = [new nint(901), new nint(902)],
            Grouping = PhysicalDeviceGrouping.WindowsContainerId,
        };
        var remaining = logical with { MemberSessionHandles = [new nint(902)] };
        var host = new FakeRawInputMessageHost();
        var enumerator = new FakeLogitechG13DeviceEnumerator(logical);
        await using var provider = new LogitechG13InputProvider(enumerator, host);
        var changes = new List<LogitechG13DeviceChangedEventArgs>();
        List<ControlSignal> mapped = [];
        provider.DeviceChanged += (_, change) => changes.Add(change);
        provider.SignalReceived += mapped.Add;
        _ = provider.EnumerateControllers();
        Assert.True(provider.SetCaptureTarget(logical.SessionHandle));
        host.Emit(Frame(new nint(901), 0));
        Assert.True(provider.SetConfirmedPersistentId(logical.PersistentId));
        host.Emit(Frame(new nint(901), 1));
        enumerator.SetDescriptors(remaining);

        host.EmitDeviceChange(new nint(901), RawInputDeviceChangeKind.Removal);
        host.Emit(Frame(new nint(902), 1UL << 1));

        var change = Assert.Single(changes);
        Assert.Equal(RawInputDeviceChangeKind.MembershipChanged, change.Kind);
        Assert.True(change.WasCaptureTarget);
        Assert.Equal(logical.SessionHandle, provider.CaptureTarget);
        Assert.False(provider.IsCaptureConfirmed);
        Assert.Equal([ControlSignalKind.Press, ControlSignalKind.Release],
            mapped.Select(signal => signal.Kind));
    }

    [Fact]
    public async Task RemovalPublishesAllHeldReleasesAndSanitizedDeviceChange()
    {
        var host = new FakeRawInputMessageHost();
        var enumerator = new FakeLogitechG13DeviceEnumerator(G13);
        await using var provider = new LogitechG13InputProvider(enumerator, host);
        List<ControlSignal> signals = [];
        LogitechG13DeviceChangedEventArgs? removal = null;
        provider.SignalReceived += signals.Add;
        provider.DeviceChanged += (_, args) => removal = args;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, 1UL << 35, y: 0));
        enumerator.SetDescriptors();

        host.EmitDeviceChange(G13.SessionHandle, RawInputDeviceChangeKind.Removal);

        Assert.Equal(4, signals.Count);
        Assert.Equal(2, signals.Count(signal => signal.Kind == ControlSignalKind.Release));
        Assert.NotNull(removal);
        Assert.Equal(RawInputDeviceChangeKind.Removal, removal.Kind);
        Assert.True(removal.WasCaptureTarget);
        Assert.Equal(G13.PersistentId, removal.Descriptor?.PersistentId);
        Assert.Null(provider.CaptureTarget);
    }

    [Fact]
    public async Task AnalogChangesAreAvailableOnlyAfterConfirmation()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        List<LogitechG13AnalogState> analog = [];
        provider.AnalogStateChanged += (_, args) => analog.Add(args.State);
        _ = provider.EnumerateControllers();
        Assert.True(provider.SetCaptureTarget(G13.SessionHandle));
        host.Emit(Frame(G13.SessionHandle, 0, x: 128, y: 128));
        host.Emit(Frame(G13.SessionHandle, 0, x: 129, y: 128));
        Assert.Empty(analog);
        Assert.True(provider.SetConfirmedPersistentId(G13.PersistentId));

        host.Emit(Frame(G13.SessionHandle, 0, x: 130, y: 127));

        Assert.Equal([new LogitechG13AnalogState(130, 127)], analog);
    }

    [Fact]
    public async Task PublicInputAndNativePacketCannotSerializeHandleOrReportState()
    {
        var host = new FakeRawInputMessageHost();
        await using var provider = CreateProvider(host);
        LogitechG13Input? observed = null;
        provider.InputReceived += (_, args) => observed = args.Input;
        ConfirmNeutral(provider, host);
        host.Emit(Frame(G13.SessionHandle, 1));
        Assert.NotNull(observed);

        var packetJson = JsonSerializer.Serialize(Frame(G13.SessionHandle, 0x123456789A));
        var packetText = Frame(G13.SessionHandle, 0x123456789A).ToString();
        var inputJson = JsonSerializer.Serialize(observed);

        Assert.Equal("{}", packetJson);
        Assert.DoesNotContain("123456789A", packetText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(G13.SessionHandle.ToString(), packetText, StringComparison.Ordinal);
        Assert.DoesNotContain(G13.SessionHandle.ToString(), inputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789A", inputJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("container", inputJson, StringComparison.OrdinalIgnoreCase);
    }

    private static LogitechG13InputProvider CreateProvider(FakeRawInputMessageHost host) =>
        new(new FakeLogitechG13DeviceEnumerator(G13), host, () => 12345);

    private static void ConfirmNeutral(
        LogitechG13InputProvider provider,
        FakeRawInputMessageHost host)
    {
        _ = provider.EnumerateControllers();
        Assert.True(provider.SetCaptureTarget(G13.SessionHandle));
        host.Emit(Frame(G13.SessionHandle, 0));
        Assert.True(provider.SetConfirmedPersistentId(G13.PersistentId));
    }

    private static RawHidInputPacket Frame(
        nint handle,
        ulong bits,
        byte x = 128,
        byte y = 128) =>
        new(handle, LogitechG13Protocol.InputReportId, x, y, bits);

    private static SanitizedDeviceDescriptor Descriptor(
        long handle,
        string sessionId,
        string persistentId,
        ushort productId,
        ushort usagePage,
        ushort usage,
        RawInputDeviceKind kind = RawInputDeviceKind.Hid) =>
        new(
            new nint(handle),
            sessionId,
            persistentId,
            new string('A', 64),
            kind,
            LogitechG13Protocol.VendorId,
            productId,
            usagePage,
            usage,
            productId == LogitechG13Protocol.ProductId ? "Logitech G13" : "Not the physical G13");
}
