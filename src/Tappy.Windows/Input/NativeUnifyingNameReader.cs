using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Tappy.Windows.Interop;

namespace Tappy.Windows.Input;

/// <summary>
/// C52B-only HID++ receiver information reads. Never sends pairing, notification,
/// lighting, key-remapping, or device-mode writes. Input routing stays unchanged.
/// </summary>
internal static class NativeUnifyingNameReader
{
    private static readonly SemaphoreSlim QueryGate = new(1, 1);

    internal static byte[] ReadRequest(byte subRegister)
    {
        if (subRegister is not (>= 0x20 and <= 0x25 or >= 0x40 and <= 0x45))
        { throw new ArgumentOutOfRangeException(nameof(subRegister)); }
        return [0x10, 0xFF, 0x83, 0xB5, subRegister, 0, 0];
    }

    internal static bool IsReply(byte[] data, byte subRegister) =>
        data.Length >= 20 && data[0] == 0x11 && data[1] == 0xFF &&
        data[2] == 0x83 && data[3] == 0xB5 && data[4] == subRegister;

    internal static bool IsError(byte[] data) => data.Length >= 7 &&
        data[0] == 0x10 && data[1] == 0xFF && data[2] == 0x8F && data[3] == 0x83 && data[4] == 0xB5;

    internal static bool IsKeyboard(byte[] reply, byte subRegister) =>
        IsReply(reply, subRegister) && (reply[11] & 0x0F) is 1 or 3;

    internal static string? ReadName(byte[] reply, byte subRegister)
    {
        if (!IsReply(reply, subRegister) || reply[5] == 0 || reply[5] > 14) { return null; }
        var name = KeyboardDeviceNames.CleanReportedName(Encoding.ASCII.GetString(reply, 6, reply[5]));
        return name == "K400 Plus" ? "Logitech K400 Plus" : name;
    }

    internal static async Task<IReadOnlyList<string>> ReadAsync(Guid container)
    {
        // Serialize across device tabs, and bound every query including waiting for the gate.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await QueryGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            var paths = FindInterfaces(container);
            if (paths.Short is null || paths.Long is null) { return []; }
            using var shortHandle = Open(paths.Short, 0xC0000000);
            using var longHandle = Open(paths.Long, 0x80000000);
            if (shortHandle.IsInvalid || longHandle.IsInvalid ||
                !TryGetLengths(shortHandle, out var shortInput, out var shortOutput) ||
                !TryGetLengths(longHandle, out var longInput, out _) ||
                shortInput != 7 || shortOutput != 7 || longInput != 20)
            { return []; }

            using var shortStream = new FileStream(shortHandle, FileAccess.ReadWrite, 1, true);
            using var longStream = new FileStream(longHandle, FileAccess.Read, 1, true);
            using var reads = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var shortRead = ReadReportAsync(shortStream, 7, reads.Token);
            var longRead = ReadReportAsync(longStream, 20, reads.Token);
            try
            {
                async Task<byte[]?> Query(byte sub)
                {
                    await shortStream.WriteAsync(ReadRequest(sub), deadline.Token).ConfigureAwait(false);
                    // A timeout aborts the scan: never misattribute a late reply to another slot.
                    var timeout = Task.Delay(350, deadline.Token);
                    while (true)
                    {
                        var completed = await Task.WhenAny(shortRead, longRead, timeout).ConfigureAwait(false);
                        if (completed == timeout) { throw new OperationCanceledException("Receiver name query timed out."); }
                        var report = await ((Task<byte[]>)completed).ConfigureAwait(false);
                        if (completed == shortRead) { shortRead = ReadReportAsync(shortStream, 7, reads.Token); }
                        else { longRead = ReadReportAsync(longStream, 20, reads.Token); }
                        if (IsReply(report, sub)) { return report; }
                        if (IsError(report)) { return null; }
                    }
                }

                var names = new List<string>();
                for (byte slot = 0; slot < 6; slot++)
                {
                    var sub = (byte)(0x20 + slot);
                    var pairing = await Query(sub).ConfigureAwait(false);
                    if (pairing is null || !IsKeyboard(pairing, sub)) { continue; }
                    var nameSub = (byte)(0x40 + slot);
                    var nameReply = await Query(nameSub).ConfigureAwait(false);
                    // Keep unnamed keyboards represented, so one named keyboard is never mistaken
                    // for the only keyboard sharing the receiver.
                    names.Add(nameReply is not null && ReadName(nameReply, nameSub) is { } name
                        ? name : $"Paired keyboard {slot + 1} (model unknown)");
                }
                return names;
            }
            finally
            {
                reads.Cancel();
                try { await Task.WhenAll(shortRead, longRead).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
            }
        }
        finally { QueryGate.Release(); }
    }

    private static async Task<byte[]> ReadReportAsync(FileStream stream, int length, CancellationToken token)
    {
        var data = new byte[length];
        var count = await stream.ReadAsync(data, token).ConfigureAwait(false);
        if (count == 0) { throw new IOException("Receiver disconnected."); }
        return data[..count];
    }

    private static (string? Short, string? Long) FindInterfaces(Guid container)
    {
        var resolver = new WindowsDeviceContainerIdResolver();
        var size = (uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDeviceList>();
        uint count = 0;
        if (RawInputNativeMethods.GetRawInputDeviceList(nint.Zero, ref count, size) == uint.MaxValue || count == 0) { return default; }
        var capacity = count;
        var buffer = Marshal.AllocHGlobal(checked((int)(count * size)));
        string? shortPath = null, longPath = null;
        try
        {
            var result = RawInputNativeMethods.GetRawInputDeviceList(buffer, ref count, size);
            if (result == uint.MaxValue || result > capacity) { return default; }
            for (var i = 0; i < result; i++)
            {
                var item = Marshal.PtrToStructure<RawInputNativeMethods.RawInputDeviceList>(nint.Add(buffer, checked((int)(i * size))));
                if (item.Type != RawInputNativeMethods.RimTypeHid) { continue; }
                var info = new RawInputNativeMethods.RawInputDeviceInfo { Size = (uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDeviceInfo>() };
                var infoSize = info.Size;
                if (GetRawInputDeviceInfoW(item.Device, RawInputNativeMethods.RidiDeviceInfo, ref info, ref infoSize) == uint.MaxValue ||
                    info.VendorId != 0x046D || info.ProductId != 0xC52B || info.UsagePage != 0xFF00 || info.Usage is not (1 or 2))
                { continue; }
                var path = NativeRawInputDeviceEnumerator.GetDevicePath(item.Device);
                if (path is null || resolver.Resolve(path) != container) { continue; }
                if (info.Usage == 1) { if (shortPath is not null) { return default; } shortPath = path; }
                else { if (longPath is not null) { return default; } longPath = path; }
            }
            return (shortPath, longPath);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static SafeFileHandle Open(string path, uint access) => CreateFileW(path, access, 3, nint.Zero, 3, 0x40000000, nint.Zero);
    private static bool TryGetLengths(SafeFileHandle handle, out int input, out int output)
    {
        input = output = 0;
        if (!HidD_GetPreparsedData(handle, out var parsed)) { return false; }
        var caps = Marshal.AllocHGlobal(64);
        try
        {
            if (HidP_GetCaps(parsed, caps) != 0x00110000) { return false; }
            input = (ushort)Marshal.ReadInt16(caps, 4);
            output = (ushort)Marshal.ReadInt16(caps, 6);
            return true;
        }
        finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(parsed); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetRawInputDeviceInfoW(nint device, uint command, ref RawInputNativeMethods.RawInputDeviceInfo info, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint data, nint caps);
}
