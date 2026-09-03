using System.Runtime.InteropServices;

namespace Tappy.Windows.Input;

internal interface IDeviceContainerIdResolver
{
    Guid? Resolve(string deviceInterfacePath);
}

/// <summary>
/// Resolves a Raw Input device-interface path to the PnP devnode that owns that
/// exact interface, then reads DEVPKEY_Device_ContainerId. No registry layout,
/// friendly-name matching, VID/PID grouping, or arrival-time correlation is used.
/// </summary>
internal sealed class WindowsDeviceContainerIdResolver : IDeviceContainerIdResolver
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DevpropTypeGuid = 0x0000000D;
    private static readonly nint InvalidHandleValue = new(-1);

    // DEFINE_DEVPROPKEY(DEVPKEY_Device_ContainerId,
    // 0x8c7ed206,0x3f8a,0x4827,0xb3,0xab,0xae,0x9e,0x1f,0xae,0xfc,0x6c,2)
    private static readonly DevPropKey ContainerIdProperty = new()
    {
        FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        PropertyId = 2,
    };

    public Guid? Resolve(string deviceInterfacePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInterfacePath) ||
            !TryReadTrailingInterfaceClass(deviceInterfacePath, out var interfaceClass))
        {
            return null;
        }

        var deviceInfoSet = SetupDiGetClassDevs(
            ref interfaceClass,
            null,
            nint.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandleValue)
        {
            return null;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData
                {
                    Size = checked((uint)Marshal.SizeOf<DeviceInterfaceData>()),
                };
                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        nint.Zero,
                        ref interfaceClass,
                        index,
                        ref interfaceData))
                {
                    return null;
                }

                if (TryReadInterface(
                        deviceInfoSet,
                        ref interfaceData,
                        out var enumeratedPath,
                        out var deviceInfo) &&
                    string.Equals(
                        NormalizeInterfacePath(enumeratedPath),
                        NormalizeInterfacePath(deviceInterfacePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return TryReadContainerId(deviceInfoSet, ref deviceInfo);
                }
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryReadInterface(
        nint deviceInfoSet,
        ref DeviceInterfaceData interfaceData,
        out string path,
        out DeviceInfoData deviceInfo)
    {
        path = string.Empty;
        deviceInfo = new DeviceInfoData
        {
            Size = checked((uint)Marshal.SizeOf<DeviceInfoData>()),
        };

        _ = SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            nint.Zero,
            0,
            out var requiredBytes,
            nint.Zero);
        if (requiredBytes == 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W has a platform-specific cbSize,
            // while DevicePath starts at byte offset four in the native structure.
            Marshal.WriteInt32(buffer, IntPtr.Size == sizeof(long) ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    buffer,
                    requiredBytes,
                    out _,
                    ref deviceInfo))
            {
                return false;
            }

            path = Marshal.PtrToStringUni(nint.Add(buffer, sizeof(uint))) ?? string.Empty;
            return path.Length != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Guid? TryReadContainerId(nint deviceInfoSet, ref DeviceInfoData deviceInfo)
    {
        var propertyKey = ContainerIdProperty;
        var buffer = new byte[16];
        if (!SetupDiGetDeviceProperty(
                deviceInfoSet,
                ref deviceInfo,
                ref propertyKey,
                out var propertyType,
                buffer,
                checked((uint)buffer.Length),
                out var requiredBytes,
                0) ||
            propertyType != DevpropTypeGuid ||
            requiredBytes != buffer.Length)
        {
            return null;
        }

        var value = new Guid(buffer);
        return value == Guid.Empty ? null : value;
    }

    private static bool TryReadTrailingInterfaceClass(string path, out Guid interfaceClass)
    {
        interfaceClass = Guid.Empty;
        var marker = path.LastIndexOf("#{", StringComparison.Ordinal);
        return marker >= 0 && Guid.TryParse(path.AsSpan(marker + 1), out interfaceClass);
    }

    private static string NormalizeInterfacePath(string path) =>
        path.Trim().TrimEnd('\0');

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        internal uint Size;
        internal Guid InterfaceClassGuid;
        internal uint Flags;
        internal nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        internal uint Size;
        internal Guid ClassGuid;
        internal uint DeviceInstance;
        internal nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        internal Guid FormatId;
        internal uint PropertyId;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        nint parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet,
        nint deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        nint deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        ref DeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(
        nint deviceInfoSet,
        ref DeviceInfoData deviceInfoData,
        ref DevPropKey propertyKey,
        out uint propertyType,
        [Out] byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
