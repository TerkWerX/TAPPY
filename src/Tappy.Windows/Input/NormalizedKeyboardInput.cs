using Tappy.Core.Input;

namespace Tappy.Windows.Input;

public enum KeyTransition
{
    Press,
    Release,
}

public enum ExtendedKeyKind
{
    None,
    E0,
    E1,
}

public sealed record VirtualKeyMetadata(
    ushort VirtualKey,
    string Name,
    string DisplayName,
    bool IsModifier,
    bool IsNumpadKey);

/// <summary>
/// Device-aware event published only after the device has been selected and its
/// persistent identity explicitly confirmed.
/// </summary>
public sealed record NormalizedKeyboardInput(
    nint SessionDeviceHandle,
    ControllerSessionId ControllerSessionId,
    string PersistentDeviceId,
    ControlId ControlId,
    ushort MakeCode,
    ushort RawVirtualKey,
    ExtendedKeyKind ExtendedKey,
    KeyTransition Transition,
    bool IsRepeat,
    VirtualKeyMetadata Key,
    uint NativeMessage,
    uint ExtraInformation,
    ControlSignal Signal);
