using Filekin.Core.FileSystem;

namespace Filekin.Core.Archives;

/// <summary>
/// What Filekin knew about one archive output when its operation ended. A SHA-256 fingerprint makes
/// same-size or timestamp-preserving edits detectable later. <see cref="ExistedAtCompletion"/> is
/// <c>false</c> for a path recorded before a failed write that left no file, and <c>null</c> only when
/// the evidence itself could not be captured.
/// </summary>
public sealed record ArchiveOutputEvidence
{
    public ArchiveOutputEvidence(
        string path,
        bool? existedAtCompletion,
        long? length,
        DateTime? lastWriteTimeUtc,
        string? sha256,
        string? unavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (existedAtCompletion == true &&
            (length is null || lastWriteTimeUtc is null || string.IsNullOrWhiteSpace(sha256) ||
             !string.IsNullOrWhiteSpace(unavailableReason)))
        {
            throw new ArgumentException("An existing archive output requires complete fingerprint evidence.");
        }

        if (existedAtCompletion == false &&
            (length is not null || lastWriteTimeUtc is not null || sha256 is not null || unavailableReason is not null))
        {
            throw new ArgumentException("An absent archive output cannot contain fingerprint evidence.");
        }

        if (existedAtCompletion is null &&
            (length is not null || lastWriteTimeUtc is not null || sha256 is not null ||
             string.IsNullOrWhiteSpace(unavailableReason)))
        {
            throw new ArgumentException(
                "Unavailable archive output evidence requires a reason and cannot contain a partial fingerprint.");
        }

        Path = path;
        ExistedAtCompletion = existedAtCompletion;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Sha256 = sha256;
        UnavailableReason = unavailableReason;
    }

    public string Path { get; }

    public bool? ExistedAtCompletion { get; }

    public long? Length { get; }

    public DateTime? LastWriteTimeUtc { get; }

    public string? Sha256 { get; }

    public string? UnavailableReason { get; }

    public bool CanVerify => ExistedAtCompletion is not null;

    public static ArchiveOutputEvidence Captured(
        string path,
        long length,
        DateTime lastWriteTimeUtc,
        string sha256) =>
        new(path, true, length, lastWriteTimeUtc, sha256, unavailableReason: null);

    public static ArchiveOutputEvidence Absent(string path) =>
        new(path, false, length: null, lastWriteTimeUtc: null, sha256: null, unavailableReason: null);

    public static ArchiveOutputEvidence Unavailable(string path, string reason) =>
        new(path, existedAtCompletion: null, length: null, lastWriteTimeUtc: null, sha256: null, reason);
}

/// <summary>
/// Exact Restore evidence for one original that an archive Overwrite sent to the Recycle Bin. The
/// backing identity is opaque internal data and must never be presented as a filesystem path.
/// </summary>
public sealed record ArchiveReplacementEvidence
{
    public ArchiveReplacementEvidence(
        string originalPath,
        RecycledItem? recycledItem,
        string? restoreUnavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (recycledItem is not null &&
            !string.Equals(recycledItem.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Replacement evidence must belong to its original path.", nameof(recycledItem));
        }

        var hasIdentity = !string.IsNullOrWhiteSpace(recycledItem?.RecycleBinIdentity);
        if (hasIdentity == !string.IsNullOrWhiteSpace(restoreUnavailableReason))
        {
            throw new ArgumentException(
                "Replacement evidence requires either an exact identity or an unavailable reason.");
        }

        OriginalPath = originalPath;
        RecycledItem = recycledItem;
        RestoreUnavailableReason = restoreUnavailableReason;
    }

    public string OriginalPath { get; }

    public RecycledItem? RecycledItem { get; }

    public string? RestoreUnavailableReason { get; }

    public bool CanRestore => RecycledItem is not null;

    public static ArchiveReplacementEvidence FromRecycleOutcome(RecycleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new ArchiveReplacementEvidence(
            outcome.OriginalPath,
            outcome.RecycledItem,
            outcome.RestoreUnavailableReason);
    }
}
