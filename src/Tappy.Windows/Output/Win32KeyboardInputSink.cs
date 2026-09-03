using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tappy.Windows.Output;

public sealed class Win32KeyboardInputSink : IKeyboardInputSink
{
    public int Send(IReadOnlyList<KeyboardInjection> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return 0;
        }

        var native = new NativeInput[inputs.Count];
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            native[index] = new NativeInput
            {
                Type = NativeInput.KeyboardType,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = input.VirtualKey,
                        ScanCode = input.ScanCode,
                        Flags = (uint)input.Flags,
                        ExtraInformation = input.ExtraInformation,
                    },
                },
            };
        }

        var inserted = SendInput(
            checked((uint)native.Length),
            native,
            Marshal.SizeOf<NativeInput>());
        if (inserted == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected Tappy keyboard output.");
        }

        return checked((int)inserted);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal const uint KeyboardType = 1;

        internal uint Type;
        internal NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        internal NativeMouseInput Mouse;

        [FieldOffset(0)]
        internal NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int structureSize);
}
