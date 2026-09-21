using Tappy.App.Services;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows.Output;

namespace Tappy.App.Runtime;

public sealed partial class DeviceAwareControllerRuntime
{
    public event EventHandler<ControllerBankRequest>? BankControlRequested;
    private string _bankConnectionToken = Guid.NewGuid().ToString("N");

    public ControllerBankEndpoint? GetBankEndpoint()
    {
        lock (_gate)
        {
            if (_disposed || _confirmed is null || _candidate is not null ||
                _isRememberedReconnect || _suspended || _sessionLocked || _outputSafetyNeedsAttention ||
                IsProtectedKeyboard(_confirmed) || FindEditableController(_confirmed.PersistentId) is not { } controller)
            { return null; }
            var tartarus = RazerTartarusLayout.Matches(_confirmed.ProviderId, _confirmed.VendorId, _confirmed.ProductId);
            string[] colors = ["Yellow", "Green", "Blue", "Yellow + green", "Yellow + blue", "Green + blue", "All three", "All off"];
            var banks = controller.Layers.Select((layer, i) => new ControllerBankChoice(layer.Id,
                tartarus && i < colors.Length ? $"{i + 1} · {colors[i]}" : $"{i + 1} · {layer.Name}")).ToArray();
            var choice = ToChoice(_confirmed);
            return new ControllerBankEndpoint(choice.SessionId, choice.PersistentId, choice.ProviderId,
                _confirmationGeneration, $"{choice.DisplayName} · ID {choice.DeviceTag}" +
                    (_confirmed.IsAmbiguous ? " · reselect after reconnect" : ""), controller.ActiveLayerId, banks,
                _bankConnectionToken, _confirmed.IsAmbiguous);
        }
    }

    public BankControlBinding? GetBankControl(string controlId)
    {
        lock (_gate)
        {
            var report = AssignmentReportLocked(controlId);
            return _confirmed is null ? null : FindEditableController(_confirmed.PersistentId)?.BankControls
                .FirstOrDefault(control => control.ControlId.Value == report);
        }
    }

    public RuntimeOperation SetBankControl(ControllerAssignmentTarget source, BankControlBinding? binding)
    {
        RuntimeControlUpdate[] updates;
        lock (_gate)
        {
            if (GetBankEndpoint() is null || !IsCurrentAssignmentTargetLocked(source))
            { return RuntimeOperation.Failed("The source controller or bank changed. Nothing was changed; reopen Bank control."); }
            if (_engine.InputStates.GetPressedControls(new ControllerSessionId(source.ControllerSessionId)).Count != 0)
            { return RuntimeOperation.Failed("Release the source controller's controls before saving a bank control."); }
            if (binding is not null)
            {
                try { binding.Validate(); }
                catch (InvalidDataException ex) { return RuntimeOperation.Failed(ex.Message); }
                if (binding.ControlId.Value != source.ControlId)
                { return RuntimeOperation.Failed("The bank control does not match the selected physical key."); }
            }
            var controller = FindEditableController(source.ControllerPersistentId)!;
            controller.BankControls.RemoveAll(control => control.ControlId.Value == source.ControlId);
            if (binding is not null) { controller.BankControls.Add(binding); }
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            {
                _isRehearsal = true; _engine.SetRehearsalMode(true);
                return RuntimeOperation.Failed("Bank control updated, but output cleanup needs attention. Restart Tappy before using output.");
            }
            _engine.ConnectController(controller.Identity);
            updates = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }
        PublishReleasedUpdates(updates);
        var message = binding is null ? "Bank control removed. The original per-bank assignments are restored." :
            "Bank control set for every source bank. Original assignments are retained underneath.";
        RaiseState(mappingStatus: message);
        return RuntimeOperation.Ok(message);
    }

    public RuntimeOperation ChangeBank(ControllerBankEndpoint expected, BankControlOperation operation, string layerId = "", string alternateLayerId = "")
    {
        RuntimeControlUpdate[] updates;
        string label;
        lock (_gate)
        {
            var current = GetBankEndpoint();
            if (!expected.SameConnection(current))
            { return RuntimeOperation.Failed("The target controller is unavailable or changed. No other device was used."); }
            if (_tartarusLedSyncPending)
            { return RuntimeOperation.Failed("The target's LEDs are still synchronizing. Try the bank button again shortly."); }
            var session = new ControllerSessionId(current!.SessionId);
            if (_engine.InputStates.GetPressedControls(session).Count != 0)
            { return RuntimeOperation.Failed("Release the target controller's controls, then try the bank button again."); }
            var controller = FindEditableController(current.PersistentId)!;
            if (operation == BankControlOperation.Toggle && (alternateLayerId == layerId ||
                !controller.Layers.Any(l => l.Id == layerId) || !controller.Layers.Any(l => l.Id == alternateLayerId)))
            { return RuntimeOperation.Failed("Choose two different banks that exist on the target."); }
            var index = controller.Layers.FindIndex(layer => layer.Id == controller.ActiveLayerId);
            index = operation switch
            {
                BankControlOperation.Next => (index + 1) % controller.Layers.Count,
                BankControlOperation.Previous => (index + controller.Layers.Count - 1) % controller.Layers.Count,
                BankControlOperation.Select => controller.Layers.FindIndex(layer => layer.Id == layerId),
                BankControlOperation.Toggle => controller.Layers.FindIndex(layer => layer.Id ==
                    (controller.ActiveLayerId == layerId ? alternateLayerId : layerId)),
                _ => -1
            };
            if (index < 0) { return RuntimeOperation.Failed("That bank does not exist on this target. No bank was changed."); }
            var layer = controller.Layers[index];
            if (!_engine.SetActiveLayer(session, layer.Id)) { return RuntimeOperation.Failed("The target session is no longer connected."); }
            controller.ActiveLayerId = layer.Id;
            SyncFireBankIndicatorLocked(_confirmed!, index);
            _tartarusKeymapRevision++;
            _tartarusVerifiedLedMask = null;
            updates = CreateControlUpdatesLocked(controller, isSnapshot: true);
            label = current.Banks[index].Label;
        }
        PublishReleasedUpdates(updates);
        RaiseState(mappingStatus: $"Bank {label} selected. Other banks and controllers are unchanged.");
        return RuntimeOperation.Ok($"{expected.DisplayName}: bank {label} selected.");
    }

    /// <summary>
    /// Lights the Fire's own Modes/Bank indicator LED (Channel Rack / Mixer /
    /// User 1 / User 2 — see docs/AKAI_FIRE.md) to match Tappy's own active
    /// bank, so a physical button mapped to "next bank" visibly agrees with
    /// the controller's own hardware indicator instead of only changing
    /// Tappy's internal state. CC 0x1B, channel 1, value = the 0-based bank
    /// index — the "plain index, 16 = all off" reading from AKAI_FIRE.md's
    /// two candidate encodings, tried first since it is not yet physically
    /// confirmed against real hardware; the value is not yet range-checked
    /// against a verified 4-bank maximum beyond bankIndex naturally being
    /// 0-3 for a Fire (its own default layer count). Best-effort: a failed
    /// indicator sync must never block the bank change itself, matching
    /// every other Fire lighting sync's fail-soft precedent.
    /// </summary>
    private void SyncFireBankIndicatorLocked(RuntimeInputDevice confirmed, int bankIndex)
    {
        if (_controllerLedOutput is null ||
            MidiControllerModelCatalog.Find(confirmed.ProviderId, confirmed.DisplayName)?.Id != MidiControllerModelCatalog.AkaiFireV1Id)
        {
            return;
        }

        var fireOutputs = MatchMidiOutputPorts(WinMmMidiOutput.GetDevices(false), confirmed.DisplayName);
        if (fireOutputs.Length != 1)
        {
            return;
        }

        try
        {
            _controllerLedOutput.Send(fireOutputs[0].Name,
                MidiMessageParser.Create(MidiShortMessageKind.ControlChange, channel: 1, data1: 0x1B, data2: bankIndex));
        }
        catch
        {
            // Best-effort: see the XML doc comment above.
        }
    }
}
