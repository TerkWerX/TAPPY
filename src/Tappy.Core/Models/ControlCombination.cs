using System.Security.Cryptography;
using System.Text;
using Tappy.Core.Input;

namespace Tappy.Core.Models;

public sealed class ControlCombination
{
    public ControlId ControlId { get; set; }
    public string Name { get; set; } = "Combination";
    public List<ControlId> Members { get; set; } = [];
    public static ControlId IdFor(IEnumerable<ControlId> members) => new("combination:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", members.Select(m => m.Value).Distinct().Order(StringComparer.Ordinal))))));
    public void Normalize()
    {
        Members = (Members ?? []).Distinct().OrderBy(m => m.Value, StringComparer.Ordinal).ToList();
        if (Members.Count is < 2 or > 8 || Members.Any(m => m.IsEmpty || m.Value.StartsWith("combination:", StringComparison.Ordinal)) || ControlId != IdFor(Members))
        { throw new InvalidDataException("A combination needs 2–8 distinct physical controls and a matching identity."); }
        Name = string.IsNullOrWhiteSpace(Name) ? "Combination" : Name.Trim();
    }
    public ControlCombination Clone() => new() { ControlId = ControlId, Name = Name, Members = Members is null ? [] : [.. Members] };
    public static bool RequiresHold(ControlBindingDefinition binding) => binding.Enabled &&
        (binding.PressAction.Mode == KeyboardActionMode.HoldUntilRelease ||
         (!binding.PressSequence.IsEmpty && binding.PressSequence.Mode != ControllerActionSequenceMode.RunOnce));
}
