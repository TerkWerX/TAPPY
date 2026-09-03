namespace Tappy.Core.Output;

/// <summary>
/// Defines the portable key names understood by Tappy's keyboard-output contract.
/// Platform backends must support every value in <see cref="SupportedKeys"/>.
/// </summary>
public static class KeyboardOutputCapabilities
{
    private static readonly string[] KeyNames =
    [
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString()),
        .. Enumerable.Range(0, 10).Select(value => value.ToString()),
        .. Enumerable.Range(1, 24).Select(value => $"F{value}"),
        "BACKSPACE", "TAB", "ENTER", "ESCAPE", "SPACE",
        "PAGEUP", "PAGEDOWN", "END", "HOME", "LEFT", "UP", "RIGHT", "DOWN",
        "INSERT", "DELETE",
        "SHIFT", "LEFTSHIFT", "RIGHTSHIFT",
        "CTRL", "LEFTCTRL", "RIGHTCTRL",
        "ALT", "LEFTALT", "RIGHTALT",
        "WIN", "LEFTWIN", "RIGHTWIN",
        "CAPSLOCK", "NUMLOCK", "SCROLLLOCK", "PRINTSCREEN", "PAUSE", "APPS",
        .. Enumerable.Range(0, 10).Select(value => $"NUMPAD{value}"),
        "NUMPADMULTIPLY", "NUMPADADD", "NUMPADSUBTRACT", "NUMPADDECIMAL",
        "NUMPADDIVIDE", "NUMPADENTER",
        ";", "=", ",", "-", ".", "/", "`", "[", "\\", "]", "'",
        "BROWSERBACK", "BROWSERFORWARD", "BROWSERREFRESH", "BROWSERSTOP",
        "BROWSERSEARCH", "BROWSERFAVORITES", "BROWSERHOME",
        "VOLUMEMUTE", "VOLUMEDOWN", "VOLUMEUP",
        "MEDIANEXT", "MEDIAPREVIOUS", "MEDIASTOP", "MEDIAPLAYPAUSE", "MEDIASELECT",
        "LAUNCHMAIL", "LAUNCHAPP1", "LAUNCHAPP2"
    ];

    private static readonly HashSet<string> Lookup = new(KeyNames, StringComparer.Ordinal);

    public static IReadOnlyList<string> SupportedKeys { get; } = Array.AsReadOnly(KeyNames);

    public static bool IsSupported(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Lookup.Contains(new KeyboardOutputKey(value).Value);
    }

    public static bool IsSupported(KeyboardOutputKey key) =>
        !key.IsEmpty && Lookup.Contains(key.Value);
}
