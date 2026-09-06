using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Tappy.Windows.Input;

namespace Tappy.Windows.Output;

/// <summary>
/// Sends the G13's model-specific 0x07 HID feature report directly to the
/// confirmed 046D:C21C interface. No Logitech-wide RGB target is used.
/// </summary>
public sealed class NativeLogitechG13LightingOutput : ILogitechG13LightingOutput
{
    private readonly NativeLogitechG13DeviceEnumerator _deviceEnumerator;
    private readonly IHidFeatureReportWriter _featureReportWriter;

    public NativeLogitechG13LightingOutput()
        : this(new NativeLogitechG13DeviceEnumerator(), new NativeHidFeatureReportWriter())
    {
    }

    internal NativeLogitechG13LightingOutput(
        NativeLogitechG13DeviceEnumerator deviceEnumerator,
        IHidFeatureReportWriter featureReportWriter)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _featureReportWriter = featureReportWriter ?? throw new ArgumentNullException(nameof(featureReportWriter));
    }

    public void SetBacklightRgb(string controllerPersistentId, byte red, byte green, byte blue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerPersistentId);
        var candidates = _deviceEnumerator.EnumerateRawControllers();
        var targetPaths = SelectTargetPaths(candidates, controllerPersistentId);
        if (targetPaths.Count == 0)
        {
            throw new IOException("The confirmed Logitech G13 lighting interface is no longer available.");
        }

        var report = CreateBacklightFeatureReport(red, green, blue);
        var lastError = 0;
        foreach (var path in targetPaths)
        {
            if (_featureReportWriter.TryWrite(path, report, out lastError))
            {
                return;
            }
        }

        throw lastError == 0
            ? new IOException("Windows did not expose a writable feature-report interface for the confirmed Logitech G13.")
            : new Win32Exception(lastError, "Windows rejected the Logitech G13 backlight feature report.");
    }

    internal static byte[] CreateBacklightFeatureReport(byte red, byte green, byte blue) =>
        [LogitechG13Protocol.BacklightFeatureReportId, red, green, blue, 0];

    internal static IReadOnlyList<string> SelectTargetPaths(
        IReadOnlyList<RawLogitechG13DeviceCandidate> candidates,
        string controllerPersistentId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerPersistentId);
        var target = NativeLogitechG13DeviceEnumerator.CreateDescriptors(candidates)
            .SingleOrDefault(device => string.Equals(
                device.PersistentId,
                controllerPersistentId,
                StringComparison.Ordinal));
        return target is null
            ? []
            : candidates
                .Where(candidate => target.ContainsSessionHandle(candidate.SessionHandle))
                .Select(candidate => candidate.RawDevicePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}

internal interface IHidFeatureReportWriter
{
    bool TryWrite(string devicePath, byte[] report, out int errorCode);
}

internal sealed class NativeHidFeatureReportWriter : IHidFeatureReportWriter
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    public bool TryWrite(string devicePath, byte[] report, out int errorCode)
    {
        using var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            0,
            nint.Zero);
        if (handle.IsInvalid)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        if (!HidD_SetFeature(handle, report, report.Length))
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        errorCode = 0;
        return true;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);
}
