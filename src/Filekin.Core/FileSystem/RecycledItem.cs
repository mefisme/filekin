namespace Filekin.Core.FileSystem;

/// <summary>
/// One entry in the Recycle Bin, as read from the shell: enough to show it in the <c>/recycle</c> view
/// and to identify it for restore. <see cref="OriginalPath"/> is the full path the item was deleted
/// from (including its name). <see cref="RecycleBinIdentity"/> is an opaque, platform-owned identity
/// used internally to distinguish entries that share the same original path; it must not be shown as
/// a filesystem location. <see cref="SizeBytes"/> is <c>null</c> for directories.
/// </summary>
public sealed record RecycledItem(
    string Name,
    string OriginalPath,
    DateTime? DeletedWhen,
    long? SizeBytes,
    bool IsDirectory,
    string? RecycleBinIdentity = null);
