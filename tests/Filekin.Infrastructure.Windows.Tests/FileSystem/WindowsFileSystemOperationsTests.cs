using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.Infrastructure.Windows.Tests.FileSystem;

[TestClass]
public sealed class WindowsFileSystemOperationsTests
{
    private string _root = null!;
    private WindowsFileSystemOperations _operations = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "filekin-fsops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _operations = new WindowsFileSystemOperations();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked handle should not fail the test run.
        }
    }

    [TestMethod]
    public void GetKindDistinguishesFileDirectoryAndAbsent()
    {
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "x");
        var directory = Path.Combine(_root, "sub");
        Directory.CreateDirectory(directory);

        Assert.AreEqual(FileSystemEntryKind.File, _operations.GetKind(file));
        Assert.AreEqual(FileSystemEntryKind.Directory, _operations.GetKind(directory));
        Assert.AreEqual(FileSystemEntryKind.None, _operations.GetKind(Path.Combine(_root, "missing")));
    }

    [TestMethod]
    public void CopyDuplicatesAFileAndLeavesTheSource()
    {
        var source = Path.Combine(_root, "a.txt");
        var target = Path.Combine(_root, "b.txt");
        File.WriteAllText(source, "hello");

        _operations.Copy(source, target);

        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual("hello", File.ReadAllText(target));
    }

    [TestMethod]
    public void CopyRecursesIntoDirectories()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "top.txt"), "1");
        File.WriteAllText(Path.Combine(source, "nested", "deep.txt"), "2");
        var target = Path.Combine(_root, "dst");

        _operations.Copy(source, target);

        Assert.AreEqual("1", File.ReadAllText(Path.Combine(target, "top.txt")));
        Assert.AreEqual("2", File.ReadAllText(Path.Combine(target, "nested", "deep.txt")));
    }

    [TestMethod]
    public void MoveRelocatesAFile()
    {
        var source = Path.Combine(_root, "a.txt");
        var target = Path.Combine(_root, "moved.txt");
        File.WriteAllText(source, "data");

        _operations.Move(source, target);

        Assert.IsFalse(File.Exists(source));
        Assert.AreEqual("data", File.ReadAllText(target));
    }

    [TestMethod]
    public void RecycleRemovesTheFileFromItsOriginalPath()
    {
        var file = Path.Combine(_root, "trash.txt");
        File.WriteAllText(file, "bye");

        _operations.Recycle(file);

        // The file leaves its original location whether it was recycled or, where the Recycle Bin is
        // unavailable, deleted outright; both satisfy the app-owned delete contract.
        Assert.IsFalse(File.Exists(file));
    }

    [TestMethod]
    public void RecycleBinIdentityDistinguishesDuplicateOriginalPaths()
    {
        var target = new RecycledItem(
            "same.txt",
            @"D:\Work\same.txt",
            DateTime.Now,
            SizeBytes: 1,
            IsDirectory: false,
            RecycleBinIdentity: @"D:\$Recycle.Bin\$RNEW.txt");

        Assert.IsFalse(WindowsRecycleBin.MatchesTarget(
            @"D:\$Recycle.Bin\$ROLD.txt",
            @"D:\Work\same.txt",
            target));
        Assert.IsTrue(WindowsRecycleBin.MatchesTarget(
            @"D:\$Recycle.Bin\$RNEW.txt",
            @"D:\Work\same.txt",
            target));
    }

    [TestMethod]
    public void ExactNewRecycleItemIgnoresOlderDuplicateOriginalPath()
    {
        var originalPath = @"D:\Work\same.txt";
        var oldItem = Recycled(originalPath, @"D:\$Recycle.Bin\$ROLD.txt");
        var newItem = Recycled(originalPath, @"D:\$Recycle.Bin\$RNEW.txt");

        var selected = WindowsFileSystemOperations.FindExactNewItem(
            [oldItem],
            [oldItem, newItem],
            originalPath);

        Assert.AreEqual(newItem, selected);
    }

    [TestMethod]
    public void AmbiguousNewRecycleItemsAreNotClaimedAsRestorable()
    {
        var originalPath = @"D:\Work\same.txt";

        var selected = WindowsFileSystemOperations.FindExactNewItem(
            [],
            [
                Recycled(originalPath, @"D:\$Recycle.Bin\$RONE.txt"),
                Recycled(originalPath, @"D:\$Recycle.Bin\$RTWO.txt"),
            ],
            originalPath);

        Assert.IsNull(selected);
    }

    private static RecycledItem Recycled(string originalPath, string identity) => new(
        Path.GetFileName(originalPath),
        originalPath,
        DateTime.Now,
        SizeBytes: 1,
        IsDirectory: false,
        RecycleBinIdentity: identity);
}
