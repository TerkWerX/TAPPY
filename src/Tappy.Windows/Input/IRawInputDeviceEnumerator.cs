namespace Tappy.Windows.Input;

public interface IRawInputDeviceEnumerator
{
    IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards();

    SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle);
}
