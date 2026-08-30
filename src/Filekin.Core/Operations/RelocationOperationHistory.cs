using Filekin.Core.Commands.App;

namespace Filekin.Core.Operations;

/// <summary>One journal-ready Move or Rename operation produced from the common command result.</summary>
public sealed record RelocationOperationHistory(
    string Kind,
    string Summary,
    RelocationOperationPayload Payload)
{
    public static RelocationOperationHistory? TryCreate(AppCommandExecutionDetail execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var kind = execution.CommandName.Equals("move", StringComparison.OrdinalIgnoreCase)
            ? "move"
            : execution.CommandName.Equals("rename", StringComparison.OrdinalIgnoreCase)
                ? "rename"
                : null;
        var result = execution.Result;
        if (kind is null ||
            result.Outcome is not (AppCommandOutcome.Success or AppCommandOutcome.PartialSuccess) ||
            execution.EffectiveRelocations.Count == 0)
        {
            return null;
        }

        var summary = execution.EffectiveRelocations.Count == result.Relocations.Count
            ? result.Message
            : kind == "rename"
                ? "Rename remained applied after an incomplete rollback."
                : execution.EffectiveRelocations.Count == 1
                    ? "1 moved item remains after an incomplete rollback."
                    : $"{execution.EffectiveRelocations.Count} moved items remain after an incomplete rollback.";
        return new RelocationOperationHistory(
            kind,
            summary,
            new RelocationOperationPayload(execution.EffectiveRelocations, result.Failures));
    }
}
