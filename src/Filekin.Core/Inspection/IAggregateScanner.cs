namespace Filekin.Core.Inspection;

/// <summary>
/// Running totals for a recursive size/count scan. <see cref="IsComplete"/> distinguishes a partial
/// tick from the final answer, and <see cref="HasUnreadableFolders"/> keeps a partial total honest
/// instead of presenting it as the whole truth (DECISIONS.md, 2026-08-27).
/// </summary>
public readonly record struct AggregateTotals(
    long Bytes,
    int Files,
    int Folders,
    bool IsComplete,
    bool HasUnreadableFolders);

/// <summary>
/// Walks one or more roots and reports size and item counts. Implementations report progress
/// periodically so the Info view can fill in while the walk continues, and must honour cancellation
/// so leaving the view stops the work.
/// </summary>
public interface IAggregateScanner
{
    /// <summary>
    /// Totals every file under <paramref name="roots"/>. Files listed directly in
    /// <paramref name="roots"/> are always counted as themselves; folders are walked recursively.
    /// <paramref name="onProgress"/> is called on the calling thread at the implementation's own
    /// throttled cadence, never once per file.
    /// </summary>
    /// <param name="countRootFoldersThemselves">
    /// Whether a folder in <paramref name="roots"/> adds one to the folder count. A selection counts
    /// its own folders, so <c>37 items</c> really is <c>31 files + 6 folders</c>; a single folder
    /// target does not, because it is the subject of the sheet rather than one of its contents.
    /// </param>
    AggregateTotals Scan(
        IReadOnlyList<string> roots,
        bool countRootFoldersThemselves,
        Action<AggregateTotals> onProgress,
        CancellationToken cancellationToken);
}
