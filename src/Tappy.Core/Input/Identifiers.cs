using System.Globalization;

namespace Tappy.Core.Input;

public readonly record struct ControllerSessionId
{
    public ControllerSessionId(string value)
    {
        Value = Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A controller session id is required.", parameterName)
            : value.Trim();
}

public readonly record struct ControllerPersistentId
{
    public ControllerPersistentId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A persistent controller id is required.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}

public enum ControllerIdentityConfidence
{
    SerialExact,
    PortBound,
    Ambiguous,
    SessionOnly
}

public sealed record ControllerIdentity
{
    public ControllerIdentity(
        ControllerSessionId sessionId,
        ControllerPersistentId? persistentId,
        ControllerIdentityConfidence confidence,
        string displayName,
        string providerId = "raw-input",
        ushort? vendorId = null,
        ushort? productId = null,
        ushort usagePage = 0x0001,
        ushort usage = 0x0006)
    {
        if (sessionId.IsEmpty)
        {
            throw new ArgumentException("A controller session id is required.", nameof(sessionId));
        }

        if (confidence is ControllerIdentityConfidence.SerialExact or ControllerIdentityConfidence.PortBound &&
            persistentId is null)
        {
            throw new ArgumentException("Exact and port-bound identities require a persistent id.", nameof(persistentId));
        }

        SessionId = sessionId;
        PersistentId = persistentId;
        Confidence = confidence;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Controller" : displayName.Trim();
        ProviderId = string.IsNullOrWhiteSpace(providerId)
            ? throw new ArgumentException("An input provider id is required.", nameof(providerId))
            : providerId.Trim().ToLowerInvariant();
        VendorId = vendorId;
        ProductId = productId;
        UsagePage = usagePage;
        Usage = usage;
    }

    public ControllerSessionId SessionId { get; init; }
    public ControllerPersistentId? PersistentId { get; init; }
    public ControllerIdentityConfidence Confidence { get; init; }
    public string DisplayName { get; init; }
    public string ProviderId { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }

    public ControllerIdentity WithSession(ControllerSessionId sessionId) => this with { SessionId = sessionId };
}

public readonly record struct ControlId
{
    public ControlId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A control id is required.", nameof(value))
            : value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static ControlId Create(string providerId, string physicalIdentity)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("An input provider id is required.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(physicalIdentity))
        {
            throw new ArgumentException("A physical control identity is required.", nameof(physicalIdentity));
        }

        return new ControlId($"{providerId.Trim()}:{physicalIdentity.Trim()}");
    }

    public static ControlId FromRawInputKeyboard(
        ushort scanCode,
        bool isE0 = false,
        bool isE1 = false,
        ushort usagePage = 0x0007,
        ushort? usage = null)
    {
        if (scanCode == 0 && usage is null)
        {
            throw new ArgumentException("A scan code or HID usage is required.", nameof(scanCode));
        }

        if (isE0 && isE1)
        {
            throw new ArgumentException("A Raw Input key cannot be both E0 and E1 extended.");
        }

        var extension = isE0 ? "e0" : isE1 ? "e1" : "base";
        var usageValue = usage.HasValue
            ? usage.Value.ToString("x4", CultureInfo.InvariantCulture)
            : "none";
        var physical = string.Create(CultureInfo.InvariantCulture,
            $"keyboard:up{usagePage:x4}:u{usageValue}:sc{scanCode:x4}:{extension}");
        return Create("raw-input", physical);
    }

    public override string ToString() => Value ?? string.Empty;
}
