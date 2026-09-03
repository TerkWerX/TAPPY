namespace Tappy.App.Runtime;

public sealed record ControllerChoice(
    string SessionId,
    string PersistentId,
    string DisplayName,
    string IdentityConfidence,
    string ProviderId = "raw-input");

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
    bool IsIdentificationCaptureActive = false);

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

    Task InitializeAsync(CancellationToken cancellationToken = default);
    void RefreshDevices();
    RuntimeOperation BeginIdentification(ControllerChoice device);
    RuntimeOperation ConfirmController();
    RuntimeOperation AssignMapping(string controlId, string outputKey);
    Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default);
    RuntimeOperation EmergencyStop(string reason);
}
