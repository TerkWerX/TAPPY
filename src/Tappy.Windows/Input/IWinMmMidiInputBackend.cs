namespace Tappy.Windows.Input;

internal sealed record WinMmMidiInputDevice(
    int DeviceId,
    string Name,
    ushort ManufacturerId,
    ushort ProductId,
    uint DriverVersion);

internal readonly record struct WinMmMidiInputMessage(long SessionGeneration, uint PackedMessage);

internal sealed class WinMmMidiInputSessionException(long sessionGeneration, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    internal long SessionGeneration { get; } = sessionGeneration;
}

internal interface IWinMmMidiInputBackend : IDisposable
{
    event Action<WinMmMidiInputMessage>? ShortMessageReceived;

    event EventHandler<Exception>? Faulted;

    long SessionGeneration { get; }

    IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices();

    void Open(int deviceId);

    void Start();

    void Stop();

    void Close();
}
