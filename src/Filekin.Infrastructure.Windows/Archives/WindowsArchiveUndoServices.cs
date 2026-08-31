using System.Security.Cryptography;
using Filekin.Core.Archives;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>Reads a stable current SHA-256 fingerprint while denying concurrent writers.</summary>
public sealed class WindowsArchiveOutputEvidenceReader : IArchiveOutputEvidenceReader
{
    public ArchiveOutputEvidence Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
            {
                return ArchiveOutputEvidence.Absent(path);
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            var length = stream.Length;
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            var hash = SHA256.HashData(stream);
            return ArchiveOutputEvidence.Captured(
                path,
                length,
                lastWriteTimeUtc,
                Convert.ToHexString(hash));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return ArchiveOutputEvidence.Unavailable(
                path,
                $"Could not capture Undo safety evidence: {ex.Message}");
        }
    }
}

/// <summary>Standard filesystem removals behind the platform-neutral archive Undo port.</summary>
public sealed class WindowsArchiveUndoStorage : IArchiveUndoStorage
{
    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(path);
    }

    public ArchiveDirectoryRemoval RemoveDirectoryIfEmpty(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            return ArchiveDirectoryRemoval.Missing;
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            return ArchiveDirectoryRemoval.NotEmpty;
        }

        Directory.Delete(path);
        return ArchiveDirectoryRemoval.Removed;
    }
}
