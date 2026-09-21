namespace Tappy.Windows.Input;

/// <summary>Excludes known non-controller interfaces without changing Windows devices.</summary>
internal static class KeyboardInterfacePolicy
{
    internal static bool IsEHomeInfrared(string path) =>
        path.StartsWith(@"\\?\HID#IRDEVICEV2&", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSelectable(string path, ushort? vendorId, ushort? productId) =>
        !IsEHomeInfrared(path) && !LogitechG13Protocol.IsVirtualKeyboard(vendorId, productId);
}
