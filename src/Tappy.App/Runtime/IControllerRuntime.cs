using Tappy.Core.Models;

namespace Tappy.App.Runtime;

public sealed record KeyboardMappingAssignment(
    string Name,
    KeyboardActionMode PressMode,
    IReadOnlyList<string> PressKeys,
    KeyboardActionMode ReleaseMode,
    IReadOnlyList<string> ReleaseKeys)
{
    public static KeyboardMappingAssignment PressOnce(string name, IReadOnlyList<string> keys) =>
        new(name, KeyboardActionMode.Tap, keys, KeyboardActionMode.None, []);

    public static KeyboardMappingAssignment HoldUntilRelease(string name, IReadOnlyList<string> keys) =>
        new(name, KeyboardActionMode.HoldUntilRelease, keys, KeyboardActionMode.None, []);

    public static KeyboardMappingAssignment ReleaseOnce(string name, IReadOnlyList<string> keys) =>
        new(name, KeyboardActionMode.None, [], KeyboardActionMode.Tap, keys);
}

public sealed record ControllerActionAssignment(
    string Name,
    ControllerActionSequenceDefinition PressSequence,
    ControllerActionSequenceDefinition ReleaseSequence);

public sealed record ControllerAssignmentTarget(
    string ControllerSessionId,
    string ControllerPersistentId,
    string ProviderId,
    string LayerId,
    string ControlId,
    long ConfirmationGeneration);

public sealed record ControllerAssignmentEditContext(
    ControllerAssignmentTarget Target,
    ControllerActionAssignment? ExistingAssignment);

public sealed record ControllerControlPlacement(
    string ControlId,
    int NaturalRow,
    int NaturalColumn,
    double? X,
    double? Y,
    double Width,
    double Height,
    string ColorKey = "Default",
    int AnalogRawAtMinimum = 0,
    int AnalogRawAtMaximum = 127,
    int? AnalogRawAtCenter = null,
    double EncoderDegreesPerStep = 15,
    bool EncoderReversed = false);

public sealed record ControllerLayoutWorkspace(
    int GridColumns,
    int GridRows,
    bool SnapToGrid,
    IReadOnlyList<ControllerControlPlacement> Controls,
    double Zoom = 1.0,
    double PhotoPaneWidth = 420,
    bool IsLayoutLocked = false);

public enum ControllerLightingTopology
{
    PerControl,
    Zoned,
    Global
}

public sealed record ControllerLedColorCapability(
    string ModelId,
    string DisplayName,
    IReadOnlyList<string> SupportedColorKeys,
    IReadOnlySet<string> ColorAddressableControlIds,
    ControllerLightingTopology Topology = ControllerLightingTopology.PerControl,
    bool SupportsFreeformColor = false);

public sealed record ControllerChoice(
    string SessionId,
    string PersistentId,
    string DisplayName,
    string IdentityConfidence,
    string ProviderId = "raw-input",
    ushort? VendorId = null,
    ushort? ProductId = null)
{
    public bool IsProtectedForTyping { get; init; }
    public string DeviceTag => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(ProviderId + "\0" + PersistentId)))[..6];

    public string Details
    {
        get
        {
            var hardware = VendorId is { } vendor && ProductId is { } product
                ? $" · Hardware ID {vendor:X4}:{product:X4}" : string.Empty;
            var explanation = ProviderId == "raw-input" && VendorId == 0x046D && ProductId == 0xC52B
                ? "Wireless receiver keyboard input. Paired models are shown when receiver discovery succeeds; a paired entry is not proof the keyboard is awake. Keyboards sharing this receiver remain one Tappy input source and share typing protection. "
                : DisplayName.Contains("infrared", StringComparison.OrdinalIgnoreCase)
                    ? "Infrared remote-control input reported by Windows, not another plugged-in USB keyboard. "
                    : "";
            var identity = IdentityConfidence == "Ambiguous"
                ? "This connection cannot reliably distinguish identical devices. "
                : "";
            return $"{(IsProtectedForTyping ? "Protected for typing — Tappy assignments are blocked. " : "")}{explanation}{identity}Device ID {DeviceTag}{hardware}. Use Identify and press/release a key on the intended device. The ID may change if you move USB ports.";
        }
    }
}

public sealed record RuntimeControlUpdate(
    string ControllerPersistentId,
    string ControlId,
    string DisplayLabel,
    bool IsPressed,
    bool IsRepeat,
    string AssignedAction,
    int SimultaneousCount,
    long AggregateEventCount,
    bool IsSnapshot = false,
    int? AnalogRawValue = null,
    double? AnalogDelta = null,
    bool CanAssign = true);

public sealed record RuntimeState(
    bool IsConfirmed,
    bool CanConfirm,
    string IdentificationStatus,
    string ActiveControllerLabel,
    string ActiveLayerName,
    string MappingStatus,
    string Status,
    string EffectiveSourceLabel = "Effective: Pass-through",
    bool IsIdentificationCaptureActive = false,
    string? ActiveControllerProviderId = null,
    ushort? ActiveControllerVendorId = null,
    ushort? ActiveControllerProductId = null,
    bool IsRememberedReconnect = false,
    bool IsLifecyclePaused = false,
    IReadOnlyList<RuntimeControlUpdate>? RetainedControls = null,
    string? InputBankLabel = null,
    bool? NumLockOn = null,
    bool IsWaitingForReconnect = false,
    string? ActiveControllerPresentationModelId = null,
    int? TartarusKeymap = null,
    int? TartarusVerifiedLedMask = null);

public sealed record RuntimeOperation(bool Succeeded, string Message)
{
    public static RuntimeOperation Ok(string message) => new(true, message);
    public static RuntimeOperation Failed(string message) => new(false, message);
}

public interface IControllerRuntime : IAsyncDisposable
{
    event EventHandler? DevicesChanged;
    event EventHandler<RuntimeControlUpdate>? ControlChanged;
    event EventHandler<RuntimeState>? StateChanged;
    event EventHandler<ControllerBankRequest>? BankControlRequested { add { } remove { } }
    ControllerBankEndpoint? GetBankEndpoint() => null;
    BankControlBinding? GetBankControl(string controlId) => null;
    RuntimeOperation SetBankControl(ControllerAssignmentTarget source, BankControlBinding? binding) =>
        RuntimeOperation.Failed("Bank controls are unavailable in this runtime.");
    RuntimeOperation ChangeBank(ControllerBankEndpoint expected, BankControlOperation operation, string layerId = "", string alternateLayerId = "") =>
        RuntimeOperation.Failed("Bank controls are unavailable in this runtime.");
    RuntimeOperation CreateCombination(ControllerBankEndpoint expected, IReadOnlyList<string> members) =>
        RuntimeOperation.Failed("Combinations are unavailable in this runtime.");
    RuntimeOperation RemoveCombination(ControllerAssignmentTarget target) =>
        RuntimeOperation.Failed("Combinations are unavailable in this runtime.");

    IReadOnlyList<ControllerChoice> Devices { get; }
    ControllerChoice? ReservedDevice => null;
    ControllerChoice? GetVerifiedTypingKeyboard() => null;
    void SetTypingKeyboardProtection(string? persistentId, bool blockAllKeyboards = false) { }
    bool IsRehearsal { get; set; }
    bool CanConfirmController { get; }
    bool IsIdentificationCaptureActive { get; }
    bool IsOutputStateConfirmedSafe { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    void RefreshDevices();
    void PollRememberedReconnect() { }
    RuntimeOperation BeginIdentification(ControllerChoice device);
    RuntimeOperation ConfirmController();
    RuntimeOperation AssignMapping(string controlId, string outputKey);
    RuntimeOperation AssignKeyboardMapping(string controlId, KeyboardMappingAssignment assignment);
    RuntimeOperation AssignControllerAction(string controlId, ControllerActionAssignment assignment) =>
        RuntimeOperation.Failed("This runtime does not support multi-action controller assignments.");
    ControllerActionAssignment? GetControllerAction(string controlId) => null;
    ControllerAssignmentEditContext? BeginAssignmentEdit(string controlId) => null;
    RuntimeOperation AssignKeyboardMapping(ControllerAssignmentTarget target, KeyboardMappingAssignment assignment) =>
        RuntimeOperation.Failed("This runtime cannot safely save an assignment to a captured controller target.");
    RuntimeOperation AssignControllerAction(ControllerAssignmentTarget target, ControllerActionAssignment assignment) =>
        RuntimeOperation.Failed("This runtime cannot safely save an assignment to a captured controller target.");
    RuntimeOperation ReorderControls(IReadOnlyList<string> orderedControlIds) =>
        RuntimeOperation.Failed("This runtime does not support custom controller layouts.");
    ControllerLayoutWorkspace? GetControllerLayout() => null;
    RuntimeOperation UpdateControllerLayout(ControllerLayoutWorkspace workspace) =>
        RuntimeOperation.Failed("This runtime does not support freeform controller layouts.");
    RuntimeOperation SyncControllerLedColors(IReadOnlyDictionary<string, string> colors) =>
        RuntimeOperation.Failed("This controller does not expose a verified hardware-lighting protocol to Tappy.");
    ControllerLedColorCapability? GetControllerLedColorCapability() => null;
    RuntimeOperation SendFireOledDiagnosticPattern() =>
        RuntimeOperation.Failed("This controller does not have a Tappy-supported OLED display.");
    RuntimeOperation SendFireOledFrame(bool[,] pixels) =>
        RuntimeOperation.Failed("This controller does not have a Tappy-supported OLED display.");
    RuntimeOperation SelectTartarusKeymap(int bank) => RuntimeOperation.Failed("This controller does not support Tartarus keymaps.");
    Task<RuntimeOperation> SyncTartarusIndicatorsAsync() => Task.FromResult(RuntimeOperation.Failed("Tartarus indicator output is unavailable."));
    Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default);
    RuntimeOperation EmergencyStop(string reason);
}
