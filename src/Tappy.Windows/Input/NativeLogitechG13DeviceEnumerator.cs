using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tappy.Windows.Interop;

namespace Tappy.Windows.Input;

/// <summary>
/// Enumerates only the physical C21C G13 vendor collection. Logitech's separate
/// C232 virtual-keyboard interface is intentionally outside this provider.
/// </summary>
public sealed class NativeLogitechG13DeviceEnumerator : ILogitechG13DeviceEnumerator
{
    private readonly IDeviceContainerIdResolver _containerIdResolver;

    public NativeLogitechG13DeviceEnumerator()
        : this(new WindowsDeviceContainerIdResolver())
    {
    }

    internal NativeLogitechG13DeviceEnumerator(IDeviceContainerIdResolver containerIdResolver)
    {
        _containerIdResolver = containerIdResolver ?? throw new ArgumentNullException(nameof(containerIdResolver));
    }

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers()
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

        var capacity = count;
        var buffer = Marshal.AllocHGlobal(checked((int)(capacity * structureSize)));
        try
        {
            result = RawInputNativeMethods.GetRawInputDeviceList(buffer, ref count, structureSize);
            if (result == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw Input device enumeration failed.");
            }

            if (result > capacity || count > capacity)
            {
                throw new Win32Exception("Raw Input device inventory grew while it was being read.");
            }

            var candidates = new List<RawLogitechG13DeviceCandidate>();
            for (var index = 0u; index < result; index++)
            {
                var itemPointer = nint.Add(buffer, checked((int)(index * structureSize)));
                var item = Marshal.PtrToStructure<RawInputNativeMethods.RawInputDeviceList>(itemPointer);
                if (item.Type != RawInputNativeMethods.RimTypeHid || !IsPhysicalG13Collection(item.Device))
                {
                    continue;
                }

                var rawPath = GetDevicePath(item.Device);
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                candidates.Add(new RawLogitechG13DeviceCandidate(
                    item.Device,
                    rawPath,
                    _containerIdResolver.Resolve(rawPath)));
            }

            return CreateDescriptors(candidates);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public SanitizedDeviceDescriptor? DescribeController(nint deviceHandle)
    {
        var descriptor = EnumerateControllers()
            .FirstOrDefault(candidate => candidate.ContainsSessionHandle(deviceHandle));
        if (descriptor is not null)
        {
            return descriptor;
        }

        if (!IsPhysicalG13Collection(deviceHandle))
        {
            return null;
        }

        var rawPath = GetDevicePath(deviceHandle);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        return CreateDescriptors(
        [
            new RawLogitechG13DeviceCandidate(
                deviceHandle,
                rawPath,
                _containerIdResolver.Resolve(rawPath)),
        ]).Single();
    }

    internal static IReadOnlyList<SanitizedDeviceDescriptor> CreateDescriptors(
        IReadOnlyList<RawLogitechG13DeviceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var descriptors = new List<SanitizedDeviceDescriptor>(candidates.Count);

        foreach (var group in candidates
                     .Where(candidate => candidate.ContainerId is not null && candidate.ContainerId != Guid.Empty)
                     .GroupBy(candidate => candidate.ContainerId!.Value))
        {
            descriptors.Add(CreateDescriptor(group.ToArray(), group.Key));
        }

        foreach (var candidate in candidates.Where(
                     candidate => candidate.ContainerId is null || candidate.ContainerId == Guid.Empty))
        {
            descriptors.Add(CreateDescriptor([candidate], containerId: null));
        }

        return descriptors
            .OrderBy(descriptor => descriptor.PersistentId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SanitizedDeviceDescriptor CreateDescriptor(
        IReadOnlyList<RawLogitechG13DeviceCandidate> members,
        Guid? containerId)
    {
        if (members.Count == 0)
        {
            throw new ArgumentException("A G13 descriptor needs at least one interface.", nameof(members));
        }

        var isContainerGrouped = containerId is not null && containerId != Guid.Empty;
        var identitySource = isContainerGrouped
            ? containerId!.Value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant()
            : members[0].RawDevicePath.Trim().ToUpperInvariant();
        var identityDomain = isContainerGrouped
            ? "Tappy.RawInput.LogitechG13.Container.v1"
            : "Tappy.RawInput.LogitechG13.Interface.v1";
        var fingerprint = DomainSeparatedHash(identityDomain, identitySource);
        var sessionHandle = isContainerGrouped
            ? CreateLogicalSessionHandle(fingerprint)
            : members[0].SessionHandle;
        var groupingName = isContainerGrouped ? "container" : "interface";

        return new SanitizedDeviceDescriptor(
            sessionHandle,
            isContainerGrouped
                ? $"raw-g13-container-session-{fingerprint[..16]}"
                : $"raw-g13-session-{unchecked((nuint)sessionHandle):X}",
            $"raw-hid-g13:vid-046d:pid-c21c:upff00:u0000:{groupingName}-sha256-{fingerprint}",
            fingerprint,
            RawInputDeviceKind.Hid,
            LogitechG13Protocol.VendorId,
            LogitechG13Protocol.ProductId,
            LogitechG13Protocol.UsagePage,
            LogitechG13Protocol.Usage,
            "Logitech G13")
        {
            MemberSessionHandles = members
                .Select(member => member.SessionHandle)
                .Distinct()
                .OrderBy(handle => unchecked((nuint)handle))
                .ToArray(),
            Grouping = isContainerGrouped
                ? PhysicalDeviceGrouping.WindowsContainerId
                : PhysicalDeviceGrouping.RawInputInterfaceFallback,
        };
    }

    private static bool IsPhysicalG13Collection(nint deviceHandle)
    {
        var structureSize = checked((uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDeviceInfo>());
        var nativeInfo = new RawInputNativeMethods.RawInputDeviceInfo
        {
            Size = structureSize,
        };
        var buffer = Marshal.AllocHGlobal(checked((int)structureSize));
        try
        {
            Marshal.StructureToPtr(nativeInfo, buffer, fDeleteOld: false);
            var dataSize = structureSize;
            var result = RawInputNativeMethods.GetRawInputDeviceInfo(
                deviceHandle,
                RawInputNativeMethods.RidiDeviceInfo,
                buffer,
                ref dataSize);
            if (result == uint.MaxValue || dataSize < 24)
            {
                return false;
            }

            nativeInfo = Marshal.PtrToStructure<RawInputNativeMethods.RawInputDeviceInfo>(buffer);
            return nativeInfo.Type == RawInputNativeMethods.RimTypeHid &&
                nativeInfo.VendorId == LogitechG13Protocol.VendorId &&
                nativeInfo.ProductId == LogitechG13Protocol.ProductId &&
                nativeInfo.UsagePage == LogitechG13Protocol.UsagePage &&
                nativeInfo.Usage == LogitechG13Protocol.Usage;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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

    private static string DomainSeparatedHash(string domain, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(domain + "\0" + value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static nint CreateLogicalSessionHandle(string fingerprint)
    {
        var bytes = Convert.FromHexString(fingerprint);
        if (IntPtr.Size == sizeof(long))
        {
            var value = BitConverter.ToInt64(bytes, 0) | long.MinValue;
            return new nint(value == -1 ? long.MinValue + 1 : value);
        }

        var compact = BitConverter.ToInt32(bytes, 0) | int.MinValue;
        return new nint(compact == -1 ? int.MinValue + 1 : compact);
    }
}

internal sealed record RawLogitechG13DeviceCandidate(
    nint SessionHandle,
    string RawDevicePath,
    Guid? ContainerId);
