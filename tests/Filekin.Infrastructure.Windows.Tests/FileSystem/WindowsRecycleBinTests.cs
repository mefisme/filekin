using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.Infrastructure.Windows.Tests.FileSystem;

// These tests drive the real Windows Recycle Bin through the shell, so they must not run at the same
// time as each other — the assembly enables method-level parallelism, which would race two tests on the
// one shared bin.
[TestClass]
[DoNotParallelize]
public sealed class WindowsRecycleBinTests
{
    [TestMethod]
    public void RecycledFileAppearsInTheBinAndCanBeRestored()
    {
        var directory = Path.Combine(Path.GetTempPath(), "filekin-recyclebin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "restore-me.txt");
        File.WriteAllText(file, "hello");

        var operations = new WindowsFileSystemOperations();
        var recycleBin = new WindowsRecycleBin();

        try
        {
            operations.Recycle(file);
            Assert.IsFalse(File.Exists(file), "The file should be gone from its original path after recycling.");

            var listed = recycleBin.List();
            RequireObservableRecycleBin(listed);

            var item = listed.FirstOrDefault(i =>
                string.Equals(i.OriginalPath, file, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(item, "The recycled file should appear in the Recycle Bin.");
            Assert.IsFalse(item!.IsDirectory);

            var restored = recycleBin.Restore(item);
            Assert.IsTrue(restored, "Restore should report success.");
            Assert.IsTrue(File.Exists(file), "The file should be back at its original path after restore.");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [TestMethod]
    public void DeleteForeverRemovesOnlyTheTargetedItemFromTheBin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "filekin-recyclebin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "delete-me-forever.txt");
        File.WriteAllText(file, "goodbye");

        var operations = new WindowsFileSystemOperations();
        var recycleBin = new WindowsRecycleBin();

        try
        {
            operations.Recycle(file);

            var listed = recycleBin.List();
            RequireObservableRecycleBin(listed);

            var item = listed.FirstOrDefault(i =>
                string.Equals(i.OriginalPath, file, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(item, "The recycled file should appear in the Recycle Bin.");

            var deleted = recycleBin.DeleteForever(item!);
            Assert.IsTrue(deleted, "DeleteForever should report success.");

            var stillThere = recycleBin.List().Any(i =>
                string.Equals(i.OriginalPath, file, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(stillThere, "The item should be gone from the Recycle Bin after DeleteForever.");
            Assert.IsFalse(File.Exists(file), "The item must not have been restored to its original path.");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Skips the test when this environment cannot enumerate the shell Recycle Bin at all.
    /// </summary>
    /// <remarks>
    /// <see cref="WindowsRecycleBin"/> reads the bin through <c>Shell.Application</c>, which needs an
    /// interactive Windows shell. A hosted CI runner has none, so enumeration yields nothing and the
    /// assertions below would fail for a reason that has nothing to do with this code.
    ///
    /// The check is deliberately narrow: the caller has just recycled a file, so a working bin cannot
    /// be empty. An empty list therefore means "not observable here", while a populated list that is
    /// missing the recycled item is a genuine failure and still fails.
    /// </remarks>
    private static void RequireObservableRecycleBin(IReadOnlyCollection<RecycledItem> listed)
    {
        if (listed.Count == 0)
        {
            Assert.Inconclusive(
                "The shell Recycle Bin is not observable in this environment (no interactive Windows "
                + "shell), so real Recycle Bin behaviour cannot be verified here. Run on a desktop.");
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temp directory.
        }
    }
}
