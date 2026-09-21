using Tappy.App.Runtime;

namespace Tappy.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool SelectedIsCombination => _selectedControl?.ControlId.StartsWith("combination:", StringComparison.Ordinal) == true;
    public string[] CheckedControlIds() => Controls.Where(c => c.IsGroupSelected).Select(c => c.ControlId).ToArray();

    public async Task<RuntimeOperation> CreateCombinationAsync(ControllerBankEndpoint source, IReadOnlyList<string> members)
    {
        var result = _runtime.CreateCombination(source, members);
        if (result.Succeeded)
        {
            ClearGroupSelection();
            var saved = await _runtime.SaveProfileAsync().ConfigureAwait(true);
            if (!saved.Succeeded) { result = saved; }
        }
        MappingStatus = result.Message;
        return result;
    }

    public async Task<RuntimeOperation> RemoveCombinationAsync(ControllerAssignmentTarget target)
    {
        var result = _runtime.RemoveCombination(target);
        if (result.Succeeded)
        {
            if (_tiles.Remove(target.ControlId, out var tile))
            {
                Controls.Remove(tile);
                if (ReferenceEquals(_selectedControl, tile)) { _selectedControl = null; }
            }
            _illuminationStates.Remove(target.ControlId);
            _layoutPlacements.Remove(target.ControlId);
            Raise(nameof(SelectedIsCombination)); Raise(nameof(SelectedControlLabel));
            Raise(nameof(CanAssignSelectedControl)); Raise(nameof(AssignmentEditorButtonLabel));
            Raise(nameof(GroupSelectedCount)); Raise(nameof(GroupSelectionLabel));
            RecalculateLayoutSurface();
            var saved = await _runtime.SaveProfileAsync().ConfigureAwait(true);
            if (!saved.Succeeded) { result = saved; }
        }
        MappingStatus = result.Message;
        return result;
    }
}
