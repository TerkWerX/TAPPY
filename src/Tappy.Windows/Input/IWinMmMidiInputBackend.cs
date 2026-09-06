namespace Tappy.Windows.Input;

internal sealed record WinMmMidiInputDevice(
    int DeviceId,
    string Name,
    ushort ManufacturerId,
    ushort ProductId,
    uint DriverVersion);

internal interface IWinMmMidiInputBackend : IDisposable
{
    event Action<uint>? ShortMessageReceived;

    IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices();

    void Open(int deviceId);

    void Start();

    void Stop();

    void Close();
}
