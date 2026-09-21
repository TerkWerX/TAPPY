using System.Text.Json.Serialization;
using Tappy.Core.Input;

namespace Tappy.Core.Models;

public enum BankControlOperation { Next, Previous, Select, Toggle }

/// <summary>An all-bank navigation overlay. Removing it restores the untouched per-bank bindings.</summary>
public sealed record BankControlBinding(
    ControlId ControlId,
    string TargetProviderId,
    string TargetPersistentId,
    string TargetName,
    BankControlOperation Operation,
    string LayerId = "",
    string TargetConfirmationToken = "",
    string AlternateLayerId = "")
{
    [JsonIgnore]
    public string Label => $"{Operation switch { BankControlOperation.Next => "Next bank", BankControlOperation.Previous => "Previous bank", BankControlOperation.Toggle => $"Toggle banks {LayerId.Replace("layer-", "")}/{AlternateLayerId.Replace("layer-", "")}", _ => $"Bank {LayerId.Replace("layer-", "")}" }} → {TargetName}";

    public void Validate()
    {
        if (ControlId.IsEmpty || string.IsNullOrWhiteSpace(TargetProviderId) ||
            string.IsNullOrWhiteSpace(TargetPersistentId) || string.IsNullOrWhiteSpace(TargetName) ||
            !Enum.IsDefined(Operation) || (Operation is BankControlOperation.Select or BankControlOperation.Toggle && string.IsNullOrWhiteSpace(LayerId)) ||
            (Operation == BankControlOperation.Toggle && (string.IsNullOrWhiteSpace(AlternateLayerId) || AlternateLayerId == LayerId)))
        { throw new InvalidDataException("A bank control requires a physical source, an exact target and a valid bank operation."); }
    }
}
