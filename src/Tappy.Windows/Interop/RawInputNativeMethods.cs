using System.Runtime.InteropServices;
using System.Text;

namespace Tappy.Windows.Interop;

internal static class RawInputNativeMethods
{
    internal const uint RidInput = 0x10000003;
    internal const uint RidiDeviceName = 0x20000007;
    internal const uint RidiDeviceInfo = 0x2000000B;
    internal const uint RimTypeKeyboard = 1;
    internal const uint RimTypeHid = 2;
    internal const uint RidevPageOnly = 0x00000020;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RidevDeviceNotify = 0x00002000;
    internal const uint WmInput = 0x00FF;
    internal const uint WmInputDeviceChange = 0x00FE;
    internal const uint WmClose = 0x0010;
    internal const uint WmQuit = 0x0012;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmQueryEndSession = 0x0011;
    internal const uint WmEndSession = 0x0016;
    internal const uint WmPowerBroadcast = 0x0218;
    internal const uint WmWtsSessionChange = 0x02B1;
    internal const nuint RimInput = 0;
    internal const nuint GidcArrival = 1;
    internal const nuint GidcRemoval = 2;
    internal const nuint PbtApmSuspend = 4;
    internal const nuint PbtApmResumeSuspend = 7;
    internal const nuint PbtApmResumeAutomatic = 18;
    internal const nuint WtsSessionLock = 7;
    internal const nuint WtsSessionUnlock = 8;
    internal const uint NotifyForThisSession = 0;
    internal const uint DeviceNotifyWindowHandle = 0;
    internal static readonly nint HwndMessage = new(-3);

    internal static bool AreAllKeyboardKeysReleased()
    {
        for (var virtualKey = 0x03; virtualKey <= 0xFE; virtualKey++)
        {
            // Exclude the mouse-only keys (0x01, 0x02, and 0x04-0x06), the
            // undefined 0x07 slot, and the dedicated gamepad range. VK_CANCEL
            // (0x03) remains included because Ctrl+Break is keyboard input.
            if (virtualKey is >= 0x04 and <= 0x07 or >= 0xC3 and <= 0xDA)
            {
                continue;
            }

            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0)
            {
                return false;
            }
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDeviceList
    {
        internal nint Device;
        internal uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct RawInputDeviceInfo
    {
        [FieldOffset(0)]
        internal uint Size;

        [FieldOffset(4)]
        internal uint Type;

        [FieldOffset(8)]
        internal uint VendorId;

        [FieldOffset(12)]
        internal uint ProductId;

        [FieldOffset(16)]
        internal uint VersionNumber;

        [FieldOffset(20)]
        internal ushort UsagePage;

        [FieldOffset(22)]
        internal ushort Usage;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal WindowProcedure WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    internal delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputDeviceList(nint devices, ref uint deviceCount, uint structureSize);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint dataSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint window, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(in Message message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(in Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint RegisterSuspendResumeNotification(nint recipient, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterSuspendResumeNotification(nint registrationHandle);
}
