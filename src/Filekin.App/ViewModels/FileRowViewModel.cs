using System;
using Filekin.Core.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>
/// One Files listing row. Immutable: the listing is rebuilt on navigation and re-sort, so a row never
/// mutates in place. Exposes the display strings the <c>FileRowItem</c> template binds
/// (<see cref="TypeCode"/>, <see cref="Name"/>, <see cref="Modified"/>, <see cref="Size"/>,
/// <see cref="IsDirectory"/>) and keeps the underlying <see cref="Entry"/> for navigation and selection.
/// </summary>
public sealed class FileRowViewModel
{
    public FileRowViewModel(DirectoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        TypeCode = FileTypeCode.ForEntry(entry);
        Modified = entry.LastModified.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        Size = FormatSize(entry.SizeBytes);
    }

    public DirectoryEntry Entry { get; }

    public string TypeCode { get; }

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public string FullPath => Entry.FullPath;

    public string Modified { get; }

    public string Size { get; }

    private static string FormatSize(long? bytes)
    {
        if (bytes is not { } value)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var rounded = unit == 0 ? value.ToString(System.Globalization.CultureInfo.CurrentCulture) : size.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
        return $"{rounded} {units[unit]}";
    }
}
