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

public sealed record ControllerChoice(
    string SessionId,
    string PersistentId,
    string DisplayName,
    string IdentityConfidence,
    string ProviderId = "raw-input",
    ushort? VendorId = null,
    ushort? ProductId = null);

public sealed record RuntimeControlUpdate(
    string ControllerPersistentId,
    string ControlId,
    string DisplayLabel,
    bool IsPressed,
    bool IsRepeat,
    string AssignedAction,
    int SimultaneousCount,
    long AggregateEventCount,
    bool IsSnapshot = false);

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
    ushort? ActiveControllerProductId = null);

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

    IReadOnlyList<ControllerChoice> Devices { get; }
    bool IsRehearsal { get; set; }
    bool CanConfirmController { get; }
    bool IsIdentificationCaptureActive { get; }
    bool IsOutputStateConfirmedSafe { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    void RefreshDevices();
    RuntimeOperation BeginIdentification(ControllerChoice device);
    RuntimeOperation ConfirmController();
    RuntimeOperation AssignMapping(string controlId, string outputKey);
    RuntimeOperation AssignKeyboardMapping(string controlId, KeyboardMappingAssignment assignment);
    RuntimeOperation AssignControllerAction(string controlId, ControllerActionAssignment assignment) =>
        RuntimeOperation.Failed("This runtime does not support multi-action controller assignments.");
    Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default);
    RuntimeOperation EmergencyStop(string reason);
}
