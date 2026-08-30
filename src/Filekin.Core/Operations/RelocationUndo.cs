using Filekin.Core.FileSystem;

namespace Filekin.Core.Operations;

/// <summary>Why a relocation cannot be reversed immediately without a user decision or new state.</summary>
public enum RelocationUndoIssueKind
{
    OriginalPathOccupied,
    MovedItemMissing,
    InspectionFailed,
}

/// <summary>The aggregate present-state safety of one Move/Rename operation.</summary>
public enum RelocationUndoSafety
{
    Ready,
    NeedsConflictResolution,
    Unavailable,
}

public sealed record RelocationUndoIssue(
    PathRelocation Relocation,
    RelocationUndoIssueKind Kind,
    string Message);

/// <summary>
/// A side-effect-free present-state check. A missing moved item makes the operation unavailable;
/// an occupied original path must be resolved by the future conflict UI; only a clean assessment is
/// eligible to run immediately.
/// </summary>
public sealed record RelocationUndoAssessment
{
    public RelocationUndoAssessment(IReadOnlyList<RelocationUndoIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = [.. issues];
        Safety = Issues.Count == 0
            ? RelocationUndoSafety.Ready
            : Issues.Any(issue => issue.Kind is not RelocationUndoIssueKind.OriginalPathOccupied)
                ? RelocationUndoSafety.Unavailable
                : RelocationUndoSafety.NeedsConflictResolution;
    }

    public RelocationUndoSafety Safety { get; }

    public IReadOnlyList<RelocationUndoIssue> Issues { get; }

    public bool IsReady => Safety == RelocationUndoSafety.Ready;
}

public sealed class RelocationUndoEvaluator
{
    private readonly IFileSystemOperations _operations;

    public RelocationUndoEvaluator(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public RelocationUndoAssessment Evaluate(RelocationOperationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var issues = new List<RelocationUndoIssue>();
        foreach (var relocation in payload.PendingRelocations)
        {
            if (Evaluate(relocation) is { } issue)
            {
                issues.Add(issue);
            }
        }

        return new RelocationUndoAssessment(issues);
    }

    internal RelocationUndoIssue? Evaluate(PathRelocation relocation)
    {
        try
        {
            if (_operations.GetKind(relocation.DestinationPath) == FileSystemEntryKind.None)
            {
                return new RelocationUndoIssue(
                    relocation,
                    RelocationUndoIssueKind.MovedItemMissing,
                    $"The moved item is no longer at {relocation.DestinationPath}.");
            }

            if (_operations.GetKind(relocation.SourcePath) != FileSystemEntryKind.None)
            {
                return new RelocationUndoIssue(
                    relocation,
                    RelocationUndoIssueKind.OriginalPathOccupied,
                    $"The original path is occupied: {relocation.SourcePath}");
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
        {
            return new RelocationUndoIssue(
                relocation,
                RelocationUndoIssueKind.InspectionFailed,
                $"Could not check whether the move is safe to undo: {ex.Message}");
        }

        return null;
    }

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;
}

/// <summary>The final shape of one attempt, including exact work left after a partial reversal.</summary>
public enum RelocationUndoOutcome
{
    Succeeded,
    PartiallyUndone,
    Failed,
    Blocked,
}

public sealed record RelocationUndoFailure(
    PathRelocation Relocation,
    string Message,
    bool MayHaveReversed);

public sealed record RelocationUndoResult
{
    public RelocationUndoResult(
        RelocationUndoOutcome outcome,
        IReadOnlyList<PathRelocation> reversedRelocations,
        IReadOnlyList<PathRelocation> remainingRelocations,
        IReadOnlyList<RelocationUndoFailure> failures,
        RelocationUndoAssessment? blockedBy,
        RelocationOperationPayload updatedPayload)
    {
        ArgumentNullException.ThrowIfNull(reversedRelocations);
        ArgumentNullException.ThrowIfNull(remainingRelocations);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(updatedPayload);
        Outcome = outcome;
        ReversedRelocations = [.. reversedRelocations];
        RemainingRelocations = [.. remainingRelocations];
        Failures = [.. failures];
        BlockedBy = blockedBy;
        UpdatedPayload = updatedPayload;
    }

    public RelocationUndoOutcome Outcome { get; }

    public IReadOnlyList<PathRelocation> ReversedRelocations { get; }

    public IReadOnlyList<PathRelocation> RemainingRelocations { get; }

    public IReadOnlyList<RelocationUndoFailure> Failures { get; }

    public RelocationUndoAssessment? BlockedBy { get; }

    /// <summary>The durable payload with only unfinished relocations left pending.</summary>
    public RelocationOperationPayload UpdatedPayload { get; }

    /// <summary>
    /// Whether Files must refresh, including an I/O failure that may have thrown after writing.
    /// </summary>
    public bool MayHaveChangedFileSystem =>
        ReversedRelocations.Count > 0 || Failures.Any(failure => failure.MayHaveReversed);
}

/// <summary>
/// Reverses a fully ready Move/Rename operation. The preflight is fail-closed and does not choose a
/// collision action. Execution runs in reverse order and preserves exact partial state if ordinary
/// filesystem failures or a newly appeared collision interrupt the attempt.
/// </summary>
public sealed class RelocationUndo
{
    private readonly IFileSystemOperations _operations;
    private readonly RelocationUndoEvaluator _evaluator;

    public RelocationUndo(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
        _evaluator = new RelocationUndoEvaluator(operations);
    }

    public RelocationUndoResult Undo(RelocationOperationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var assessment = _evaluator.Evaluate(payload);
        if (!assessment.IsReady)
        {
            return Blocked(payload, assessment);
        }

        var reversed = new List<PathRelocation>();
        var failures = new List<RelocationUndoFailure>();
        var completed = new bool[payload.PendingRelocations.Count];
        RelocationUndoAssessment? blockedBy = null;

        for (var index = payload.PendingRelocations.Count - 1; index >= 0; index--)
        {
            var relocation = payload.PendingRelocations[index];

            // The filesystem can change after preflight. Re-check each item immediately before its
            // move so a newly occupied original path is never overwritten or silently skipped.
            if (_evaluator.Evaluate(relocation) is { } issue)
            {
                blockedBy = new RelocationUndoAssessment([issue]);
                break;
            }

            try
            {
                _operations.Move(relocation.DestinationPath, relocation.SourcePath);
                reversed.Add(relocation);
                completed[index] = true;
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                failures.Add(new RelocationUndoFailure(
                    relocation,
                    ex.Message,
                    MayHaveWritten(ex)));
            }
        }

        var remaining = payload.PendingRelocations
            .Where((_, index) => !completed[index])
            .ToArray();
        var outcome = reversed.Count == payload.PendingRelocations.Count
            ? RelocationUndoOutcome.Succeeded
            : reversed.Count > 0
                ? RelocationUndoOutcome.PartiallyUndone
                : blockedBy is not null
                    ? RelocationUndoOutcome.Blocked
                    : RelocationUndoOutcome.Failed;

        return new RelocationUndoResult(
            outcome,
            reversed,
            remaining,
            failures,
            blockedBy,
            payload.WithPendingRelocations(remaining));
    }

    private static RelocationUndoResult Blocked(
        RelocationOperationPayload payload,
        RelocationUndoAssessment assessment) =>
        new(
            RelocationUndoOutcome.Blocked,
            [],
            payload.PendingRelocations,
            [],
            assessment,
            payload);

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;

    private static bool MayHaveWritten(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
