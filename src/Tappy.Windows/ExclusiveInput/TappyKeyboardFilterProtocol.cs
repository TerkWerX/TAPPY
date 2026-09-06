namespace Tappy.Windows.ExclusiveInput;

public enum KeyboardFilterMode : uint
{
    PassThrough = 0,
    CaptureAndSuppress = 1
}

public enum KeyboardFilterRole : uint
{
    Unassigned = 0,
    ProtectedPrimary = 1,
    ConfigurableSecondary = 2
}

[Flags]
public enum KeyboardFilterEventFlags : ushort
{
    None = 0,
    Break = 1 << 0,
    E0 = 1 << 1,
    E1 = 1 << 2
}

public sealed record KeyboardFilterInstanceStatus(
    uint ProtocolVersion,
    KeyboardFilterMode EffectiveMode,
    KeyboardFilterRole Role,
    ulong PolicyGeneration,
    bool WatchdogArmed,
    uint WatchdogTimeoutMilliseconds,
    ulong LastSequence,
    ulong RingOverflowCount);

public sealed record KeyboardFilterEvent(
    ulong Sequence,
    ulong PolicyGeneration,
    ushort MakeCode,
    KeyboardFilterEventFlags Flags,
    ushort UnitId);

/// <summary>
/// Versioned user-mode/kernel contract for the optional Tappy keyboard filter.
/// The values are frozen before any driver binary is produced so the broker,
/// driver, and deterministic tests cannot silently drift.
/// </summary>
public static class TappyKeyboardFilterProtocol
{
    public const uint Version = 1;
    public const int MaximumEventsPerRead = 256;
    public const uint MinimumWatchdogMilliseconds = 250;
    public const uint MaximumWatchdogMilliseconds = 2_000;
    public const int SessionRequestSize = 24;
    public const int PolicyRequestSize = 48;
    public const int HeartbeatRequestSize = 32;
    public const int ReadRequestSize = 32;
    public const int StatusSize = 48;
    public const int EventSize = 24;

    public static readonly Guid DeviceInterfaceClassId =
        new("29381853-4B50-4A46-9AF6-7A44BFD8336B");

    // 0x8000-0xFFFF is Microsoft's vendor-defined device-type range. Functions
    // 0x800-0xFFF are vendor-defined. Every mutating request requires a handle
    // opened for both read and write and uses METHOD_BUFFERED.
    private const uint DeviceType = 0x8000;
    private const uint MethodBuffered = 0;
    private const uint ReadWriteAccess = 0x0003;

    public static readonly uint QueryStatus = ControlCode(0x800);
    public static readonly uint BeginAuthenticatedSession = ControlCode(0x801);
    public static readonly uint SetInstancePolicy = ControlCode(0x802);
    public static readonly uint Heartbeat = ControlCode(0x803);
    public static readonly uint ReadEvents = ControlCode(0x804);
    public static readonly uint ForceFailOpen = ControlCode(0x805);

    public static bool IsSupportedVersion(uint version) => version == Version;

    public static bool IsValidWatchdog(uint timeoutMilliseconds) =>
        timeoutMilliseconds is >= MinimumWatchdogMilliseconds and <= MaximumWatchdogMilliseconds;

    public static bool IsValidEventBatch(IReadOnlyCollection<KeyboardFilterEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count > MaximumEventsPerRead)
        {
            return false;
        }

        ulong previousSequence = 0;
        var first = true;
        foreach (var item in events)
        {
            const KeyboardFilterEventFlags knownFlags =
                KeyboardFilterEventFlags.Break |
                KeyboardFilterEventFlags.E0 |
                KeyboardFilterEventFlags.E1;
            if (item.MakeCode == 0 ||
                (item.Flags & ~knownFlags) != 0 ||
                (!first && item.Sequence <= previousSequence))
            {
                return false;
            }

            first = false;
            previousSequence = item.Sequence;
        }

        return true;
    }

    private static uint ControlCode(uint function) =>
        (DeviceType << 16) | (ReadWriteAccess << 14) | (function << 2) | MethodBuffered;
}
