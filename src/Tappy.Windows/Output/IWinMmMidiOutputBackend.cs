namespace Tappy.Windows.Output;

internal sealed record WinMmMidiOutputDevice(int DeviceId, string Name, ushort ManufacturerId, ushort ProductId);

/// <summary>
/// Native WinMM output calls, isolated behind an interface so WinMmMidiOutput's
/// device-resolution, locking and error-wrapping logic can be tested without a
/// real sound driver. See WinMmMidiOutputBackend for the actual P/Invoke and the
/// SysEx buffer-lifetime contract callers must not need to know about.
/// </summary>
internal interface IWinMmMidiOutputBackend : IDisposable
{
    IReadOnlyList<WinMmMidiOutputDevice> EnumerateDevices();

    /// <summary>Opens the device by id, or the system default mapper when null.</summary>
    void Open(int? deviceId);

    void SendShortMessage(uint packedMessage);

    /// <summary>
    /// Sends a complete SysEx message (including the leading F0 and trailing F7)
    /// and does not return until Windows confirms the driver has finished with
    /// the buffer, or the wait times out. Safe to call repeatedly on the same
    /// open handle; each call owns its own native buffer.
    /// </summary>
    void SendSysEx(byte[] data);

    void Reset();

    void Close();
}
