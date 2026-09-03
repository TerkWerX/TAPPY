namespace Tappy.Windows.Output;

[Flags]
public enum KeyboardInjectionFlags : uint
{
    None = 0,
    ExtendedKey = 0x0001,
    KeyUp = 0x0002,
    Unicode = 0x0004,
    ScanCode = 0x0008,
}

public readonly record struct KeyboardInjection(
    ushort VirtualKey,
    ushort ScanCode,
    KeyboardInjectionFlags Flags,
    uint ExtraInformation);

public interface IKeyboardInputSink
{
    int Send(IReadOnlyList<KeyboardInjection> inputs);
}
