namespace Filekin.Core.FileSystem;

/// <summary>
/// Reads the immediate contents of a filesystem directory. The call is synchronous and does real I/O,
/// so callers on the UI thread must offload it (the Files view model runs it on a background thread —
/// DECISIONS.md, 2026-08-24, "UI Thread Must Remain Responsive"). Enumeration returns every readable
/// entry and skips items it cannot stat rather than failing the whole listing; a directory that cannot
/// be opened at all still throws so the caller can report it.
/// </summary>
public interface IDirectoryLister
{
    /// <summary>
    /// Lists the files and folders directly inside <paramref name="path"/> (no recursion). The result
    /// is unsorted; ordering is a presentation concern owned by the caller.
    /// </summary>
    IReadOnlyList<DirectoryEntry> List(string path);
}
