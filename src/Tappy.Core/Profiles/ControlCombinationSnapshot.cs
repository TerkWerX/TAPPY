using Tappy.Core.Input;

namespace Tappy.Core.Profiles;

public sealed record ControlCombinationSnapshot(ControlId ControlId, string Name, IReadOnlyList<ControlId> Members);
