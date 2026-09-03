using System.Diagnostics;
using Tappy.Core.Input;
using Tappy.Core.Profiles;

namespace Tappy.Core.Abstractions;

public interface IMonotonicClock
{
    long GetTimestamp();
    long TimestampFrequency { get; }
}

public sealed class SystemMonotonicClock : IMonotonicClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public long TimestampFrequency => Stopwatch.Frequency;
}

public interface IInputDeviceProvider : IAsyncDisposable
{
    event Action<ControlSignal>? SignalReceived;
    event Action? DevicesChanged;
    IReadOnlyList<ControllerIdentity> ConnectedControllers { get; }
    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IProfileStore
{
    ValueTask<TappyProfileSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(TappyProfileSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed record ForegroundContextSnapshot(
    string ProcessName,
    string? ExecutablePath,
    string WindowTitle);

public interface IForegroundContext
{
    ForegroundContextSnapshot? GetCurrent();
}

public sealed record CoreDiagnosticEvent(
    string Category,
    string Message,
    long Timestamp,
    string? ControllerId = null,
    string? ControlId = null);

public interface IDiagnosticsSink
{
    void Record(CoreDiagnosticEvent diagnosticEvent);
}

public interface IControllerRegistry
{
    bool TryGetLayout(string registryKey, out ControllerLayoutSnapshot? layout);
}
