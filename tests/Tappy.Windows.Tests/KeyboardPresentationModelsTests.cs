using System.Security.Cryptography;
using System.Text;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class KeyboardPresentationModelsTests
{
    private const string Path = @"\\?\HID#{00001124-0000-1000-8000-00805F9B34FB}_VID&0002045E_PID&0764&COL01#TEST#GUID";

    [Fact]
    public void Observed_bluetooth_model_gets_visual_hint_without_changing_legacy_identity()
    {
        var device = DeviceDescriptorSanitizer.CreateKeyboard((nint)1, Path.ToLowerInvariant());
        Assert.Equal(KeyboardPresentationModels.MicrosoftBluetoothNumberPad, device.PresentationModelId);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "Tappy.RawInput.Interface.v1\0" + Path.ToUpperInvariant())));
        Assert.Equal($"raw-keyboard:vid-unknown:pid-unknown:sha256-{fingerprint}", device.PersistentId);
        Assert.Null(device.VendorId);
        Assert.Null(device.ProductId);
    }

    [Theory]
    [InlineData("0764", "07640")]
    [InlineData("0764", "0765")]
    [InlineData("0002045E", "0002046D")]
    [InlineData("00001124", "00001125")]
    public void Similar_or_other_bluetooth_devices_do_not_get_the_keypad_layout(string from, string to)
    {
        var device = DeviceDescriptorSanitizer.CreateKeyboard((nint)1, Path.Replace(from, to),
            reportedProduct: "Microsoft Bluetooth Number Pad");
        Assert.Null(device.PresentationModelId);
    }

    [Fact]
    public void Grouped_model_requires_agreement_and_does_not_change_container_identity()
    {
        var container = Guid.NewGuid();
        var a = new RawKeyboardDeviceCandidate((nint)1, Path, null, null, container);
        var b = a with { SessionHandle = (nint)2, RawDevicePath = Path.Replace("COL01", "COL02") };
        var known = DeviceDescriptorSanitizer.CreateKeyboardGroup(container, [a, b]);
        var mixed = DeviceDescriptorSanitizer.CreateKeyboardGroup(container,
            [a, b with { RawDevicePath = @"\\?\HID#UNKNOWN#TEST" }]);
        Assert.Equal(KeyboardPresentationModels.MicrosoftBluetoothNumberPad, known.PresentationModelId);
        Assert.Null(mixed.PresentationModelId);
        Assert.Equal(known.PersistentId, mixed.PersistentId);
    }
}
