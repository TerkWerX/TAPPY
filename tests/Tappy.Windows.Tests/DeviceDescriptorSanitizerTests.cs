using System.Text.Json;
using Tappy.Windows.Diagnostics;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class DeviceDescriptorSanitizerTests
{
    private const string RawPath = @"\\?\HID#VID_046D&PID_C31C&MI_00#7&SECRET&0&0000";

    [Fact]
    public void DescriptorContainsHashAndVidPidButNeverRawPath()
    {
        var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(new nint(17), RawPath);
        var json = JsonSerializer.Serialize(descriptor);

        Assert.Equal((ushort)0x046D, descriptor.VendorId);
        Assert.Equal((ushort)0xC31C, descriptor.ProductId);
        Assert.Matches("^[0-9A-F]{64}$", descriptor.PathFingerprintSha256);
        Assert.Contains(descriptor.PathFingerprintSha256, descriptor.PersistentId, StringComparison.Ordinal);
        Assert.DoesNotContain(RawPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RawPath", json, StringComparison.OrdinalIgnoreCase);
        var coreIdentity = descriptor.ToCoreIdentity();
        Assert.Equal(descriptor.SessionId, coreIdentity.SessionId.Value);
        Assert.Equal(descriptor.PersistentId, coreIdentity.PersistentId?.Value);
        Assert.Equal(Tappy.Core.Input.ControllerIdentityConfidence.PortBound, coreIdentity.Confidence);
    }

    [Fact]
    public void FingerprintIsCaseInsensitiveButPortBound()
    {
        var first = DeviceDescriptorSanitizer.CreateKeyboard(new nint(1), RawPath);
        var same = DeviceDescriptorSanitizer.CreateKeyboard(new nint(2), RawPath.ToLowerInvariant());
        var otherPort = DeviceDescriptorSanitizer.CreateKeyboard(new nint(3), RawPath + "-OTHER-PORT");

        Assert.Equal(first.PathFingerprintSha256, same.PathFingerprintSha256);
        Assert.NotEqual(first.PathFingerprintSha256, otherPort.PathFingerprintSha256);
    }

    [Fact]
    public void DiagnosticRedactorRemovesDeviceAndUserPaths()
    {
        var input = $"device={RawPath} profile=C:\\Users\\SensitivePerson\\profile.json";

        var sanitized = PrivacyRedactor.SanitizeDiagnosticText(input);

        Assert.Contains(PrivacyRedactor.RedactedDevicePath, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SensitivePerson", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
