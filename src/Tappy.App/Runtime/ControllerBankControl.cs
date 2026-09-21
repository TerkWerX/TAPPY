using Tappy.Core.Models;

namespace Tappy.App.Runtime;

public sealed record ControllerBankChoice(string Id, string Label);
public sealed record ControllerBankEndpoint(
    string SessionId, string PersistentId, string ProviderId, long Generation,
    string DisplayName, string ActiveLayerId, IReadOnlyList<ControllerBankChoice> Banks,
    string ConnectionToken = "", bool RequiresSessionBinding = false)
{
    public bool SameConnection(ControllerBankEndpoint? other) => other is not null &&
        SessionId == other.SessionId && PersistentId == other.PersistentId &&
        ProviderId == other.ProviderId && Generation == other.Generation && ConnectionToken == other.ConnectionToken;
}

public sealed record ControllerBankRequest(ControllerBankEndpoint Source, BankControlBinding Binding);
