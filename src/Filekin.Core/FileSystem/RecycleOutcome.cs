namespace Filekin.Core.FileSystem;

/// <summary>
/// The authoritative result of one successful app-owned Recycle Bin operation. Windows can honor a
/// delete while declining or being unable to create a recoverable item. In that case the original
/// mutation is still real, but Filekin must retain the reason and never promise Restore.
/// </summary>
public sealed record RecycleOutcome
{
    private RecycleOutcome(
        string originalPath,
        FileSystemEntryKind entryKind,
        RecycledItem? recycledItem,
        string? restoreUnavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (recycledItem is not null &&
            !string.Equals(originalPath, recycledItem.OriginalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The recycled item must belong to the reported original path.",
                nameof(recycledItem));
        }

        var hasIdentity = !string.IsNullOrWhiteSpace(recycledItem?.RecycleBinIdentity);
        if (hasIdentity == !string.IsNullOrWhiteSpace(restoreUnavailableReason))
        {
            throw new ArgumentException(
                "A recycle outcome must contain either an exact item identity or an unavailable reason.");
        }

        OriginalPath = originalPath;
        EntryKind = entryKind;
        RecycledItem = recycledItem;
        RestoreUnavailableReason = restoreUnavailableReason;
    }

    public string OriginalPath { get; }

    public FileSystemEntryKind EntryKind { get; }

    public RecycledItem? RecycledItem { get; }

    public string? RestoreUnavailableReason { get; }

    public bool CanRestore => RecycledItem is not null;

    public static RecycleOutcome Restorable(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.RecycleBinIdentity))
        {
            throw new ArgumentException("A restorable item requires an exact Recycle Bin identity.", nameof(item));
        }

        return new RecycleOutcome(
            item.OriginalPath,
            item.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File,
            item,
            restoreUnavailableReason: null);
    }

    public static RecycleOutcome Informational(
        string originalPath,
        FileSystemEntryKind entryKind,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (entryKind == FileSystemEntryKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(entryKind), "A successful recycle needs an entry kind.");
        }

        return new RecycleOutcome(originalPath, entryKind, recycledItem: null, reason);
    }
}
