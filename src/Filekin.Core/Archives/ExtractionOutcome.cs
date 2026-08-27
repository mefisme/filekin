namespace Filekin.Core.Archives;

/// <summary>How far extraction has got, for the progress the preview shows while it runs.</summary>
/// <param name="FilesDone">Files written so far.</param>
/// <param name="FilesTotal">Files the plan expects to write.</param>
/// <param name="BytesDone">Uncompressed bytes written so far.</param>
/// <param name="BytesTotal">Uncompressed bytes the plan expects to write.</param>
/// <param name="CurrentEntry">The entry being written, for the status line.</param>
public readonly record struct ExtractionProgress(
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    long BytesTotal,
    string CurrentEntry);

/// <summary>
/// Exactly what extraction did, in enough detail to undo it.
///
/// This is the journal payload, so it is plain serializable data and nothing else — see
/// <see cref="Operations.JournalEntry"/> for why. Undo needs three lists and no cleverness: delete
/// the files we created, remove the folders we created (deepest first), and put back the originals
/// we sent to the Recycle Bin.
/// </summary>
public sealed record ExtractionOutcome
{
    public ExtractionOutcome(
        string archivePath,
        string targetRoot,
        IReadOnlyList<string> createdFiles,
        IReadOnlyList<string> createdDirectories,
        IReadOnlyList<string> replacedOriginals,
        int skippedCount,
        IReadOnlyList<string> failures)
    {
        ArchivePath = archivePath;
        TargetRoot = targetRoot;
        CreatedFiles = createdFiles;
        CreatedDirectories = createdDirectories;
        ReplacedOriginals = replacedOriginals;
        SkippedCount = skippedCount;
        Failures = failures;
    }

    /// <summary>Parameterless construction for the JSON round-trip through the journal.</summary>
    public ExtractionOutcome()
        : this(string.Empty, string.Empty, [], [], [], 0, [])
    {
    }

    public string ArchivePath { get; init; }

    /// <summary>The folder the content landed in.</summary>
    public string TargetRoot { get; init; }

    /// <summary>Files this extraction wrote. Undo deletes these.</summary>
    public IReadOnlyList<string> CreatedFiles { get; init; }

    /// <summary>
    /// Folders this extraction created, shallowest first. Undo removes them in reverse, and only
    /// when they are empty, so a folder that already held something of the user's survives.
    /// </summary>
    public IReadOnlyList<string> CreatedDirectories { get; init; }

    /// <summary>
    /// Original files sent to the Recycle Bin before being replaced. Undo restores these, which is
    /// what makes <c>-overwrite</c> a survivable default.
    /// </summary>
    public IReadOnlyList<string> ReplacedOriginals { get; init; }

    /// <summary>Files left alone because they already existed and the policy was Skip.</summary>
    public int SkippedCount { get; init; }

    /// <summary>Entries that could not be written, already worded for display.</summary>
    public IReadOnlyList<string> Failures { get; init; }

    /// <summary>Whether anything was actually written, which is what makes an undo worth offering.</summary>
    public bool WroteAnything => CreatedFiles.Count > 0 || CreatedDirectories.Count > 0;

    public string ArchiveName => Path.GetFileName(ArchivePath);
}
