namespace Filekin.Core.Archives;

/// <summary>One entry after Filekin has decided where it lands.</summary>
/// <param name="EntryPath">The raw in-archive name, kept so the extractor can find the entry again.</param>
/// <param name="RelativeTarget">The path beneath <see cref="ArchivePlan.TargetRoot"/>, platform separators.</param>
/// <param name="Length">The uncompressed size in bytes.</param>
/// <param name="IsDirectory">Whether this entry only creates a folder.</param>
public readonly record struct PlannedEntry(
    string EntryPath,
    string RelativeTarget,
    long Length,
    bool IsDirectory);

/// <summary>An in-archive entry that will not be extracted, and the reason why.</summary>
/// <param name="EntryPath">The raw in-archive name.</param>
/// <param name="Reason">A short explanation, written to be shown in the preview.</param>
public readonly record struct RejectedEntry(string EntryPath, string Reason);

/// <summary>
/// The complete, inspectable answer to "what will <c>/unzip</c> write, and where".
///
/// Everything the preview shows and everything the extractor does comes from this one object, so the
/// preview cannot drift from the operation it is previewing. It is also what the undo record is built
/// from, because it already lists every path extraction will touch.
/// </summary>
public sealed record ArchivePlan
{
    internal ArchivePlan(
        string archivePath,
        string destinationRoot,
        UnzipLayout layout,
        CollisionPolicy collisionPolicy,
        string? folderName,
        string? wrapperName,
        IReadOnlyList<PlannedEntry> entries,
        IReadOnlyList<RejectedEntry> rejected,
        IReadOnlyList<string> collisions)
    {
        ArchivePath = archivePath;
        DestinationRoot = destinationRoot;
        Layout = layout;
        CollisionPolicy = collisionPolicy;
        FolderName = folderName;
        WrapperName = wrapperName;
        Entries = entries;
        Rejected = rejected;
        Collisions = collisions;
    }

    /// <summary>The archive being extracted.</summary>
    public string ArchivePath { get; }

    /// <summary>The name shown for the archive in the preview.</summary>
    public string ArchiveName => Path.GetFileName(ArchivePath);

    /// <summary>The folder the user chose. It may not exist yet; extraction creates it.</summary>
    public string DestinationRoot { get; }

    public UnzipLayout Layout { get; }

    /// <summary>What extraction does about the paths in <see cref="Collisions"/>.</summary>
    public CollisionPolicy CollisionPolicy { get; }

    /// <summary>The single folder created beneath the destination, or <c>null</c> for <c>-noroot</c>.</summary>
    public string? FolderName { get; }

    /// <summary>The archive's own single top-level directory, or <c>null</c> when it has none.</summary>
    public string? WrapperName { get; }

    /// <summary>Whether the archive already carries one wrapper directory around everything.</summary>
    public bool HasWrapper => WrapperName is not null;

    /// <summary>Every entry that will be written, in archive order.</summary>
    public IReadOnlyList<PlannedEntry> Entries { get; }

    /// <summary>Entries refused for safety. The preview shows these rather than hiding them.</summary>
    public IReadOnlyList<RejectedEntry> Rejected { get; }

    /// <summary>Absolute paths that already exist and would be replaced or skipped.</summary>
    public IReadOnlyList<string> Collisions { get; }

    /// <summary>The folder that will contain the extracted content.</summary>
    public string TargetRoot =>
        FolderName is { Length: > 0 } folder ? Path.Combine(DestinationRoot, folder) : DestinationRoot;

    /// <summary>How many files will be written, before the collision policy is applied.</summary>
    public int FileCount => Entries.Count(entry => !entry.IsDirectory);

    /// <summary>How many folders will be created.</summary>
    public int FolderCount => Entries.Count(entry => entry.IsDirectory);

    /// <summary>The total uncompressed size of every file in the plan.</summary>
    public long TotalBytes => Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Length);

    /// <summary>Whether anything at all would be written.</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Returns the same plan with a different layout, folder name, or collision policy.</summary>
    /// <remarks>
    /// The preview re-plans through <see cref="ArchivePlanner"/> rather than mutating, so collisions
    /// are always recomputed against the destination the user is actually looking at.
    /// </remarks>
    public ArchivePlan Rebuild(
        IReadOnlyList<ArchiveEntry> entries,
        UnzipLayout? layout = null,
        CollisionPolicy? collisionPolicy = null,
        string? folderName = null,
        string? destinationRoot = null,
        Func<string, bool>? pathExists = null) =>
        ArchivePlanner.Create(
            ArchivePath,
            destinationRoot ?? DestinationRoot,
            entries,
            layout ?? Layout,
            collisionPolicy ?? CollisionPolicy,
            folderName ?? FolderName,
            pathExists);
}
