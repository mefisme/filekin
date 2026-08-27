namespace Filekin.Core.Archives;

/// <summary>
/// Where the extracted files land inside the destination folder.
///
/// This one control covers both of the owner's requirements — choose the destination, and choose
/// whether the archive's own wrapper folder is kept or removed (owner decision, 2026-08-27) —
/// because both are the same axis: how many folders sit between the destination and the files.
/// </summary>
public enum UnzipLayout
{
    /// <summary>
    /// The default. Everything lands in exactly one folder inside the destination. When the archive
    /// already carries a single wrapper directory, that wrapper <em>is</em> the folder and no second
    /// one is added, which is the redundant-nesting rule in PRODUCT.md.
    /// </summary>
    NewFolder,

    /// <summary>
    /// The <c>-noroot</c> switch. Files land directly in the destination with no folder of their
    /// own, and an archive's wrapper directory is stripped rather than reused.
    /// </summary>
    NoRoot,
}
