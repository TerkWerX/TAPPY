using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Theory]
    [InlineData("removal")]
    [InlineData("refresh")]
    [InlineData("lock")]
    public async Task G13_cleanup_never_starts_a_configured_release_macro(string cleanup)
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13();
            var host = new TestHost();
            var devices = new G13Enumerator(descriptor);
            var keyboard = new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true);
            var g13 = new LogitechG13InputProvider(devices, host);
            var actions = new RecordingActions();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboard, g13, new RecordingKeyboard(), new AtomicProfileStore(root), actionOutput: actions);
            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.True(runtime.ConfirmController().Succeeded);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(c => c.Control == LogitechG13Control.G1).ControlId.Value;
            Assert.True(runtime.AssignControllerAction(g1, ReleaseMacro()).Succeeded);
            runtime.IsRehearsal = false;
            host.EmitHid(1);
            Assert.Equal(0, actions.Starts);

            if (cleanup == "lock")
            {
                host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);
            }
            else
            {
                devices.Devices = [];
                if (cleanup == "removal")
                {
                    host.EmitRemoval(descriptor.SessionHandle);
                }
                else
                {
                    runtime.RefreshDevices();
                }
            }

            Assert.Equal(0, actions.Starts);
            Assert.True(runtime.IsRehearsal);
            Assert.True(runtime.IsOutputStateConfirmedSafe);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Keyboard_removal_releases_owned_output_without_starting_release_macro()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_1234&PID_5678#REVIEW_KEYPAD");
            var devices = new KeyboardEnumerator(descriptor);
            var host = new TestHost();
            var keyboard = new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => true);
            var actions = new RecordingActions();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboard, new RecordingKeyboard(), new AtomicProfileStore(root), actionOutput: actions);
            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitKey(descriptor.SessionHandle, RawKeyboardFlags.Make);
            host.EmitKey(descriptor.SessionHandle, RawKeyboardFlags.Break);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.True(runtime.AssignControllerAction(ControlId.FromRawInputKeyboard(0x4F).Value, ReleaseMacro()).Succeeded);
            runtime.IsRehearsal = false;
            host.EmitKey(descriptor.SessionHandle, RawKeyboardFlags.Make);

            devices.Devices = [];
            host.EmitRemoval(descriptor.SessionHandle);

            Assert.Equal(0, actions.Starts);
            Assert.True(runtime.IsRehearsal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MIDI_only_unplug_automatically_releases_the_mapped_key_and_disarms()
    {
        var root = NewRoot();
        try
        {
            using var backend = new MidiBackend();
            var midi = new WinMmMidiInputProvider(backend, monitorInterval: TimeSpan.FromMilliseconds(20));
            var keyboard = new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboard, output, new AtomicProfileStore(root), midiInputProvider: midi);
            await runtime.InitializeAsync();
            IdentifyMidi(runtime, backend);
            Assert.True(runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24").Succeeded);
            runtime.IsRehearsal = false;
            backend.Publish(0x00643C90);
            Assert.Equal(1, output.Down);
            var disarmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.StateChanged += (_, state) =>
            {
                if (!state.IsConfirmed && runtime.IsRehearsal)
                {
                    disarmed.TrySetResult();
                }
            };

            backend.Devices = [];
            await disarmed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, output.Up);
            Assert.False(midi.IsCaptureConfirmed);
            Assert.True(runtime.IsRehearsal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task In_flight_MIDI_event_cannot_run_after_the_same_controller_is_reidentified()
    {
        var root = NewRoot();
        using var release = new ManualResetEventSlim();
        try
        {
            using var backend = new MidiBackend();
            var midi = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockOnce = 1;
            midi.InputReceived += (_, _) =>
            {
                if (Interlocked.Exchange(ref blockOnce, 0) == 1)
                {
                    entered.TrySetResult();
                    release.Wait();
                }
            };
            var keyboard = new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboard, output, new AtomicProfileStore(root), midiInputProvider: midi);
            await runtime.InitializeAsync();
            IdentifyMidi(runtime, backend);
            Assert.True(runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24").Succeeded);
            runtime.IsRehearsal = false;

            var oldInput = Task.Run(() => backend.Publish(0x00643C90));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IdentifyMidi(runtime, backend);
            runtime.IsRehearsal = false;
            release.Set();
            await oldInput.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, output.Down);

            backend.Publish(0x00643C90);
            Assert.Equal(1, output.Down);
        }
        finally
        {
            release.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void IdentifyMidi(DeviceAwareControllerRuntime runtime, MidiBackend backend)
    {
        Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
        backend.Publish(0x00643D90);
        backend.Publish(0x00003D80);
        Assert.True(runtime.ConfirmController().Succeeded);
    }

    [Fact]
    public async Task In_flight_MIDI_fault_cannot_disarm_a_newly_reidentified_capture()
    {
        var root = NewRoot();
        using var release = new ManualResetEventSlim();
        try
        {
            using var backend = new MidiBackend();
            var midi = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            midi.Faulted += (_, _) =>
            {
                entered.TrySetResult();
                release.Wait();
            };
            var keyboard = new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboard, output, new AtomicProfileStore(root), midiInputProvider: midi);
            await runtime.InitializeAsync();
            IdentifyMidi(runtime, backend);
            Assert.True(runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24").Succeeded);

            var oldFault = Task.Run(() => backend.Fail(new IOException("The old MIDI stream failed.")));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IdentifyMidi(runtime, backend);
            var newGeneration = midi.CurrentCaptureGeneration;
            runtime.IsRehearsal = false;
            backend.Publish(0x00643C90);
            release.Set();
            await oldFault.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(newGeneration, midi.CurrentCaptureGeneration);
            Assert.False(runtime.IsRehearsal);
            Assert.Equal(1, output.Down);
            Assert.Equal(0, output.Up);
        }
        finally
        {
            release.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    private static ControllerActionAssignment ReleaseMacro() => new(
        "Release-only text macro", new ControllerActionSequenceDefinition(),
        new ControllerActionSequenceDefinition
        {
            Steps = [new() { Type = ControllerActionStepType.Text, Value = "Only an intentional release may type this." }],
        });

    private static SanitizedDeviceDescriptor G13() => new(
        (nint)301, "g13-lifecycle-session", "g13-lifecycle-persistent", new string('A', 64),
        RawInputDeviceKind.Hid, 0x046D, 0xC21C, 0xFF00, 0, "Logitech G13");

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Tappy-InputLifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class G13Enumerator(SanitizedDeviceDescriptor descriptor) : ILogitechG13DeviceEnumerator
    {
        public IReadOnlyList<SanitizedDeviceDescriptor> Devices { get; set; } = [descriptor];
        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers() => Devices;
        public SanitizedDeviceDescriptor? DescribeController(nint handle) => Devices.FirstOrDefault(d => d.SessionHandle == handle);
    }

    private sealed class KeyboardEnumerator(params SanitizedDeviceDescriptor[] descriptors) : IRawInputDeviceEnumerator
    {
        public IReadOnlyList<SanitizedDeviceDescriptor> Devices { get; set; } = descriptors;
        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => Devices;
        public SanitizedDeviceDescriptor? DescribeKeyboard(nint handle) => Devices.FirstOrDefault(d => d.SessionHandle == handle);
    }

    private sealed class TestHost : IRawInputMessageHost, IRawHidInputMessageHost
    {
        public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;
        public event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived;
        public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;
        public event EventHandler<Exception>? Faulted;
        public bool IsRunning => true;
        public nint WindowHandle => 0;
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void EmitHid(ulong buttons) => HidPacketReceived?.Invoke(this, new(new((nint)301, LogitechG13Protocol.InputReportId, 128, 128, buttons)));
        public void EmitRemoval(nint handle) => DeviceChanged?.Invoke(this, new(handle, RawInputDeviceChangeKind.Removal));
        public void EmitLifecycle(WindowsLifecycleSignal signal) => LifecycleChanged?.Invoke(this, new(signal));
        public void EmitFault() => Faulted?.Invoke(this, new IOException("Simulated input failure"));
        public void EmitKey(nint handle, RawKeyboardFlags flags, ushort scan = 0x4F, ushort virtualKey = 0x61) => KeyboardPacketReceived?.Invoke(this,
            new(new RawKeyboardPacket(handle, scan, flags, 0, virtualKey, 0, 0)));
    }

    private sealed class MidiBackend : IWinMmMidiInputBackend
    {
        private IReadOnlyList<WinMmMidiInputDevice> _devices = [new(0, "APC MINI", 1, 2, 3)];
        public IReadOnlyList<WinMmMidiInputDevice> Devices
        {
            get => Volatile.Read(ref _devices);
            set => Volatile.Write(ref _devices, value);
        }
        public event Action<WinMmMidiInputMessage>? ShortMessageReceived;
        public event EventHandler<Exception>? Faulted;
        public long SessionGeneration { get; private set; }
        public IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices() => Devices;
        public void Open(int id) => SessionGeneration++;
        public void Start() { }
        public void Stop() { }
        public void Close() { }
        public void Dispose() { }
        public void Publish(uint value) => ShortMessageReceived?.Invoke(new(SessionGeneration, value));
        public void Fail(Exception exception) => Faulted?.Invoke(this, exception);
    }

    private sealed class RecordingKeyboard : IKeyboardOutput
    {
        public bool RejectRelease { get; set; }
        public int Down { get; private set; }
        public int Up { get; private set; }
        public void KeyDown(KeyboardOutputRequest request) => Down++;
        public void KeyUp(KeyboardOutputRequest request)
        {
            if (RejectRelease) { throw new IOException("Simulated output cleanup failure"); }
            Up++;
        }
    }

    private sealed class RecordingActions : IControllerActionOutput
    {
        public int Starts { get; private set; }
        public event EventHandler<ControllerActionOutputFault>? Faulted { add { } remove { } }
        public bool Start(ControllerActionOutputRequest request) { Starts++; return true; }
        public bool ReleaseOwner(string owner) => true;
        public bool ReleaseScope(string scope) => true;
        public bool ReleaseAll() => true;
    }
}
