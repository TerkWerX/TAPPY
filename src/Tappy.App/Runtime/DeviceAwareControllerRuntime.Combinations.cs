using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Runtime;

public sealed partial class DeviceAwareControllerRuntime
{
    private static bool CombinationRequiresRunOnce(ControllerProfile controller, ControlId id) =>
        controller.Combinations.Any(c => c.ControlId == id || c.Members.Contains(id));

    public RuntimeOperation CreateCombination(ControllerBankEndpoint expected, IReadOnlyList<string> members)
    {
        RuntimeControlUpdate[] updates;
        string name;
        lock (_gate)
        {
            if (!expected.SameConnection(GetBankEndpoint())) { return RuntimeOperation.Failed("The source controller changed. Select its buttons again."); }
            if (_engine.InputStates.GetPressedControls(new ControllerSessionId(expected.SessionId)).Count != 0)
            { return RuntimeOperation.Failed("Release every source button before creating a combination."); }
            var controller = FindEditableController(expected.PersistentId)!;
            var ids = members.Select(AssignmentReportLocked).Where(id => id is not null).Select(id => new ControlId(id!)).Distinct().ToArray();
            if (ids.Length != members.Count || ids.Length is < 2 or > 8 || ids.Any(id => id.Value.StartsWith("combination:", StringComparison.Ordinal) ||
                !controller.Layout.Rows.SelectMany(r => r.Controls).Any(c => c.ControlId == id)))
            { return RuntimeOperation.Failed("Check 2–8 different assignable physical buttons on this controller. Combinations cannot contain other combination tiles."); }
            if (controller.Combinations.Count >= 128 && !controller.Combinations.Any(c => c.ControlId == ControlCombination.IdFor(ids)))
            { return RuntimeOperation.Failed("This controller already has 128 combinations."); }
            foreach (var layer in controller.Layers)
            {
                if (layer.Bindings.Any(b => ids.Contains(b.ControlId) && ControlCombination.RequiresHold(b) && !controller.BankControls.Any(c => c.ControlId == b.ControlId)))
                { return RuntimeOperation.Failed("A checked button has a held/repeating assignment. Change it to Run once before adding a combination. Nothing was overwritten."); }
            }
            name = "Combo: " + string.Join(" + ", ids.Select(id => ResolveDisplayName(controller, id, id.Value)));
            var combination = new ControlCombination { ControlId = ControlCombination.IdFor(ids), Name = name, Members = ids.ToList() };
            if (!controller.Combinations.Any(c => c.ControlId == combination.ControlId)) { controller.Combinations.Add(combination); }
            EnsureCombinationTiles(controller);
            foreach (var combo in controller.Combinations) { _observedControls.Add(combo.ControlId); _controlLabels[combo.ControlId] = combo.Name; }
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            { _isRehearsal = true; _engine.SetRehearsalMode(true); return RuntimeOperation.Failed("The combination was created, but output cleanup needs attention. Restart Tappy before enabling output."); }
            _engine.ConnectController(controller.Identity);
            updates = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }
        PublishReleasedUpdates(updates); RaiseState(mappingStatus: name + " ready. Select its tile to assign a macro or bank control.");
        return RuntimeOperation.Ok(name + " ready. Assign its new tile; individual assignments remain saved.");
    }

    public RuntimeOperation RemoveCombination(ControllerAssignmentTarget target)
    {
        RuntimeControlUpdate[] updates;
        lock (_gate)
        {
            if (GetBankEndpoint() is null || !IsCurrentAssignmentTargetLocked(target)) { return RuntimeOperation.Failed("The source changed. Reselect the combination."); }
            if (_engine.InputStates.GetPressedControls(new ControllerSessionId(target.ControllerSessionId)).Count != 0)
            { return RuntimeOperation.Failed("Release every source button before removing a combination."); }
            var controller = FindEditableController(target.ControllerPersistentId)!;
            var id = new ControlId(target.ControlId);
            if (controller.Combinations.RemoveAll(c => c.ControlId == id) == 0) { return RuntimeOperation.Failed("Select a combination tile, not a physical button."); }
            controller.BankControls.RemoveAll(c => c.ControlId == id);
            foreach (var layer in controller.Layers) { layer.Bindings.RemoveAll(b => b.ControlId == id); }
            foreach (var row in controller.Layout.Rows) { row.Controls.RemoveAll(c => c.ControlId == id); }
            _observedControls.Remove(id); _controlLabels.Remove(id);
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            { _isRehearsal = true; _engine.SetRehearsalMode(true); return RuntimeOperation.Failed("Combination removed, but output cleanup needs attention. Restart Tappy before enabling output."); }
            _engine.ConnectController(controller.Identity);
            updates = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }
        PublishReleasedUpdates(updates); RaiseState();
        return RuntimeOperation.Ok("Combination and its assignments removed. Physical buttons and their original assignments were kept.");
    }

    private static void EnsureCombinationTiles(ControllerProfile controller)
    {
        var layout = controller.Layout;
        foreach (var combination in controller.Combinations)
        {
            if (layout.Rows.SelectMany(r => r.Controls).Any(c => c.ControlId == combination.ControlId)) { continue; }
            var controls = layout.Rows.SelectMany((r, row) => r.Controls.Select((c, column) => (Control: c, Row: row, Column: column))).ToArray();
            var physical = controls.Where(c => c.Control.ControlId is not { } id || !controller.Combinations.Any(combo => combo.ControlId == id)).ToArray();
            var right = physical.Select(c => (c.Control.X ?? c.Column * 144) + c.Control.Width * 144).DefaultIfEmpty(0).Max();
            var comboCount = controls.Count(c => c.Control.ControlId is { } id && controller.Combinations.Any(combo => combo.ControlId == id));
            layout.Rows.Add(new LayoutRowDefinition
            {
                Id = combination.ControlId.Value,
                Controls =
                [new LayoutControlDefinition { ControlId = combination.ControlId, Label = combination.Name, Kind = LayoutControlKind.Button,
                    X = right + 24, Y = comboCount * 116, Width = 1.8, Height = 1, Cluster = "Combinations" }]
            });
        }
    }
}
