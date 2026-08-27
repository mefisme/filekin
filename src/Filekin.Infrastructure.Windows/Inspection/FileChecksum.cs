using System.Security.Cryptography;

namespace Filekin.Infrastructure.Windows.Inspection;

/// <summary>
/// Hashes a file on request. <c>/info</c> never hashes merely because it opened — a checksum on a
/// multi-gigabyte file is exactly the expensive work the Info sheet defers until asked
/// (ARCHITECTURE.md — Topic 5R, "Optional / On-Demand Information").
/// </summary>
public static class FileChecksum
{
    /// <summary>Computes the SHA-256 of <paramref name="path"/> as lowercase hexadecimal.</summary>
    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
