using System.Reflection;

namespace Tappy.Windows.Tests;

public sealed class ProductIdentityTests
{
    [Fact]
    public void IdentityMatchesTheCanonicalTappyManifest()
    {
        Assert.Equal("Tappy.exe", ProductIdentity.ExecutableName);
        Assert.Equal("TerkWerX.Tappy", ProductIdentity.AppUserModelId);
        Assert.Equal(@"Local\TerkWerX.Tappy.HandController.0_1", ProductIdentity.SingleInstanceMutexName);
        Assert.Equal("Tappy", ProductIdentity.StartupRegistryValueName);
        Assert.Equal("Tappy", ProductIdentity.LocalDataFolderName);
        Assert.Equal("TappyData", ProductIdentity.PortableDataFolderName);
        Assert.Equal("{B42E5FBB-E4AB-458A-908E-838C8BD101BB}", ProductIdentity.InstallerAppId);
        Assert.Equal(
            "https://api.github.com/repos/TerkWerX/TAPPY/releases/latest",
            ProductIdentity.UpdateEndpointUrl);
        Assert.Equal(0x7Bu, ProductIdentity.EmergencyHotKeyVirtualKey);
        Assert.Equal(0x0007u, ProductIdentity.EmergencyHotKeyModifiers);
    }

    [Fact]
    public void IdentityContainsNoForbiddenSisterApplicationValues()
    {
        var values = typeof(ProductIdentity)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string?)field.GetValue(null))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.All(values, value => Assert.DoesNotContain("Tippy", value, StringComparison.OrdinalIgnoreCase));
        Assert.All(values, value => Assert.DoesNotContain(".tippy", value, StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(Path.Combine("", "Tappy"), ProductIdentity.LocalDataRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileAndEvidenceExtensionsAreUniqueTappyFormats()
    {
        string[] extensions =
        [
            ProductIdentity.ProfileExtension,
            ProductIdentity.LayerExtension,
            ProductIdentity.DeviceExtension,
            ProductIdentity.PassportExtension,
            ProductIdentity.HardwareTestExtension,
            ProductIdentity.DoctorExtension,
            ProductIdentity.ControllerPackExtension,
        ];

        Assert.Equal(extensions.Length, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(extensions, extension => Assert.StartsWith(".tappy", extension, StringComparison.Ordinal));
    }
}
