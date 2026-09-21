namespace Tappy.Windows.Input;

/// <summary>Presentation only: never use these names to merge or authorize devices.</summary>
public static class KeyboardDeviceNames
{
    public static string Resolve(ushort? vendor, ushort? product, IEnumerable<string> paths, string? reportedProduct = null)
    {
        // Inspect the actual interface, not the shared system-container fingerprint.
        var interfaces = paths.ToArray();
        if (interfaces.Length > 0 && interfaces.All(KeyboardInterfacePolicy.IsEHomeInfrared))
        {
            return "Microsoft eHome infrared remote input";
        }

        var reported = CleanReportedName(reportedProduct);
        return (vendor, product) switch
        {
            (0x05A4, 0x9862) => "Targus PAUK10U number pad",
            (0x1532, 0x0201) => "Razer Tartarus keypad",
            (0x1A2C, 0x2D43) => "USB gaming keypad (Freewolf K15 candidate)",
            (0x046D, 0xC52B) => "Logitech Unifying receiver",
            (0x046D, 0xC232) => "Logitech virtual keyboard (C232)",
            _ when !string.IsNullOrWhiteSpace(reported) => $"{reported} — keyboard input",
            (0x046D, _) => "Logitech keyboard input",
            (0x1532, _) => "Razer keyboard input",
            _ => "Keyboard input (model unknown)"
        };
    }

    internal static string? CleanReportedName(string? value)
    {
        var clean = value is null ? null : new string(value.Where(c => !char.IsControl(c) &&
            char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(100).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) || clean.Contains('\\') || clean.Contains('#') ? null : clean;
    }
}
