using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tappy.Core.Input;

namespace Tappy.Windows.Input;

public enum RawInputDeviceKind
{
    Keyboard,
    Hid,
    Mouse,
    Unknown,
}

public enum PhysicalDeviceGrouping
{
    /// <summary>
    /// Windows explicitly associated the interfaces through
    /// DEVPKEY_Device_ContainerId.
    /// </summary>
    WindowsContainerId,

    /// <summary>
    /// No authoritative physical-container identity was available. The Raw Input
    /// interface remains a separate choice instead of being heuristically merged.
    /// </summary>
    RawInputInterfaceFallback,
}

/// <summary>
/// Public device information that intentionally cannot reveal a Windows raw device
/// path. The handle is session-only; persistence uses the SHA-256 based identity.
/// </summary>
public sealed record SanitizedDeviceDescriptor(
    [property: JsonIgnore]
    nint SessionHandle,
    string SessionId,
    string PersistentId,
    string PathFingerprintSha256,
    RawInputDeviceKind Kind,
    ushort? VendorId,
    ushort? ProductId,
    ushort? UsagePage,
    ushort? Usage,
    string DisplayName)
{
    /// <summary>
    /// Raw Input handles belonging to this one logical physical controller. Handles
    /// are process/session-local and are intentionally excluded from serialization.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<nint> MemberSessionHandles { get; init; } = [SessionHandle];

    public PhysicalDeviceGrouping Grouping { get; init; } =
        PhysicalDeviceGrouping.RawInputInterfaceFallback;

    public int InterfaceCount => MemberSessionHandles.Count;

    internal bool ContainsSessionHandle(nint handle) =>
        MemberSessionHandles.Contains(handle);

    public ControllerIdentity ToCoreIdentity(
        ControllerIdentityConfidence confidence = ControllerIdentityConfidence.PortBound) =>
        new(
            new ControllerSessionId(SessionId),
            new ControllerPersistentId(PersistentId),
            confidence,
            DisplayName,
            providerId: "raw-input",
            vendorId: VendorId,
            productId: ProductId,
            usagePage: UsagePage ?? 0x0001,
            usage: Usage ?? 0x0006);
}

public static partial class DeviceDescriptorSanitizer
{
    public static SanitizedDeviceDescriptor CreateKeyboard(
        nint sessionHandle,
        string rawDevicePath,
        ushort usagePage = 0x0001,
        ushort usage = 0x0006)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDevicePath);

        var normalizedPath = rawDevicePath.Trim().ToUpperInvariant();
        var fingerprint = DomainSeparatedHash("Tappy.RawInput.Interface.v1", normalizedPath);
        var vendorId = ParseHexComponent(VendorIdRegex(), normalizedPath);
        var productId = ParseHexComponent(ProductIdRegex(), normalizedPath);
        var modelPart = vendorId is not null && productId is not null
            ? $"vid-{vendorId.Value:X4}:pid-{productId.Value:X4}"
            : "vid-unknown:pid-unknown";
        var displayName = vendorId is not null && productId is not null
            ? $"Keyboard VID {vendorId.Value:X4} PID {productId.Value:X4}"
            : $"Keyboard {fingerprint[..8]}";

        return new SanitizedDeviceDescriptor(
            sessionHandle,
            $"raw-keyboard-session-{unchecked((nuint)sessionHandle):X}",
            $"raw-keyboard:{modelPart}:sha256-{fingerprint}",
            fingerprint,
            RawInputDeviceKind.Keyboard,
            vendorId,
            productId,
            usagePage,
            usage,
            displayName)
        {
            MemberSessionHandles = [sessionHandle],
            Grouping = PhysicalDeviceGrouping.RawInputInterfaceFallback,
        };
    }

    internal static SanitizedDeviceDescriptor CreateKeyboardGroup(
        Guid containerId,
        IReadOnlyList<RawKeyboardDeviceCandidate> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (containerId == Guid.Empty)
        {
            throw new ArgumentException("A Windows device container ID cannot be empty.", nameof(containerId));
        }

        if (members.Count == 0)
        {
            throw new ArgumentException("A physical keyboard group needs at least one interface.", nameof(members));
        }

        var normalizedContainer = containerId.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();
        var fingerprint = DomainSeparatedHash("Tappy.RawInput.Container.v1", normalizedContainer);
        var sessionHandle = CreateLogicalSessionHandle(fingerprint);
        var vendorId = ConsensusValue(members.Select(member => member.VendorId));
        var productId = ConsensusValue(members.Select(member => member.ProductId));
        var modelPart = vendorId is not null && productId is not null
            ? $"vid-{vendorId.Value:X4}:pid-{productId.Value:X4}"
            : "vid-unknown:pid-unknown";
        var displayName = vendorId is not null && productId is not null
            ? $"Keyboard VID {vendorId.Value:X4} PID {productId.Value:X4}"
            : $"Keyboard {fingerprint[..8]}";

        return new SanitizedDeviceDescriptor(
            sessionHandle,
            $"raw-keyboard-container-session-{fingerprint[..16]}",
            $"raw-keyboard:{modelPart}:container-sha256-{fingerprint}",
            fingerprint,
            RawInputDeviceKind.Keyboard,
            vendorId,
            productId,
            0x0001,
            0x0006,
            displayName)
        {
            MemberSessionHandles = members
                .Select(member => member.SessionHandle)
                .Distinct()
                .OrderBy(handle => unchecked((nuint)handle))
                .ToArray(),
            Grouping = PhysicalDeviceGrouping.WindowsContainerId,
        };
    }

    private static ushort? ConsensusValue(IEnumerable<ushort?> values)
    {
        var distinct = values.Where(value => value is not null).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
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

    private static ushort? ParseHexComponent(Regex regex, string value)
    {
        var match = regex.Match(value);
        return match.Success && ushort.TryParse(
            match.Groups[1].Value,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    [GeneratedRegex(@"(?:^|[#&\\])VID_([0-9A-F]{4})(?:[&#\\]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VendorIdRegex();

    [GeneratedRegex(@"(?:^|[#&\\])PID_([0-9A-F]{4})(?:[&#\\]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdRegex();
}

internal sealed record RawKeyboardDeviceCandidate(
    nint SessionHandle,
    string RawDevicePath,
    ushort? VendorId,
    ushort? ProductId,
    Guid? ContainerId);
