namespace Tappy.Windows.Input;

public interface ILogitechG13DeviceEnumerator
{
    IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers();

    SanitizedDeviceDescriptor? DescribeController(nint deviceHandle);
}

public static class LogitechG13Protocol
{
    public const ushort VendorId = 0x046D;
    public const ushort ProductId = 0xC21C;
    public const ushort UsagePage = 0xFF00;
    public const ushort Usage = 0x0000;
    public const byte InputReportId = 0x01;
    public const int InputReportSize = 8;

    internal const ushort VirtualKeyboardProductId = 0xC232;

    internal static bool IsVirtualKeyboard(ushort? vendorId, ushort? productId) =>
        vendorId == VendorId && productId == VirtualKeyboardProductId;
}
