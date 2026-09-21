using System.Runtime.InteropServices;

namespace Tappy.Windows.Input;

/// <summary>
/// Wraps the OS-intercepted DRV_QUERYDEVICEINTERFACESIZE/DRV_QUERYDEVICEINTERFACE
/// messages (Mmddk.h) to recover a WinMM MIDI device's own SetupAPI
/// device-interface path from its numeric device ID. Per Microsoft's own
/// documentation ("Obtaining a Device Interface Name" / DRV_QUERYDEVICEINTERFACE),
/// "the system intercepts this message and returns the appropriate value
/// without sending the message to the device driver" — this is answered
/// directly by winmm.dll for every MIDI driver, not something the Fire/APC
/// MINI's own driver needs to implement. The first xxxMessage parameter is the
/// device ID itself cast to the handle type, not an opened handle, so this
/// never opens or disturbs a MIDI port.
///
/// The returned path is the same shape Raw Input keyboard descriptors already
/// carry (ending "...#{interfaceClassGuid}"), so it feeds
/// WindowsDeviceContainerIdResolver unchanged to obtain
/// DEVPKEY_Device_ContainerId — giving a MIDI device the same physical
/// hardware identity keyboards already get, instead of WinMM's own
/// manufacturer/product/name-only device caps. See D-026 in DECISIONS.md.
/// </summary>
internal static class WinMmDeviceInterfaceQuery
{
    // DRV_RESERVED and the two offsets are documented by Microsoft
    // (learn.microsoft.com/.../drv-querydeviceinterface) but not published as
    // numeric constants there. Confirmed against two independent,
    // ABI-compatible reimplementations of the real Windows headers: Wine's
    // mmsystem.h defines DRV_RESERVED = 0x0800; ReactOS's mmddk.h defines
    // DRV_QUERYDEVICEINTERFACE = DRV_RESERVED + 12 and
    // DRV_QUERYDEVICEINTERFACESIZE = DRV_RESERVED + 13.
    private const uint DrvReserved = 0x0800;
    private const uint DrvQueryDeviceInterface = DrvReserved + 12;
    private const uint DrvQueryDeviceInterfaceSize = DrvReserved + 13;
    private const int MaxInterfacePathBytes = 8192;

    public static string? TryGetMidiInDeviceInterfacePath(int deviceId) =>
        TryGetDeviceInterfacePath(deviceId, midiInMessage);

    public static string? TryGetMidiOutDeviceInterfacePath(int deviceId) =>
        TryGetDeviceInterfacePath(deviceId, midiOutMessage);

    private static string? TryGetDeviceInterfacePath(int deviceId, Func<IntPtr, uint, UIntPtr, UIntPtr, int> sendMessage)
    {
        var deviceHandle = unchecked((IntPtr)deviceId);
        var sizeBuffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(sizeBuffer, 0);
            if (sendMessage(deviceHandle, DrvQueryDeviceInterfaceSize, unchecked((UIntPtr)(nuint)sizeBuffer), UIntPtr.Zero) != 0)
            {
                return null;
            }

            var requiredBytes = unchecked((uint)Marshal.ReadInt32(sizeBuffer));
            if (requiredBytes is 0 or > MaxInterfacePathBytes)
            {
                return null;
            }

            var pathBuffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                if (sendMessage(deviceHandle, DrvQueryDeviceInterface, unchecked((UIntPtr)(nuint)pathBuffer), (UIntPtr)requiredBytes) != 0)
                {
                    return null;
                }

                var path = Marshal.PtrToStringUni(pathBuffer);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            finally
            {
                Marshal.FreeHGlobal(pathBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sizeBuffer);
        }
    }

    [DllImport("winmm.dll")]
    private static extern int midiInMessage(IntPtr deviceId, uint msg, UIntPtr param1, UIntPtr param2);

    [DllImport("winmm.dll")]
    private static extern int midiOutMessage(IntPtr deviceId, uint msg, UIntPtr param1, UIntPtr param2);
}
