namespace Tappy.Windows.Input;

public enum RawInputCapability
{
    Keyboard,
    LogitechG13,
}

/// <summary>
/// Reports failure of one independently registered Raw Input capability without
/// implying that every capability owned by the shared native host has failed.
/// </summary>
public sealed class RawInputCapabilityException : Exception
{
    public RawInputCapabilityException(
        RawInputCapability capability,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Capability = capability;
    }

    public RawInputCapability Capability { get; }
}
