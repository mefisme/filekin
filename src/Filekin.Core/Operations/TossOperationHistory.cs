using Filekin.Core.Commands.App;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Operations;

/// <summary>Maps one authoritative recoverable-delete result into its journal-ready form.</summary>
public sealed record TossOperationHistory(
    string Summary,
    TossOperationPayload Payload,
    bool CanRestore,
    string? RestoreUnavailableReason)
{
    public static TossOperationHistory? TryCreate(AppCommandExecutionDetail execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (!IsTossCommand(execution.CommandName) || execution.Result.RecycleOutcomes.Count == 0)
        {
            return null;
        }

        var items = execution.Result.RecycleOutcomes.Select(static outcome =>
        {
            var recycled = outcome.RecycledItem;
            var name = recycled?.Name ?? Path.GetFileName(
                outcome.OriginalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return new TossedItem(
                outcome.OriginalPath,
                string.IsNullOrEmpty(name) ? outcome.OriginalPath : name,
                outcome.EntryKind == FileSystemEntryKind.Directory,
                recycled?.RecycleBinIdentity,
                outcome.RestoreUnavailableReason);
        }).ToArray();
        var payload = new TossOperationPayload(items, execution.Result.Failures);
        var unavailable = items.Where(static item => !item.CanRestore).ToArray();
        var reason = unavailable.Length switch
        {
            0 => null,
            1 => $"Restore is unavailable. {unavailable[0].RestoreUnavailableReason}",
            _ => $"Restore is unavailable because Windows did not return exact Recycle Bin identities for {unavailable.Length} items.",
        };
        return new TossOperationHistory(
            execution.Result.Message,
            payload,
            payload.CanRestore,
            reason);
    }

    private static bool IsTossCommand(string commandName) =>
        commandName.Equals("toss", StringComparison.OrdinalIgnoreCase) ||
        commandName.Equals("trash", StringComparison.OrdinalIgnoreCase) ||
        commandName.Equals("delete", StringComparison.OrdinalIgnoreCase);
}
