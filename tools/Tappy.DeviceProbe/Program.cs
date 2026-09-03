using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tappy.Windows.Input;

namespace Tappy.DeviceProbe;

internal static class Program
{
    private const uint RidiDeviceInfo = 0x2000000B;

    private static int Main(string[] args)
    {
        if (args.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        var json = args.Length == 1 && args[0].Equals("--json", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 0 && !json)
        {
            Console.Error.WriteLine("Unknown option. This probe intentionally supports descriptor inventory only; it has no watch or key-history mode.");
            PrintHelp();
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Tappy.DeviceProbe requires Windows Raw Input.");
            return 3;
        }

        try
        {
            var keyboards = new NativeRawInputDeviceEnumerator()
                .EnumerateKeyboards()
                .Select(ToKeyboardProbeDescriptor);
            var g13Controllers = new NativeLogitechG13DeviceEnumerator()
                .EnumerateControllers()
                .Select(ToLogitechG13ProbeDescriptor);
            var controllers = keyboards
                .Concat(g13Controllers)
                .OrderBy(controller => controller.DeviceKind, StringComparer.Ordinal)
                .ThenBy(controller => controller.IdentityFingerprint, StringComparer.Ordinal)
                .ToArray();
            if (json)
            {
                var report = new ProbeReport(
                    3,
                    "Tappy",
                    "Descriptor-only Windows inventory. No device was registered for input, opened for reports, or monitored, and no control activity was captured. Physical grouping occurs only when Windows reports a shared DEVPKEY_Device_ContainerId.",
                    controllers);
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));
            }
            else
            {
                Console.WriteLine("Tappy Device Probe — descriptor-only controller inventory");
                Console.WriteLine("No device was registered for input, opened for reports, or monitored. No control activity was captured.");
                Console.WriteLine("Interfaces merge only with an authoritative shared Windows ContainerId; unresolved interfaces stay separate.");
                Console.WriteLine();
                if (controllers.Length == 0)
                {
                    Console.WriteLine("No Raw Input keyboard-class devices were reported by Windows.");
                }

                for (var index = 0; index < controllers.Length; index++)
                {
                    var controller = controllers[index];
                    var usb = controller.VendorId is null || controller.ProductId is null
                        ? "USB identity unavailable"
                        : $"VID_{controller.VendorId} PID_{controller.ProductId}";
                    var usage = controller.UsagePage is null || controller.Usage is null
                        ? "usage unavailable"
                        : $"UP_{controller.UsagePage} U_{controller.Usage}";
                    Console.WriteLine($"[{index + 1}] {controller.DeviceKind} · {usb} · {usage}");
                    Console.WriteLine($"    identity fingerprint {controller.IdentityFingerprint}");
                    Console.WriteLine($"    grouping {controller.Grouping}; Raw Input interfaces {controller.InterfaceCount}");
                    if (controller.ReportedTotalKeys.Count != 0)
                    {
                        Console.WriteLine($"    reported key totals {string.Join(", ", controller.ReportedTotalKeys)}");
                    }

                    if (controller.CodeDefinedControlCount is { } controlCount)
                    {
                        Console.WriteLine($"    code-defined controls {controlCount} (descriptor presence only; not a functional hardware result)");
                    }
                }
            }

            return 0;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OverflowException)
        {
            Console.Error.WriteLine($"Device inventory failed: {exception.Message}");
            return 1;
        }
    }

    private static ProbeControllerDescriptor ToKeyboardProbeDescriptor(SanitizedDeviceDescriptor descriptor)
    {
        var capabilities = descriptor.MemberSessionHandles
            .Select(ReadKeyboardInfo)
            .ToArray();
        return new ProbeControllerDescriptor(
            "Physical keyboard controller",
            descriptor.PathFingerprintSha256,
            descriptor.VendorId?.ToString("X4"),
            descriptor.ProductId?.ToString("X4"),
            descriptor.UsagePage?.ToString("X4"),
            descriptor.Usage?.ToString("X4"),
            descriptor.Grouping.ToString(),
            descriptor.InterfaceCount,
            capabilities.Select(value => value.TotalKeys).Distinct().Order().ToArray(),
            capabilities.Select(value => value.FunctionKeys).Distinct().Order().ToArray(),
            capabilities.Select(value => value.Indicators).Distinct().Order().ToArray(),
            CodeDefinedControlCount: null);
    }

    private static ProbeControllerDescriptor ToLogitechG13ProbeDescriptor(
        SanitizedDeviceDescriptor descriptor) =>
        new(
            "Logitech G13 physical vendor-HID collection",
            descriptor.PathFingerprintSha256,
            descriptor.VendorId?.ToString("X4"),
            descriptor.ProductId?.ToString("X4"),
            descriptor.UsagePage?.ToString("X4"),
            descriptor.Usage?.ToString("X4"),
            descriptor.Grouping.ToString(),
            descriptor.InterfaceCount,
            [],
            [],
            [],
            LogitechG13InputProvider.SupportedControls.Count);

    private static RawKeyboardInfo ReadKeyboardInfo(nint handle)
    {
        var info = new RawInputDeviceInfo
        {
            Size = checked((uint)Marshal.SizeOf<RawInputDeviceInfo>()),
        };
        var byteCount = info.Size;
        var copied = GetRawInputDeviceInfo(handle, RidiDeviceInfo, ref info, ref byteCount);
        return copied == uint.MaxValue ? default : info.Data.Keyboard;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: Tappy.DeviceProbe [--json]");
        Console.WriteLine();
        Console.WriteLine("Lists sanitized physical keyboard and supported-controller descriptors using Windows ContainerId grouping.");
        Console.WriteLine("Container IDs and physical device paths are represented only by domain-separated SHA-256 fingerprints.");
        Console.WriteLine("This tool never registers for input and has no control capture, history, or streaming mode.");
    }

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        nint deviceHandle,
        uint command,
        ref RawInputDeviceInfo data,
        ref uint dataSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceInfo
    {
        internal uint Size;
        internal uint Type;
        internal RawInputDeviceInfoData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawInputDeviceInfoData
    {
        [FieldOffset(0)] internal RawMouseInfo Mouse;
        [FieldOffset(0)] internal RawKeyboardInfo Keyboard;
        [FieldOffset(0)] internal RawHidInfo Hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawMouseInfo
    {
        internal readonly uint Id;
        internal readonly uint NumberOfButtons;
        internal readonly uint SampleRate;
        internal readonly uint HasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawKeyboardInfo
    {
        internal readonly uint Type;
        internal readonly uint SubType;
        internal readonly uint KeyboardMode;
        internal readonly uint FunctionKeys;
        internal readonly uint Indicators;
        internal readonly uint TotalKeys;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawHidInfo
    {
        internal readonly uint VendorId;
        internal readonly uint ProductId;
        internal readonly uint VersionNumber;
        internal readonly ushort UsagePage;
        internal readonly ushort Usage;
    }

    private sealed record ProbeControllerDescriptor(
        string DeviceKind,
        string IdentityFingerprint,
        string? VendorId,
        string? ProductId,
        string? UsagePage,
        string? Usage,
        string Grouping,
        int InterfaceCount,
        IReadOnlyList<uint> ReportedTotalKeys,
        IReadOnlyList<uint> ReportedFunctionKeys,
        IReadOnlyList<uint> ReportedIndicators,
        int? CodeDefinedControlCount);

    private sealed record ProbeReport(
        int SchemaVersion,
        string Product,
        string PrivacyNotice,
        IReadOnlyList<ProbeControllerDescriptor> Controllers);
}
