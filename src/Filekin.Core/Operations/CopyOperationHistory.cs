using Filekin.Core.Commands.App;

namespace Filekin.Core.Operations;

/// <summary>Maps an authoritative common app-command result into the v1 informational Copy row.</summary>
public sealed record CopyOperationHistory(string Summary, CopyOperationPayload Payload)
{
    /// <summary>
    /// Returns one history record only when <c>/copy</c> reports at least one known successful
    /// destination. Refusals and failures with only unknown writes remain unjournaled.
    /// </summary>
    public static CopyOperationHistory? TryCreate(AppCommandExecutionDetail execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var result = execution.Result;
        if (!execution.CommandName.Equals("copy", StringComparison.OrdinalIgnoreCase) ||
            result.Outcome is not (AppCommandOutcome.Success or AppCommandOutcome.PartialSuccess) ||
            result.AffectedPaths.Count == 0)
        {
            return null;
        }

        return new CopyOperationHistory(
            result.Message,
            new CopyOperationPayload(result.AffectedPaths, result.Failures));
    }
}
