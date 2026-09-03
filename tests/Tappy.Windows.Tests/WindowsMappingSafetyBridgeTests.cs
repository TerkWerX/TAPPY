using Tappy.Core.Input;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;

namespace Tappy.Windows.Tests;

public sealed class WindowsMappingSafetyBridgeTests
{
    [Fact]
    public async Task DisconnectAndLifecycleSignalsReachCoreSafetySeam()
    {
        var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
            new nint(55),
            @"\\?\HID#VID_1234&PID_5678#PORT_A");
        var host = new FakeRawInputMessageHost();
        await using var provider = new RawInputKeyboardProvider(
            new FakeRawInputDeviceEnumerator(descriptor),
            host,
            keyboardIsNeutral: static () => true);
        var target = new RecordingSafetyTarget();
        var applicationLifecycle = new ApplicationLifecycleSignalSource();
        using var bridge = new WindowsMappingSafetyBridge(provider, target, applicationLifecycle);
        _ = provider.EnumerateKeyboards();

        host.EmitDeviceChange(descriptor.SessionHandle, RawInputDeviceChangeKind.Removal);
        host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);
        host.EmitLifecycle(WindowsLifecycleSignal.Resuming);
        host.EmitLifecycle(WindowsLifecycleSignal.Suspending);
        applicationLifecycle.Report(WindowsLifecycleSignal.ShutdownRequested);

        Assert.Equal(
            [new ControllerSessionId(descriptor.SessionId)],
            target.DisconnectedControllers);
        Assert.Equal(3, target.LifecycleResetCount);
    }

    private sealed class RecordingSafetyTarget : IWindowsMappingSafetyTarget
    {
        internal List<ControllerSessionId> DisconnectedControllers { get; } = [];

        internal int LifecycleResetCount { get; private set; }

        public void DisconnectController(ControllerSessionId sessionId) =>
            DisconnectedControllers.Add(sessionId);

        public void ResetForLifecycleTransition() => LifecycleResetCount++;
    }
}
