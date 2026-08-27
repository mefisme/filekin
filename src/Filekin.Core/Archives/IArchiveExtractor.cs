namespace Filekin.Core.Archives;

/// <summary>
/// Writes the files an <see cref="ArchivePlan"/> describes.
///
/// The extractor never re-decides anything: it writes exactly what the plan lists, which is what
/// guarantees the preview the user approved is the operation that runs. Everything it does is I/O,
/// so it is always awaited off the UI thread.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Extracts <paramref name="plan"/>, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// A single entry that cannot be written is collected into
    /// <see cref="ExtractionOutcome.Failures"/> rather than abandoning the rest, which is the
    /// partial-success rule ARCHITECTURE.md sets for batch operations and names <c>/unzip</c> in.
    /// Cancellation stops early and still returns what was written, so it can be undone.
    /// </remarks>
    Task<ExtractionOutcome> ExtractAsync(
        ArchivePlan plan,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Reverses a completed extraction, using the record the extraction left behind.</summary>
public interface IExtractionUndo
{
    /// <summary>
    /// Deletes what <paramref name="outcome"/> created and restores what it replaced.
    /// </summary>
    /// <returns>A short line describing what was reversed, for the command-bar result.</returns>
    Task<string> UndoAsync(ExtractionOutcome outcome, CancellationToken cancellationToken = default);
}
