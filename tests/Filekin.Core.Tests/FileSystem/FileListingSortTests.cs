using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.FileSystem;

[TestClass]
public sealed class FileListingSortTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DirectoryEntry Dir(string name, DateTime modified) =>
        new(name, $@"D:\{name}", IsDirectory: true, SizeBytes: null, modified);

    private static DirectoryEntry File(string name, long size, DateTime modified) =>
        new(name, $@"D:\{name}", IsDirectory: false, size, modified);

    private static string[] NamesOf(IEnumerable<DirectoryEntry> entries) =>
        entries.Select(e => e.Name).ToArray();

    [TestMethod]
    public void DirectoriesAlwaysGroupBeforeFiles()
    {
        var entries = new[]
        {
            File("apple.txt", 10, Base),
            Dir("zebra", Base),
            File("banana.txt", 20, Base),
            Dir("aardvark", Base),
        };

        var sorted = FileListingSort.Sort(entries, FileSortColumn.Name, descending: false);

        var expected = new[] { "aardvark", "zebra", "apple.txt", "banana.txt" };
        CollectionAssert.AreEqual(expected, NamesOf(sorted));
    }

    [TestMethod]
    public void DirectoriesStayFirstEvenWhenDescending()
    {
        var entries = new[]
        {
            File("apple.txt", 10, Base),
            Dir("zebra", Base),
            Dir("aardvark", Base),
        };

        var sorted = FileListingSort.Sort(entries, FileSortColumn.Name, descending: true);

        // Directories remain grouped ahead of files; only within-group order reverses.
        var expected = new[] { "zebra", "aardvark", "apple.txt" };
        CollectionAssert.AreEqual(expected, NamesOf(sorted));
    }

    [TestMethod]
    public void NameSortIsCaseInsensitive()
    {
        var entries = new[]
        {
            File("Beta.txt", 1, Base),
            File("alpha.txt", 1, Base),
        };

        var sorted = FileListingSort.Sort(entries, FileSortColumn.Name, descending: false);

        var expected = new[] { "alpha.txt", "Beta.txt" };
        CollectionAssert.AreEqual(expected, NamesOf(sorted));
    }

    [TestMethod]
    public void SizeSortOrdersFilesBySizeAscending()
    {
        var entries = new[]
        {
            File("big.bin", 5000, Base),
            File("small.bin", 10, Base),
            File("mid.bin", 500, Base),
        };

        var sorted = FileListingSort.Sort(entries, FileSortColumn.Size, descending: false);

        var expected = new[] { "small.bin", "mid.bin", "big.bin" };
        CollectionAssert.AreEqual(expected, NamesOf(sorted));
    }

    [TestMethod]
    public void ModifiedSortOrdersByTimestamp()
    {
        var entries = new[]
        {
            File("newer.txt", 1, Base.AddDays(2)),
            File("older.txt", 1, Base),
            File("newest.txt", 1, Base.AddDays(5)),
        };

        var sorted = FileListingSort.Sort(entries, FileSortColumn.Modified, descending: true);

        var expected = new[] { "newest.txt", "newer.txt", "older.txt" };
        CollectionAssert.AreEqual(expected, NamesOf(sorted));
    }
}
