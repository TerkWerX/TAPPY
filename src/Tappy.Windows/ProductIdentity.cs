namespace Tappy.Windows;

/// <summary>
/// Windows-visible identity owned exclusively by Tappy. Keeping these values in one
/// place makes accidental reuse of the sister application's identity easy to test.
/// </summary>
public static class ProductIdentity
{
    public const string ProductName = "Tappy";
    public const string CompanyName = "TerkWerX";
    public const string ExecutableName = "Tappy.exe";
    public const string RootNamespace = "Tappy";
    public const string AppUserModelId = "TerkWerX.Tappy";
    public const string SingleInstanceMutexName = @"Local\TerkWerX.Tappy.HandController.0_1";
    public const string StartupRegistryValueName = "Tappy";
    public const string LocalDataFolderName = "Tappy";
    public const string PortableDataFolderName = "TappyData";
    public const string InstallerAppId = "{B42E5FBB-E4AB-458A-908E-838C8BD101BB}";
    public const string WebsiteUrl = "https://www.terkwerx.com/tappy/";
    public const string RepositoryUrl = "https://github.com/TerkWerX/TAPPY";
    public const string ProfileExtension = ".tappy.json";
    public const string LayerExtension = ".tappy-layer.json";
    public const string DeviceExtension = ".tappy-device.json";
    public const string PassportExtension = ".tappy-passport.json";
    public const string HardwareTestExtension = ".tappy-hil.json";
    public const string DoctorExtension = ".tappy-doctor.json";
    public const string ControllerPackExtension = ".tappy-controller-pack.zip";

    // MOD_CONTROL | MOD_ALT | MOD_SHIFT + F12. Registration must still detect a
    // conflict at runtime and the tray/mouse release path remains authoritative.
    public const uint EmergencyHotKeyModifiers = 0x0002 | 0x0001 | 0x0004;
    public const uint EmergencyHotKeyVirtualKey = 0x7B;

    public static string LocalDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LocalDataFolderName);
}
