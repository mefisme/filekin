namespace Filekin.Core.Archives;

/// <summary>
/// What extraction does when a file it wants to write is already there.
///
/// Neither value is hardcoded as "the" default. The owner extracts over existing files most of the
/// time, but that is a preference rather than a safe universal, so the default lives in Settings and
/// both switches exist to override it for one command (owner decision, 2026-08-27). Filekin ships
/// with <see cref="Skip"/> so someone who never opens Settings cannot lose a file by accident, and
/// the longer word — <c>-overwrite</c> — is the one that replaces data.
///
/// <see cref="Overwrite"/> is survivable because a replaced file is not destroyed: the original goes
/// to the Recycle Bin first, which is what lets the whole extraction be undone afterwards.
/// </summary>
public enum CollisionPolicy
{
    /// <summary>Leave the existing file alone and do not write the archive's copy. The <c>-skip</c> switch.</summary>
    Skip,

    /// <summary>Replace it, sending the original to the Recycle Bin first. The <c>-overwrite</c> switch.</summary>
    Overwrite,
}
