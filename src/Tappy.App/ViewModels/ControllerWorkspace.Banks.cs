using System.Collections.Concurrent;
using Tappy.App.Runtime;
using Tappy.Core.Models;

namespace Tappy.App.ViewModels;

public sealed partial class ControllerWorkspace
{
    private readonly ConcurrentDictionary<IControllerRuntime, byte> _bankRuntimes = new();
    private readonly HashSet<IControllerRuntime> _busyBankTargets = [];
    private volatile bool _bankControlsPaused;
    public bool BankControlsPaused { get => _bankControlsPaused; set => _bankControlsPaused = value; }

    public IReadOnlyList<ControllerBankEndpoint> GetBankTargets() => Tabs.Select(tab => tab.Runtime.GetBankEndpoint())
        .OfType<ControllerBankEndpoint>().Where(endpoint => Tabs.Count(tab => tab.Runtime.GetBankEndpoint() is { } candidate &&
            candidate.ProviderId == endpoint.ProviderId && candidate.PersistentId == endpoint.PersistentId) == 1).ToArray();

    private void RemoveBankRouting(IControllerRuntime runtime)
    {
        runtime.BankControlRequested -= Runtime_BankControlRequested;
        _bankRuntimes.TryRemove(runtime, out _);
    }

    private void Runtime_BankControlRequested(object? sender, ControllerBankRequest request)
    {
        if (BankControlsPaused || sender is not IControllerRuntime source) { return; }
        // Capture the destination connection before dispatching to the UI. A later
        // reconnect must never receive a command emitted for the old connection.
        var matches = _bankRuntimes.Keys.Select(runtime => (Runtime: runtime, Endpoint: runtime.GetBankEndpoint()))
            .Where(item => item.Endpoint is { } endpoint && endpoint.ProviderId == request.Binding.TargetProviderId &&
                endpoint.PersistentId == request.Binding.TargetPersistentId &&
                (!endpoint.RequiresSessionBinding || endpoint.ConnectionToken == request.Binding.TargetConfirmationToken)).ToArray();
        var target = matches.Length == 1 ? matches[0] : default;
        _onUi(() => _ = RouteBankControlAsync(source, request, target.Runtime, target.Endpoint));
    }

    private async Task RouteBankControlAsync(IControllerRuntime source, ControllerBankRequest request,
        IControllerRuntime? target, ControllerBankEndpoint? endpoint)
    {
        var sourceTab = Tabs.FirstOrDefault(tab => tab.Runtime == source);
        if (BankControlsPaused || sourceTab is null || !request.Source.SameConnection(source.GetBankEndpoint()) ||
            source.GetBankControl(request.Binding.ControlId.Value) != request.Binding) { return; }
        if (IsIdentificationCaptureActive || !IsOutputStateConfirmedSafe)
        { sourceTab.ViewModel.ReportStatus("Bank switching paused while device setup or output cleanup needs attention."); return; }
        var targetTab = Tabs.FirstOrDefault(tab => tab.Runtime == target);
        if (target is null || endpoint is null || targetTab is null || !endpoint.SameConnection(target.GetBankEndpoint()))
        { sourceTab.ViewModel.ReportStatus($"Bank target unavailable: {request.Binding.TargetName}. No other device was used; the command was not queued."); return; }
        if (!_busyBankTargets.Add(target))
        { sourceTab.ViewModel.ReportStatus("The target is completing its previous bank change. Try again shortly."); return; }
        try
        {
            var result = await targetTab.ViewModel.ChangeBankAsync(endpoint, request.Binding.Operation, request.Binding.LayerId, request.Binding.AlternateLayerId);
            sourceTab.ViewModel.ReportStatus(result.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { sourceTab.ViewModel.ReportStatus("Bank control could not complete: " + ex.Message); }
        finally { _busyBankTargets.Remove(target); }
    }
}
