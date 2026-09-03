using System.Globalization;
using Tappy.Core.Output;

namespace Tappy.Windows.Output;

/// <summary>
/// Keyboard output tagged with Tappy's process marker. Tap batches always contain a
/// balanced down/up sequence; held output balancing is owned by the Core ledger.
/// </summary>
public sealed class SendInputKeyboardOutput : IKeyboardOutput
{
    private readonly IKeyboardInputSink _sink;

    public SendInputKeyboardOutput()
        : this(new Win32KeyboardInputSink())
    {
    }

    public SendInputKeyboardOutput(IKeyboardInputSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public uint Marker => InjectedInputMarker.Value;

    public void KeyDown(KeyboardOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMarker(request);
        SendChecked(request.Keys.Select(key => CreateVirtualKeyInjection(key, keyUp: false)).ToArray());
    }

    public void KeyUp(KeyboardOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMarker(request);
        SendChecked(request.Keys.Select(key => CreateVirtualKeyInjection(key, keyUp: true)).ToArray());
    }

    public void Tap(IReadOnlyList<KeyboardOutputKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var inputs = new List<KeyboardInjection>(checked(keys.Count * 2));
        inputs.AddRange(keys.Select(key => CreateVirtualKeyInjection(key, keyUp: false)));
        inputs.AddRange(keys.Reverse().Select(key => CreateVirtualKeyInjection(key, keyUp: true)));
        SendChecked(inputs);
    }

    public void PressScanCode(ushort scanCode, bool extended = false) =>
        SendChecked([CreateScanCodeInjection(scanCode, extended, keyUp: false)]);

    public void ReleaseScanCode(ushort scanCode, bool extended = false) =>
        SendChecked([CreateScanCodeInjection(scanCode, extended, keyUp: true)]);

    public void TapScanCode(ushort scanCode, bool extended = false) =>
        SendChecked(
        [
            CreateScanCodeInjection(scanCode, extended, keyUp: false),
            CreateScanCodeInjection(scanCode, extended, keyUp: true),
        ]);

    private void SendChecked(IReadOnlyList<KeyboardInjection> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var inserted = _sink.Send(inputs);
        if (inserted == inputs.Count)
        {
            return;
        }

        // A partial SendInput batch can strand a key. Best-effort releases are
        // emitted before surfacing the failure to the engine.
        var recovery = inputs
            .Where(input => (input.Flags & KeyboardInjectionFlags.KeyUp) == 0)
            .Reverse()
            .Select(input => input with { Flags = input.Flags | KeyboardInjectionFlags.KeyUp })
            .ToArray();
        if (recovery.Length > 0)
        {
            _ = _sink.Send(recovery);
        }

        throw new InvalidOperationException(
            $"Windows accepted {inserted.ToString(CultureInfo.InvariantCulture)} of " +
            $"{inputs.Count.ToString(CultureInfo.InvariantCulture)} keyboard events.");
    }

    private static void ValidateMarker(KeyboardOutputRequest request)
    {
        if (request.InjectionMarker != InjectedInputMarker.Value)
        {
            throw new InvalidOperationException(
                "The mapping engine injection marker does not match this Tappy process. Output was refused.");
        }
    }

    private static KeyboardInjection CreateScanCodeInjection(
        ushort scanCode,
        bool extended,
        bool keyUp)
    {
        if (scanCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scanCode), "A non-zero scan code is required.");
        }

        var flags = KeyboardInjectionFlags.ScanCode;
        if (extended)
        {
            flags |= KeyboardInjectionFlags.ExtendedKey;
        }

        if (keyUp)
        {
            flags |= KeyboardInjectionFlags.KeyUp;
        }

        return new KeyboardInjection(0, scanCode, flags, InjectedInputMarker.Value);
    }

    private static KeyboardInjection CreateVirtualKeyInjection(KeyboardOutputKey key, bool keyUp)
    {
        var (virtualKey, extended) = TranslateKey(key.Value);
        var flags = extended ? KeyboardInjectionFlags.ExtendedKey : KeyboardInjectionFlags.None;
        if (keyUp)
        {
            flags |= KeyboardInjectionFlags.KeyUp;
        }

        return new KeyboardInjection(virtualKey, 0, flags, InjectedInputMarker.Value);
    }

    private static (ushort VirtualKey, bool Extended) TranslateKey(string value)
    {
        if (value.Length == 1)
        {
            var character = value[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return (character, false);
            }
        }

        if (value.StartsWith('F') &&
            int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function) &&
            function is >= 1 and <= 24)
        {
            return (checked((ushort)(0x6F + function)), false);
        }

        return value switch
        {
            "BACKSPACE" => (0x08, false),
            "TAB" => (0x09, false),
            "PAUSE" => (0x13, false),
            "CAPSLOCK" => (0x14, false),
            "ENTER" => (0x0D, false),
            "SHIFT" or "LEFTSHIFT" => (0xA0, false),
            "RIGHTSHIFT" => (0xA1, false),
            "CTRL" or "LEFTCTRL" => (0xA2, false),
            "RIGHTCTRL" => (0xA3, true),
            "ALT" or "LEFTALT" => (0xA4, false),
            "RIGHTALT" => (0xA5, true),
            "ESCAPE" => (0x1B, false),
            "SPACE" => (0x20, false),
            "PAGEUP" => (0x21, true),
            "PAGEDOWN" => (0x22, true),
            "END" => (0x23, true),
            "HOME" => (0x24, true),
            "LEFT" => (0x25, true),
            "UP" => (0x26, true),
            "RIGHT" => (0x27, true),
            "DOWN" => (0x28, true),
            "INSERT" => (0x2D, true),
            "DELETE" => (0x2E, true),
            "PRINTSCREEN" => (0x2C, true),
            "WIN" or "LEFTWIN" => (0x5B, true),
            "RIGHTWIN" => (0x5C, true),
            "APPS" => (0x5D, true),
            "NUMPAD0" => (0x60, false),
            "NUMPAD1" => (0x61, false),
            "NUMPAD2" => (0x62, false),
            "NUMPAD3" => (0x63, false),
            "NUMPAD4" => (0x64, false),
            "NUMPAD5" => (0x65, false),
            "NUMPAD6" => (0x66, false),
            "NUMPAD7" => (0x67, false),
            "NUMPAD8" => (0x68, false),
            "NUMPAD9" => (0x69, false),
            "NUMPADMULTIPLY" => (0x6A, false),
            "NUMPADADD" => (0x6B, false),
            "NUMPADSUBTRACT" => (0x6D, false),
            "NUMPADDECIMAL" => (0x6E, false),
            "NUMPADDIVIDE" => (0x6F, true),
            "NUMPADENTER" => (0x0D, true),
            "NUMLOCK" => (0x90, true),
            "SCROLLLOCK" => (0x91, false),
            ";" => (0xBA, false),
            "=" => (0xBB, false),
            "," => (0xBC, false),
            "-" => (0xBD, false),
            "." => (0xBE, false),
            "/" => (0xBF, false),
            "`" => (0xC0, false),
            "[" => (0xDB, false),
            "\\" => (0xDC, false),
            "]" => (0xDD, false),
            "'" => (0xDE, false),
            "BROWSERBACK" => (0xA6, true),
            "BROWSERFORWARD" => (0xA7, true),
            "BROWSERREFRESH" => (0xA8, true),
            "BROWSERSTOP" => (0xA9, true),
            "BROWSERSEARCH" => (0xAA, true),
            "BROWSERFAVORITES" => (0xAB, true),
            "BROWSERHOME" => (0xAC, true),
            "VOLUMEMUTE" => (0xAD, true),
            "VOLUMEDOWN" => (0xAE, true),
            "VOLUMEUP" => (0xAF, true),
            "MEDIANEXT" => (0xB0, true),
            "MEDIAPREVIOUS" => (0xB1, true),
            "MEDIASTOP" => (0xB2, true),
            "MEDIAPLAYPAUSE" => (0xB3, true),
            "LAUNCHMAIL" => (0xB4, true),
            "MEDIASELECT" => (0xB5, true),
            "LAUNCHAPP1" => (0xB6, true),
            "LAUNCHAPP2" => (0xB7, true),
            _ => throw new NotSupportedException($"Keyboard output key '{value}' is not supported."),
        };
    }
}
