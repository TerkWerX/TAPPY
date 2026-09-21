using System.Text.RegularExpressions;

namespace Tappy.Windows.Input;

/// <summary>Verified model hints for visuals only; never change input identity or authorization.</summary>
public static partial class KeyboardPresentationModels
{
    public const string MicrosoftBluetoothNumberPad = "microsoft-bluetooth-number-pad-0764";

    internal static string? Resolve(string path) => MicrosoftBluetoothInterface().IsMatch(path)
        ? MicrosoftBluetoothNumberPad : null;

    // Observed on the owner's X821504-001. Bluetooth encodes vendor-source + VID
    // differently from USB. Do not change legacy persistent-ID parsing to name it.
    [GeneratedRegex(@"^\\\\\?\\HID#\{00001124-0000-1000-8000-00805F9B34FB\}_VID&0002045E_PID&0764(?:[&#]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MicrosoftBluetoothInterface();
}
