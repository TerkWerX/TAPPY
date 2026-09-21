using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tappy.Windows.Input;

internal static class HidProductNameReader
{
    // Adapted from TPE: metadata access only, shared with existing device users.
    internal static string? Read(string path)
    {
        using var handle = CreateFileW(path, 0, 3, nint.Zero, 3, 0, nint.Zero);
        if (handle.IsInvalid) { return null; }
        var buffer = new byte[256];
        return HidD_GetProductString(handle, buffer, buffer.Length)
            ? Encoding.Unicode.GetString(buffer).Split('\0')[0] : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        nint security, uint disposition, uint flags, nint template);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, int length);
}
