namespace Filekin.Core.FileSystem;

/// <summary>The column a Files listing is ordered by (DECISIONS.md, 2026-08-25 — "Files Hierarchy Sorts by Clicking Column Headers").</summary>
public enum FileSortColumn
{
    Type,
    Name,
    Modified,
    Size,
}

/// <summary>
/// Orders a directory listing for display. Directories always group before files regardless of the
/// active column (DECISIONS.md, 2026-08-25); within each group the entries are ordered by the chosen
/// column, and re-clicking a header reverses that within-group direction. Name is the tie-breaker and
/// uses a case-insensitive ordinal comparison so the order is stable and culture-independent.
/// </summary>
public static class FileListingSort
{
    public static IReadOnlyList<DirectoryEntry> Sort(
        IEnumerable<DirectoryEntry> entries,
        FileSortColumn column,
        bool descending)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var comparer = ColumnComparer(column, descending);

        return entries
            .OrderByDescending(static e => e.IsDirectory)
            .ThenBy(static e => e, comparer)
            .ToList();
    }

    private static Comparer<DirectoryEntry> ColumnComparer(FileSortColumn column, bool descending)
    {
        Comparison<DirectoryEntry> comparison = column switch
        {
            FileSortColumn.Name => CompareByName,
            FileSortColumn.Type => CompareByType,
            FileSortColumn.Modified => CompareByModified,
            FileSortColumn.Size => CompareBySize,
            _ => CompareByName,
        };

        return Comparer<DirectoryEntry>.Create(descending
            ? (a, b) => comparison(b, a)
            : comparison);
    }

    private static int CompareByName(DirectoryEntry a, DirectoryEntry b) =>
        string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private static int CompareByType(DirectoryEntry a, DirectoryEntry b)
    {
        var byCode = string.Compare(FileTypeCode.ForEntry(a), FileTypeCode.ForEntry(b), StringComparison.OrdinalIgnoreCase);
        return byCode != 0 ? byCode : CompareByName(a, b);
    }

    private static int CompareByModified(DirectoryEntry a, DirectoryEntry b)
    {
        var byTime = a.LastModified.CompareTo(b.LastModified);
        return byTime != 0 ? byTime : CompareByName(a, b);
    }

    private static int CompareBySize(DirectoryEntry a, DirectoryEntry b)
    {
        var bySize = Nullable.Compare(a.SizeBytes, b.SizeBytes);
        return bySize != 0 ? bySize : CompareByName(a, b);
    }
}
