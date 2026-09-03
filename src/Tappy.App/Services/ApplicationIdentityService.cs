using System.Runtime.InteropServices;

namespace Tappy.App.Services;

public static partial class ApplicationIdentityService
{
    public static void Apply(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        var result = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
