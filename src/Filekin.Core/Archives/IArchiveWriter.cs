namespace Filekin.Core.Archives;

/// <summary>How far compression has got.</summary>
/// <param name="FilesDone">Files stored so far.</param>
/// <param name="FilesTotal">Files the plan expects to store.</param>
/// <param name="BytesDone">Source bytes read so far.</param>
/// <param name="BytesTotal">Source bytes the plan expects to read.</param>
/// <param name="CurrentEntry">The entry being stored, for the status line.</param>
public readonly record struct CompressionProgress(
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    long BytesTotal,
    string CurrentEntry);

/// <summary>
/// What compression did, in enough detail to undo it.
///
/// Undoing a <c>/zip</c> is far simpler than undoing an extraction: one file was created, and at
/// most one was replaced. It is still recorded the same way, through the journal, so both commands
/// reach <c>/undo</c> and <c>/history</c> by the same route when the durable store is built.
/// </summary>
public sealed record CompressionOutcome
{
    public CompressionOutcome(
        string outputPath,
        int filesStored,
        long bytesRead,
        long archiveBytes,
        string? replacedOriginal,
        IReadOnlyList<string> failures,
        ArchiveOutputEvidence? outputEvidence = null,
        ArchiveReplacementEvidence? replacementEvidence = null)
    {
        OutputPath = outputPath;
        FilesStored = filesStored;
        BytesRead = bytesRead;
        ArchiveBytes = archiveBytes;
        ReplacedOriginal = replacedOriginal;
        Failures = failures;
        OutputEvidence = outputEvidence;
        ReplacementEvidence = replacementEvidence;
    }

    /// <summary>Parameterless construction for the JSON round-trip through the journal.</summary>
    public CompressionOutcome()
        : this(string.Empty, 0, 0, 0, null, [])
    {
    }

    /// <summary>The archive that was written. Undo deletes this.</summary>
    public string OutputPath { get; init; }

    public string OutputName => Path.GetFileName(OutputPath);

    public int FilesStored { get; init; }

    /// <summary>The total size of the files that went in.</summary>
    public long BytesRead { get; init; }

    /// <summary>The size of the finished archive.</summary>
    public long ArchiveBytes { get; init; }

    /// <summary>
    /// An archive of the same name that was replaced and sent to the Recycle Bin, or <c>null</c>.
    /// Undo restores it.
    /// </summary>
    public string? ReplacedOriginal { get; init; }

    /// <summary>The completion-time fingerprint of <see cref="OutputPath"/>, when it was written.</summary>
    public ArchiveOutputEvidence? OutputEvidence { get; init; }

    /// <summary>Exact Recycle Bin evidence for <see cref="ReplacedOriginal"/>, when one existed.</summary>
    public ArchiveReplacementEvidence? ReplacementEvidence { get; init; }

    /// <summary>Files that could not be stored, already worded for display.</summary>
    public IReadOnlyList<string> Failures { get; init; }
}

/// <summary>
/// Writes the archive a <see cref="ZipPlan"/> describes. Like the extractor, it re-decides nothing:
/// it stores exactly what the plan lists.
/// </summary>
public interface IArchiveWriter
{
    /// <summary>
    /// Compresses <paramref name="plan"/>, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read is collected into <see cref="CompressionOutcome.Failures"/> rather
    /// than abandoning the rest. Cancellation removes the half-written archive, because a truncated
    /// zip is worse than no zip — it looks like a real one.
    /// </remarks>
    Task<CompressionOutcome> CompressAsync(
        ZipPlan plan,
        IProgress<CompressionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Reverses a completed <c>/zip</c>.</summary>
public interface ICompressionUndo
{
    /// <summary>Deletes the archive that was created and restores any archive it replaced.</summary>
    /// <returns>A short line describing what was reversed, for the command-bar result.</returns>
    Task<string> UndoAsync(CompressionOutcome outcome, CancellationToken cancellationToken = default);
}
