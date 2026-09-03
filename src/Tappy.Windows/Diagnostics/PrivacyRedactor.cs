using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tappy.Windows.Diagnostics;

public static partial class PrivacyRedactor
{
    public const string RedactedDevicePath = "<device-path-redacted>";

    public static string RedactDevicePath(string? rawDevicePath) =>
        string.IsNullOrEmpty(rawDevicePath) ? string.Empty : RedactedDevicePath;

    public static string HashForLocalCorrelation(string sensitiveIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensitiveIdentifier);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(sensitiveIdentifier.Trim().ToUpperInvariant())));
    }

    public static string SanitizeDiagnosticText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = RawDevicePathRegex().Replace(value, RedactedDevicePath);
        sanitized = UserProfilePathRegex().Replace(sanitized, @"C:\Users\<redacted>");
        sanitized = ReplaceSensitiveName(sanitized, Environment.UserName, "<user-redacted>");
        sanitized = ReplaceSensitiveName(sanitized, Environment.MachineName, "<computer-redacted>");
        return sanitized;
    }

    private static string ReplaceSensitiveName(string input, string sensitiveName, string replacement) =>
        string.IsNullOrWhiteSpace(sensitiveName)
            ? input
            : Regex.Replace(
                input,
                Regex.Escape(sensitiveName),
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\\\\\?\\[^\s\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex RawDevicePathRegex();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\s\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserProfilePathRegex();
}
