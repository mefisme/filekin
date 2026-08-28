namespace Filekin.Core.Tidy;

/// <summary>One file Tidy could not move, and the error a human needs to see.</summary>
public sealed record TidyFailure(string Name, string Reason);

/// <summary>
/// What one <c>/tidy</c> run actually did.
///
/// The counts are per run, never cumulative (owner decision, 2026-08-27): tidying a folder that was
/// already tidied last week reports only the files moved this time. <see cref="Skipped"/> carries the
/// planner's refusals forward so the result view can explain every file that did not move.
/// </summary>
public sealed record TidyOutcome(
    string FolderPath,
    int MovedCount,
    IReadOnlyList<TidyCategory> FoldersUsed,
    IReadOnlyList<TidySkip> Skipped,
    IReadOnlyList<TidyFailure> Failures)
{
    public bool MovedAnything => MovedCount > 0;
}

/// <summary>Progress while a run is under way, for the command-bar task row.</summary>
/// <param name="FilesDone">Files moved so far.</param>
/// <param name="FilesTotal">Files this run intends to move.</param>
/// <param name="CurrentName">The file being moved, or empty when finishing.</param>
public readonly record struct TidyProgress(int FilesDone, int FilesTotal, string CurrentName);
