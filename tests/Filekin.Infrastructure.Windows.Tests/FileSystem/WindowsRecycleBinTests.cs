using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.Infrastructure.Windows.Tests.FileSystem;

// These tests drive the real Windows Recycle Bin through the shell.
//
// They must not run at the same time as each other: the assembly enables method-level parallelism,
// which would race two tests on the one shared bin.
//
// They also need an interactive Windows session. WindowsRecycleBin reads the bin through
// Shell.Application, and on a hosted CI runner a recycled file never reaches the bin, so the round
// trip cannot be verified there. The RequiresInteractiveShell category is what CI excludes; do not
// weaken the assertions to make them pass in an environment that cannot support them.
[TestClass]
[DoNotParallelize]
[TestCategory(RequiresInteractiveShell)]
public sealed class WindowsRecycleBinTests
{
    /// <summary>Tests that need a real interactive Windows shell; CI filters these out.</summary>
    public const string RequiresInteractiveShell = "RequiresInteractiveShell";

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
            var recycleOutcome = operations.Recycle(file);
            Assert.IsFalse(File.Exists(file), "The file should be gone from its original path after recycling.");
            var listed = recycleBin.List();
            var samePathItems = listed.Where(i =>
                string.Equals(i.OriginalPath, file, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.IsTrue(
                recycleOutcome.CanRestore,
                $"{recycleOutcome.RestoreUnavailableReason} Matching listed items: {samePathItems.Length}; " +
                $"identified: {samePathItems.Count(i => !string.IsNullOrWhiteSpace(i.RecycleBinIdentity))}.");

            var item = recycleOutcome.RecycledItem!;
            Assert.IsTrue(
                listed.Any(i => i.RecycleBinIdentity == item.RecycleBinIdentity),
                "The exact item returned by the delete callback should appear in the Recycle Bin.");
            Assert.IsFalse(item.IsDirectory);

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
