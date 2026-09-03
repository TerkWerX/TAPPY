using Tappy.Windows.Input;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

internal sealed class FakeRawInputDeviceEnumerator(
    params SanitizedDeviceDescriptor[] descriptors) : IRawInputDeviceEnumerator
{
    private SanitizedDeviceDescriptor[] _descriptors = descriptors;

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() =>
        _descriptors.OrderBy(descriptor => descriptor.SessionId, StringComparer.Ordinal).ToArray();

    public SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle) =>
        _descriptors.FirstOrDefault(descriptor =>
            descriptor.SessionHandle == deviceHandle ||
            descriptor.MemberSessionHandles.Contains(deviceHandle));

    internal void SetDescriptors(params SanitizedDeviceDescriptor[] descriptors) =>
        _descriptors = descriptors;
}

internal sealed class FakeRawInputMessageHost : IRawHidInputMessageHost
{
    public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;

    public event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived;

    public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;

    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    public event EventHandler<Exception>? Faulted;

    public bool IsRunning { get; private set; }

    public nint WindowHandle => IsRunning ? new nint(1234) : nint.Zero;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsRunning = false;
        return ValueTask.CompletedTask;
    }

    internal void Emit(RawKeyboardPacket packet) =>
        KeyboardPacketReceived?.Invoke(this, new RawKeyboardPacketEventArgs(packet));

    internal void Emit(RawHidInputPacket packet) =>
        HidPacketReceived?.Invoke(this, new RawHidInputPacketEventArgs(packet));

    internal void EmitDeviceChange(nint handle, RawInputDeviceChangeKind kind) =>
        DeviceChanged?.Invoke(this, new NativeDeviceChangeEventArgs(handle, kind));

    internal void EmitLifecycle(WindowsLifecycleSignal signal) =>
        LifecycleChanged?.Invoke(this, new WindowsLifecycleSignalEventArgs(signal));

    internal void EmitFault(Exception exception) => Faulted?.Invoke(this, exception);
}

internal sealed class FakeLogitechG13DeviceEnumerator(
    params SanitizedDeviceDescriptor[] descriptors) : ILogitechG13DeviceEnumerator
{
    private SanitizedDeviceDescriptor[] _descriptors = descriptors;

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers() =>
        _descriptors.OrderBy(descriptor => descriptor.SessionId, StringComparer.Ordinal).ToArray();

    public SanitizedDeviceDescriptor? DescribeController(nint deviceHandle) =>
        _descriptors.FirstOrDefault(descriptor =>
            descriptor.SessionHandle == deviceHandle ||
            descriptor.MemberSessionHandles.Contains(deviceHandle));

    internal void SetDescriptors(params SanitizedDeviceDescriptor[] descriptors) =>
        _descriptors = descriptors;
}

internal sealed class RecordingKeyboardInputSink : IKeyboardInputSink
{
    internal List<KeyboardInjection[]> Batches { get; } = [];

    internal int? NextResult { get; set; }

    public int Send(IReadOnlyList<KeyboardInjection> inputs)
    {
        Batches.Add(inputs.ToArray());
        var result = NextResult ?? inputs.Count;
        NextResult = null;
        return result;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Tappy.Windows.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
