using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.FileSystem;

[TestClass]
public sealed class FileSystemDirectoryListerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTempTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "filekin-lister-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "b.md"), "# world");
    }

    [TestCleanup]
    public void RemoveTempTree()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void ListsDirectoriesAndFilesWithTheirKindAndSize()
    {
        var lister = new FileSystemDirectoryLister();

        var entries = lister.List(_root);

        Assert.AreEqual(3, entries.Count);

        var sub = entries.Single(e => e.Name == "sub");
        Assert.IsTrue(sub.IsDirectory);
        Assert.IsNull(sub.SizeBytes);

        var text = entries.Single(e => e.Name == "a.txt");
        Assert.IsFalse(text.IsDirectory);
        Assert.AreEqual(5, text.SizeBytes);
        Assert.AreEqual(Path.Combine(_root, "a.txt"), text.FullPath);
    }

    [TestMethod]
    public void ListingIsNotRecursive()
    {
        File.WriteAllText(Path.Combine(_root, "sub", "deep.txt"), "x");
        var lister = new FileSystemDirectoryLister();

        var entries = lister.List(_root);

        Assert.IsFalse(entries.Any(e => e.Name == "deep.txt"));
    }
}
