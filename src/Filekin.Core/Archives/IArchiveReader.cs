namespace Filekin.Core.Archives;

/// <summary>
/// Reads an archive's table of contents without extracting anything, so <c>/unzip</c> can show a
/// preview immediately. Implementations read only the archive's own index, never the compressed
/// payload, which is what keeps the preview fast for a large archive.
/// </summary>
public interface IArchiveReader
{
    /// <summary>Whether <paramref name="path"/> names an archive format this reader understands.</summary>
    bool CanRead(string path);

    /// <summary>
    /// Lists every entry in <paramref name="path"/>.
    /// </summary>
    /// <exception cref="ArchiveReadException">The file is missing, unreadable, or not a valid archive.</exception>
    IReadOnlyList<ArchiveEntry> ReadEntries(string path, CancellationToken cancellationToken = default);
}

/// <summary>An archive could not be opened or its index could not be read.</summary>
public sealed class ArchiveReadException : Exception
{
    public ArchiveReadException(string message)
        : base(message)
    {
    }

    public ArchiveReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ArchiveReadException()
    {
    }
}
