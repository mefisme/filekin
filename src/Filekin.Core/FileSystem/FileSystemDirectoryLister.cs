namespace Filekin.Core.FileSystem;

/// <summary>
/// The default <see cref="IDirectoryLister"/> over ordinary .NET filesystem APIs
/// (ENGINEERING-GUARDRAILS.md — use .NET for ordinary filesystem work). It enumerates one directory
/// level and reads each entry's kind, size, and modified time. Entries that vanish or become
/// inaccessible mid-enumeration are skipped so a single unreadable item does not blank the listing;
/// a directory that cannot be enumerated at all propagates its exception to the caller.
///
/// The only entries omitted are protected operating-system items — those marked
/// <see cref="FileAttributes.Hidden"/> and <see cref="FileAttributes.System"/> together ("super-hidden",
/// the combination Explorer keeps out of even its "show hidden items" view unless "hide protected
/// operating system files" is disabled). That is exactly what the legacy Windows profile junctions
/// (Application Data, Cookies, Local Settings, NetHood, PrintHood, Recent, SendTo, Start Menu,
/// Templates) are: reparse points that deny traversal and cannot be opened. Everything Explorer's
/// hidden view would show — including plain hidden items such as AppData (Hidden but not System) and
/// dot-prefixed names (.ssh, .config) — is listed.
/// </summary>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    private const FileAttributes ProtectedOs = FileAttributes.Hidden | FileAttributes.System;

    public IReadOnlyList<DirectoryEntry> List(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = new DirectoryInfo(path);
        var entries = new List<DirectoryEntry>();

        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            var entry = TryDescribe(info);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static DirectoryEntry? TryDescribe(FileSystemInfo info)
    {
        try
        {
            var attributes = info.Attributes;

            // Protected OS items (Hidden+System) are never listed; they deny access and only clutter.
            if ((attributes & ProtectedOs) == ProtectedOs)
            {
                return null;
            }

            var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;
            var size = isDirectory ? (long?)null : ((FileInfo)info).Length;
            return new DirectoryEntry(info.Name, info.FullName, isDirectory, size, info.LastWriteTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The item disappeared or is no longer readable since enumeration began; skip it rather
            // than failing the whole listing.
            return null;
        }
    }
}
