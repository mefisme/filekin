using Filekin.Core.FileSystem;

namespace Filekin.Core.Operations;

/// <summary>Why a tossed item cannot be restored immediately without new state or a user decision.</summary>
public enum TossRestoreIssueKind
{
    RestoreIdentityUnavailable,
    RecycledItemMissing,
    OriginalPathOccupied,
    InspectionFailed,
}

public enum TossRestoreSafety
{
    Ready,
    NeedsConflictResolution,
    Unavailable,
}

public sealed record TossRestoreIssue(
    TossedItem Item,
    TossRestoreIssueKind Kind,
    string Message);

/// <summary>A side-effect-free assessment of whether one Toss invocation can be restored now.</summary>
public sealed record TossRestoreAssessment
{
    public TossRestoreAssessment(IReadOnlyList<TossRestoreIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = [.. issues];
        Safety = Issues.Count == 0
            ? TossRestoreSafety.Ready
            : Issues.All(issue => issue.Kind == TossRestoreIssueKind.OriginalPathOccupied)
                ? TossRestoreSafety.NeedsConflictResolution
                : TossRestoreSafety.Unavailable;
    }

    public TossRestoreSafety Safety { get; }

    public IReadOnlyList<TossRestoreIssue> Issues { get; }

    public bool IsReady => Safety == TossRestoreSafety.Ready;
}

public sealed class TossRestoreEvaluator
{
    private readonly IFileSystemOperations _fileSystem;
    private readonly IRecycleBin _recycleBin;

    public TossRestoreEvaluator(IFileSystemOperations fileSystem, IRecycleBin recycleBin)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(recycleBin);
        _fileSystem = fileSystem;
        _recycleBin = recycleBin;
    }

    public TossRestoreAssessment Evaluate(TossOperationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        IReadOnlyList<RecycledItem> available;
        try
        {
            available = _recycleBin.List();
        }
        catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
        {
            return new TossRestoreAssessment(payload.PendingItems.Select(item => new TossRestoreIssue(
                item,
                TossRestoreIssueKind.InspectionFailed,
                $"Could not inspect the Recycle Bin before Restore: {ex.Message}")).ToArray());
        }

        return new TossRestoreAssessment(payload.PendingItems
            .Select(item => Evaluate(item, available))
            .Where(static issue => issue is not null)
            .Select(static issue => issue!)
            .ToArray());
    }

    internal TossRestoreIssue? Evaluate(TossedItem item, IReadOnlyList<RecycledItem> available)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(available);
        if (!item.CanRestore)
        {
            return new TossRestoreIssue(
                item,
                TossRestoreIssueKind.RestoreIdentityUnavailable,
                item.RestoreUnavailableReason ?? "The exact Recycle Bin identity is unavailable.");
        }

        if (FindExactItem(item, available) is null)
        {
            return new TossRestoreIssue(
                item,
                TossRestoreIssueKind.RecycledItemMissing,
                $"The exact recycled item is no longer available for {item.OriginalPath}.");
        }

        try
        {
            if (_fileSystem.GetKind(item.OriginalPath) != FileSystemEntryKind.None)
            {
                return new TossRestoreIssue(
                    item,
                    TossRestoreIssueKind.OriginalPathOccupied,
                    $"The original path is occupied: {item.OriginalPath}");
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
        {
            return new TossRestoreIssue(
                item,
                TossRestoreIssueKind.InspectionFailed,
                $"Could not check whether Restore is safe: {ex.Message}");
        }

        return null;
    }

    internal static RecycledItem? FindExactItem(
        TossedItem item,
        IReadOnlyList<RecycledItem> available)
    {
        if (!item.CanRestore)
        {
            return null;
        }

        var matches = available.Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.RecycleBinIdentity) &&
                string.Equals(
                    candidate.RecycleBinIdentity,
                    item.RecycleBinIdentity,
                    StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;
}

public enum TossRestoreOutcome
{
    Succeeded,
    PartiallyRestored,
    Failed,
    Blocked,
}

public sealed record TossRestoreFailure(
    TossedItem Item,
    string Message,
    bool MayHaveRestored);

public sealed record TossRestoreResult
{
    public TossRestoreResult(
        TossRestoreOutcome outcome,
        IReadOnlyList<TossedItem> restoredItems,
        IReadOnlyList<TossedItem> remainingItems,
        IReadOnlyList<TossRestoreFailure> failures,
        TossRestoreAssessment? blockedBy,
        TossOperationPayload updatedPayload)
    {
        ArgumentNullException.ThrowIfNull(restoredItems);
        ArgumentNullException.ThrowIfNull(remainingItems);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(updatedPayload);
        Outcome = outcome;
        RestoredItems = [.. restoredItems];
        RemainingItems = [.. remainingItems];
        Failures = [.. failures];
        BlockedBy = blockedBy;
        UpdatedPayload = updatedPayload;
    }

    public TossRestoreOutcome Outcome { get; }

    public IReadOnlyList<TossedItem> RestoredItems { get; }

    public IReadOnlyList<TossedItem> RemainingItems { get; }

    public IReadOnlyList<TossRestoreFailure> Failures { get; }

    public TossRestoreAssessment? BlockedBy { get; }

    public TossOperationPayload UpdatedPayload { get; }

    public bool MayHaveChangedFileSystem =>
        RestoredItems.Count > 0 || Failures.Any(static failure => failure.MayHaveRestored);
}

/// <summary>
/// Restores a fully ready Toss invocation in reverse order. Every item is checked again immediately
/// before its shell action; exact unfinished items remain in the payload after a failed or partial run.
/// </summary>
public sealed class TossRestore
{
    private readonly IRecycleBin _recycleBin;
    private readonly TossRestoreEvaluator _evaluator;

    public TossRestore(IFileSystemOperations fileSystem, IRecycleBin recycleBin)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(recycleBin);
        _recycleBin = recycleBin;
        _evaluator = new TossRestoreEvaluator(fileSystem, recycleBin);
    }

    public TossRestoreResult Restore(TossOperationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var assessment = _evaluator.Evaluate(payload);
        if (!assessment.IsReady)
        {
            return Blocked(payload, assessment);
        }

        var restored = new List<TossedItem>();
        var failures = new List<TossRestoreFailure>();
        var completed = new bool[payload.PendingItems.Count];
        TossRestoreAssessment? blockedBy = null;

        for (var index = payload.PendingItems.Count - 1; index >= 0; index--)
        {
            var item = payload.PendingItems[index];
            IReadOnlyList<RecycledItem> available;
            try
            {
                available = _recycleBin.List();
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                blockedBy = new TossRestoreAssessment([
                    new TossRestoreIssue(
                        item,
                        TossRestoreIssueKind.InspectionFailed,
                        $"Could not recheck the Recycle Bin before Restore: {ex.Message}"),
                ]);
                break;
            }

            if (_evaluator.Evaluate(item, available) is { } issue)
            {
                blockedBy = new TossRestoreAssessment([issue]);
                break;
            }

            var recycledItem = TossRestoreEvaluator.FindExactItem(item, available)!;
            try
            {
                if (_recycleBin.Restore(recycledItem))
                {
                    restored.Add(item);
                    completed[index] = true;
                }
                else
                {
                    failures.Add(new TossRestoreFailure(
                        item,
                        $"The exact recycled item could not be restored to {item.OriginalPath}.",
                        MayHaveRestored: false));
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                failures.Add(new TossRestoreFailure(item, ex.Message, MayHaveWritten(ex)));
            }
        }

        var remaining = payload.PendingItems.Where((_, index) => !completed[index]).ToArray();
        var outcome = restored.Count == payload.PendingItems.Count
            ? TossRestoreOutcome.Succeeded
            : restored.Count > 0
                ? TossRestoreOutcome.PartiallyRestored
                : blockedBy is not null
                    ? TossRestoreOutcome.Blocked
                    : TossRestoreOutcome.Failed;
        return new TossRestoreResult(
            outcome,
            restored,
            remaining,
            failures,
            blockedBy,
            payload.WithPendingItems(remaining));
    }

    private static TossRestoreResult Blocked(
        TossOperationPayload payload,
        TossRestoreAssessment assessment) =>
        new(
            TossRestoreOutcome.Blocked,
            [],
            payload.PendingItems,
            [],
            assessment,
            payload);

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;

    private static bool MayHaveWritten(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
