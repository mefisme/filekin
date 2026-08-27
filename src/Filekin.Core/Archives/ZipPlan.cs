namespace Filekin.Core.Archives;

/// <summary>One file that will be stored in the new archive.</summary>
/// <param name="SourcePath">The file on disk.</param>
/// <param name="EntryPath">The name it takes inside the archive, forward-slash separated.</param>
/// <param name="Length">The file's size in bytes.</param>
public readonly record struct ZipPlanEntry(string SourcePath, string EntryPath, long Length);

/// <summary>A source that will not be stored, and why.</summary>
/// <param name="SourcePath">The item that was skipped.</param>
/// <param name="Reason">A short explanation, written to be shown in the preview.</param>
public readonly record struct SkippedSource(string SourcePath, string Reason);

/// <summary>
/// What <c>/zip</c> will write, and from what.
///
/// The mirror of <see cref="ArchivePlan"/>, and it exists for the same reason: the preview and the
/// operation read from one object, so what the user approved is what runs.
/// </summary>
public sealed record ZipPlan
{
    internal ZipPlan(
        IReadOnlyList<string> sources,
        string outputPath,
        bool includeRoot,
        CollisionPolicy collisionPolicy,
        IReadOnlyList<ZipPlanEntry> entries,
        IReadOnlyList<SkippedSource> skipped,
        bool outputExists)
    {
        Sources = sources;
        OutputPath = outputPath;
        IncludeRoot = includeRoot;
        CollisionPolicy = collisionPolicy;
        Entries = entries;
        Skipped = skipped;
        OutputExists = outputExists;
    }

    /// <summary>The files and folders being compressed, as the user named them.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The archive that will be written.</summary>
    public string OutputPath { get; }

    /// <summary>The archive's file name, for the preview heading.</summary>
    public string OutputName => Path.GetFileName(OutputPath);

    /// <summary>
    /// Whether a single folder source keeps its own name as a folder inside the archive. This is the
    /// inverse of <c>/unzip -noroot</c>: with it off, unzipping the result spills the contents out
    /// rather than restoring the folder.
    /// </summary>
    public bool IncludeRoot { get; }

    /// <summary>What happens if <see cref="OutputPath"/> is already there.</summary>
    public CollisionPolicy CollisionPolicy { get; }

    /// <summary>Every file that will be stored.</summary>
    public IReadOnlyList<ZipPlanEntry> Entries { get; }

    /// <summary>Sources that could not be read, shown rather than hidden.</summary>
    public IReadOnlyList<SkippedSource> Skipped { get; }

    /// <summary>Whether an archive already exists at <see cref="OutputPath"/>.</summary>
    public bool OutputExists { get; }

    public int FileCount => Entries.Count;

    /// <summary>The total size of the files going in, before compression.</summary>
    public long TotalBytes => Entries.Sum(entry => entry.Length);

    public bool IsEmpty => Entries.Count == 0;
}
