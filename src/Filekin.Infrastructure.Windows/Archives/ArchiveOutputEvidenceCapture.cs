using System.Security.Cryptography;
using Filekin.Core.Archives;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>Captures immutable completion evidence while denying concurrent writers during hashing.</summary>
internal static class ArchiveOutputEvidenceCapture
{
    internal static async Task<ArchiveOutputEvidence> CaptureAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
            {
                return ArchiveOutputEvidence.Absent(path);
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var length = stream.Length;
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            var hash = await SHA256.HashDataAsync(stream, CancellationToken.None).ConfigureAwait(false);
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
