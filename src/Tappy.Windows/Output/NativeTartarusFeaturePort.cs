using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Tappy.Windows.Input;

namespace Tappy.Windows.Output;

internal sealed record TartarusFeatureCandidate(string Path, Guid Container, ushort Vendor, ushort Product, ushort FeatureLength,
    ushort UsagePage, ushort Usage);

internal sealed class NativeTartarusFeaturePort(SafeFileHandle handle) : ITartarusFeaturePort
{
    internal static ITartarusFeaturePort OpenConfirmed(string persistentId)
    {
        var path = SelectPath(Enumerate(), persistentId);
        // Access zero permits feature reports on the mouse-class collection without
        // stealing/exclusively opening Windows' mouse input stream.
        var opened = Open(path);
        try
        {
            if (opened.IsInvalid) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the confirmed Tartarus indicator interface."); }
            if (!TryCaps(opened, out var vendor, out var product, out var feature, out var page, out var usage) ||
                vendor != 0x1532 || product != 0x0201 || feature != 91 || page != 1 || usage != 2)
            { throw new IOException("The Tartarus indicator interface changed; no commands were sent."); }
            return new NativeTartarusFeaturePort(opened);
        }
        catch { opened.Dispose(); throw; }
    }

    internal static string PersistentId(Guid container) => DeviceDescriptorSanitizer.CreateKeyboardGroup(container,
        [new RawKeyboardDeviceCandidate(0, "", 0x1532, 0x0201, container)]).PersistentId;

    internal static string SelectPath(IEnumerable<TartarusFeatureCandidate> candidates, string persistentId)
    {
        var matches = candidates.Where(c => c.Vendor == 0x1532 && c.Product == 0x0201 && c.FeatureLength == 91 &&
                c.UsagePage == 1 && c.Usage == 2 && c.Container != Guid.Empty &&
                c.Path.Contains("&mi_02#", StringComparison.OrdinalIgnoreCase) && PersistentId(c.Container) == persistentId)
            .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (matches.Length != 1)
        { throw new IOException("Cannot uniquely match the confirmed Tartarus to its lighting interface. No controller was chosen by guesswork."); }
        return matches[0];
    }

    private static List<TartarusFeatureCandidate> Enumerate()
    {
        var result = new List<TartarusFeatureCandidate>();
        var resolver = new WindowsDeviceContainerIdResolver();
        var guid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        var devices = SetupDiGetClassDevs(ref guid, nint.Zero, nint.Zero, 0x12);
        if (devices == new nint(-1)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(devices, nint.Zero, ref guid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() != 259) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    break;
                }
                SetupDiGetDeviceInterfaceDetail(devices, ref data, nint.Zero, 0, out var bytes, nint.Zero);
                if (bytes is < 8 or > 65536) { throw new IOException("Invalid HID interface path size."); }
                var buffer = Marshal.AllocHGlobal((int)bytes);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devices, ref data, buffer, bytes, out _, nint.Zero))
                    { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    var path = Marshal.PtrToStringUni(buffer + 4) ?? "";
                    if (!path.Contains("vid_1532&pid_0201&mi_02#", StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (resolver.Resolve(path) is not { } container) { continue; }
                    using var device = Open(path);
                    if (device.IsInvalid || !TryCaps(device, out var vendor, out var product, out var feature, out var page, out var usage)) { continue; }
                    result.Add(new(path, container, vendor, product, feature, page, usage));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
        return result;
    }

    private static bool TryCaps(SafeFileHandle device, out ushort vendor, out ushort product, out ushort feature, out ushort page, out ushort usage)
    {
        vendor = product = feature = page = usage = 0;
        var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
        if (!HidD_GetAttributes(device, ref attributes) || !HidD_GetPreparsedData(device, out var parsed)) { return false; }
        var caps = Marshal.AllocHGlobal(64);
        try
        {
            if (HidP_GetCaps(parsed, caps) < 0) { return false; }
            vendor = attributes.Vendor; product = attributes.Product;
            feature = (ushort)Marshal.ReadInt16(caps, 8);
            usage = (ushort)Marshal.ReadInt16(caps); page = (ushort)Marshal.ReadInt16(caps, 2);
            return true;
        }
        finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(parsed); }
    }

    public byte[] Exchange(byte[] request)
    {
        if (request.Length != 91 || request[0] != 0) { throw new ArgumentException("Invalid Tartarus feature report.", nameof(request)); }
        if (!HidD_SetFeature(handle, request, request.Length))
        { throw new Win32Exception(Marshal.GetLastWin32Error(), "Tartarus feature request was rejected."); }
        Thread.Sleep(30); // Off the UI thread, required command/response settling interval.
        var reply = new byte[91];
        if (!HidD_GetFeature(handle, reply, reply.Length))
        { throw new Win32Exception(Marshal.GetLastWin32Error(), "Tartarus feature response could not be read."); }
        return reply;
    }
    public void Dispose() => handle.Dispose();
    private static SafeFileHandle Open(string path) => CreateFileW(path, 0, 3, nint.Zero, 3, 0, nint.Zero);
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { internal int Size; internal Guid Guid; internal int Flags; internal nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { internal int Size; internal ushort Vendor, Product, Version; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, nint enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint devices, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint devices, ref InterfaceData data, nint buffer, uint size, out uint needed, nint device);
    [DllImport("setupapi.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint devices);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref Attributes attributes);
    [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out nint data);
    [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data, nint caps);
    [DllImport("hid.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetFeature(SafeFileHandle device, [Out] byte[] report, int length);
}
