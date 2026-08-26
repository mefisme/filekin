namespace Filekin.Core.FileSystem;

/// <summary>
/// One entry in the Recycle Bin, as read from the shell: enough to show it in the <c>/recycle</c> view
/// and to identify it for restore. <see cref="OriginalPath"/> is the full path the item was deleted
/// from (including its name) and, together with <see cref="DeletedWhen"/>, identifies which entry to
/// restore. <see cref="SizeBytes"/> is <c>null</c> for directories.
/// </summary>
public sealed record RecycledItem(
    string Name,
    string OriginalPath,
    DateTime? DeletedWhen,
    long? SizeBytes,
    bool IsDirectory);
