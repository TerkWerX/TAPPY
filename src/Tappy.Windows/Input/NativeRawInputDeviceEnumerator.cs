using System.ComponentModel;
using System.Runtime.InteropServices;
using Tappy.Windows.Interop;

namespace Tappy.Windows.Input;

public sealed class NativeRawInputDeviceEnumerator : IRawInputDeviceEnumerator
{
    private readonly IDeviceContainerIdResolver _containerIdResolver;

    public NativeRawInputDeviceEnumerator()
        : this(new WindowsDeviceContainerIdResolver())
    {
    }

    internal NativeRawInputDeviceEnumerator(IDeviceContainerIdResolver containerIdResolver)
    {
        _containerIdResolver = containerIdResolver ?? throw new ArgumentNullException(nameof(containerIdResolver));
    }

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards()
    {
        var structureSize = checked((uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDeviceList>());
        uint count = 0;
        var result = RawInputNativeMethods.GetRawInputDeviceList(nint.Zero, ref count, structureSize);
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw Input device enumeration failed.");
        }

        if (count == 0)
        {
            return Array.Empty<SanitizedDeviceDescriptor>();
        }

        var bytes = checked((int)(count * structureSize));
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            result = RawInputNativeMethods.GetRawInputDeviceList(buffer, ref count, structureSize);
            if (result == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw Input device enumeration failed.");
            }

            var candidates = new List<RawKeyboardDeviceCandidate>(checked((int)count));
            for (var index = 0; index < count; index++)
            {
                var itemPointer = nint.Add(buffer, checked((int)(index * structureSize)));
                var item = Marshal.PtrToStructure<RawInputNativeMethods.RawInputDeviceList>(itemPointer);
                if (item.Type != RawInputNativeMethods.RimTypeKeyboard)
                {
                    continue;
                }

                var rawPath = GetDevicePath(item.Device);
                if (!string.IsNullOrWhiteSpace(rawPath))
                {
                    var interfaceDescriptor = DeviceDescriptorSanitizer.CreateKeyboard(item.Device, rawPath);
                    if (LogitechG13Protocol.IsVirtualKeyboard(
                            interfaceDescriptor.VendorId,
                            interfaceDescriptor.ProductId))
                    {
                        // Logitech's C232 interface is a software-emulated virtual
                        // keyboard, not a selectable physical controller. Exposing
                        // it risks feedback and false identity; the physical C21C
                        // collection is handled separately and is never linked to it.
                        continue;
                    }

                    candidates.Add(new RawKeyboardDeviceCandidate(
                        item.Device,
                        rawPath,
                        interfaceDescriptor.VendorId,
                        interfaceDescriptor.ProductId,
                        _containerIdResolver.Resolve(rawPath)));
                }
            }

            return CreatePhysicalDescriptors(candidates);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle)
    {
        var grouped = EnumerateKeyboards()
            .FirstOrDefault(descriptor => descriptor.ContainsSessionHandle(deviceHandle));
        if (grouped is not null)
        {
            return grouped;
        }

        // Arrival notifications can race the inventory snapshot. Describing only
        // this exact interface remains safe; a subsequent enumeration will fold it
        // into its authoritative Windows container if peers are visible.
        var rawPath = GetDevicePath(deviceHandle);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var fallback = DeviceDescriptorSanitizer.CreateKeyboard(deviceHandle, rawPath);
        if (LogitechG13Protocol.IsVirtualKeyboard(fallback.VendorId, fallback.ProductId))
        {
            return null;
        }

        var containerId = _containerIdResolver.Resolve(rawPath);
        return containerId is null
            ? fallback
            : DeviceDescriptorSanitizer.CreateKeyboardGroup(
                containerId.Value,
                [new RawKeyboardDeviceCandidate(
                    deviceHandle,
                    rawPath,
                    fallback.VendorId,
                    fallback.ProductId,
                    containerId)]);
    }

    internal static IReadOnlyList<SanitizedDeviceDescriptor> CreatePhysicalDescriptors(
        IReadOnlyList<RawKeyboardDeviceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var selectable = candidates.Where(candidate =>
            !LogitechG13Protocol.IsVirtualKeyboard(candidate.VendorId, candidate.ProductId)).ToArray();
        var descriptors = new List<SanitizedDeviceDescriptor>(selectable.Length);

        foreach (var group in selectable
                     .Where(candidate => candidate.ContainerId is not null && candidate.ContainerId != Guid.Empty)
                     .GroupBy(candidate => candidate.ContainerId!.Value))
        {
            descriptors.Add(DeviceDescriptorSanitizer.CreateKeyboardGroup(group.Key, group.ToArray()));
        }

        foreach (var candidate in selectable.Where(
                     candidate => candidate.ContainerId is null || candidate.ContainerId == Guid.Empty))
        {
            descriptors.Add(DeviceDescriptorSanitizer.CreateKeyboard(
                candidate.SessionHandle,
                candidate.RawDevicePath));
        }

        return descriptors
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.PersistentId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetDevicePath(nint deviceHandle)
    {
        uint characterCount = 0;
        var result = RawInputNativeMethods.GetRawInputDeviceInfo(
            deviceHandle,
            RawInputNativeMethods.RidiDeviceName,
            nint.Zero,
            ref characterCount);
        if (result == uint.MaxValue || characterCount == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(checked(((int)characterCount + 1) * sizeof(char)));
        try
        {
            result = RawInputNativeMethods.GetRawInputDeviceInfo(
                deviceHandle,
                RawInputNativeMethods.RidiDeviceName,
                buffer,
                ref characterCount);
            return result == uint.MaxValue
                ? null
                : Marshal.PtrToStringUni(buffer, checked((int)characterCount))?.TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
