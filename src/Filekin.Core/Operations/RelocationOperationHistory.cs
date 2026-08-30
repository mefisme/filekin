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
            result.Relocations.Count == 0)
        {
            return null;
        }

        return new RelocationOperationHistory(
            kind,
            result.Message,
            new RelocationOperationPayload(result.Relocations, result.Failures));
    }
}
