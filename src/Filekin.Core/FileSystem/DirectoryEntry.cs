namespace Filekin.Core.FileSystem;

/// <summary>
/// One item in a directory listing, as read from the filesystem: enough to draw a Files row and to
/// sort the listing, with no WPF or presentation concern attached (DECISIONS.md, 2026-08-24 — keep
/// core logic separated from WPF). <see cref="SizeBytes"/> is <c>null</c> for directories, whose size
/// is not computed during enumeration.
/// </summary>
public sealed record DirectoryEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? SizeBytes,
    DateTime LastModified);
