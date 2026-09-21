using Tappy.App.Runtime;
using Tappy.Core.Models;

namespace Tappy.App.ViewModels;

public sealed partial class MainViewModel
{
    private ControllerBankEndpoint? _bankEndpoint;
    private bool _bankChangeBusy;
    public IReadOnlyList<ControllerBankChoice> BankChoices => _bankEndpoint?.Banks ?? [];
    public string? SelectedBankId => _bankEndpoint?.ActiveLayerId;
    public bool CanChangeBank => _bankEndpoint is not null && !_bankChangeBusy && !_tartarusLedBusy;
    public ControllerBankEndpoint? GetBankEndpoint() => _runtime.GetBankEndpoint();
    public BankControlBinding? GetSelectedBankControl() => _selectedControl is null ? null : _runtime.GetBankControl(_selectedControl.ControlId);

    public async Task<RuntimeOperation> SaveBankControlAsync(ControllerAssignmentTarget source, BankControlBinding? binding)
    {
        var result = _runtime.SetBankControl(source, binding);
        MappingStatus = result.Message;
        if (result.Succeeded)
        {
            var saved = await _runtime.SaveProfileAsync().ConfigureAwait(true);
            if (!saved.Succeeded) { result = saved; }
            MappingStatus = result.Message;
        }
        return result;
    }

    public async Task<RuntimeOperation> ChangeBankAsync(ControllerBankEndpoint expected,
        BankControlOperation operation, string layerId = "", string alternateLayerId = "")
    {
        if (_isDisposed || _bankChangeBusy || _tartarusLedBusy)
        { return RuntimeOperation.Failed("The target is busy. Try the bank control again shortly."); }
        _bankChangeBusy = true; Raise(nameof(CanChangeBank));
        try
        {
            var result = _runtime.ChangeBank(expected, operation, layerId, alternateLayerId);
            _bankEndpoint = _runtime.GetBankEndpoint();
            Raise(nameof(SelectedBankId));
            MappingStatus = result.Message;
            if (!result.Succeeded) { return result; }
            // Use the target's own explicit lighting opt-in, never the source's.
            if (HasTartarusKeymaps && TartarusFollowKeymaps)
            { await SyncTartarusIndicatorsAsync().ConfigureAwait(true); }
            var saved = await _runtime.SaveProfileAsync().ConfigureAwait(true);
            if (!saved.Succeeded) { MappingStatus = saved.Message; return saved; }
            MappingStatus = result.Message + " Saved." + (_tartarusLedError is null ? "" : " LED sync needs attention: " + _tartarusLedError);
            return RuntimeOperation.Ok(MappingStatus);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MappingStatus = "The bank may have changed, but its profile could not be saved. Use Save profile to retry. " + ex.Message;
            return RuntimeOperation.Failed(MappingStatus);
        }
        finally { _bankChangeBusy = false; Raise(nameof(CanChangeBank)); }
    }
}
