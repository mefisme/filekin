namespace Filekin.Core.Archives;

/// <summary>
/// One entry as the archive itself names it, before Filekin decides where it lands.
///
/// <see cref="Path"/> is the raw in-archive name and keeps the archive's own forward slashes. It is
/// never trusted as a filesystem path: <see cref="ArchivePlanner"/> validates and rewrites it, which
/// is where archive path traversal is stopped (ARCHITECTURE.md — Security Considerations).
/// </summary>
/// <param name="Path">The raw in-archive entry name, forward-slash separated.</param>
/// <param name="Length">The uncompressed size in bytes. Zero for a directory entry.</param>
/// <param name="CompressedLength">The stored size in bytes.</param>
/// <param name="LastWriteTime">The timestamp recorded in the archive.</param>
/// <param name="IsDirectory">Whether the entry is an explicit directory marker rather than a file.</param>
public readonly record struct ArchiveEntry(
    string Path,
    long Length,
    long CompressedLength,
    DateTimeOffset LastWriteTime,
    bool IsDirectory);
