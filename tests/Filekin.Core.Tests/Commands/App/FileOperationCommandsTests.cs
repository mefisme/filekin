using Filekin.Core.Commands.App;
using Filekin.Core.FileSystem;
using Filekin.Core.Shell;

namespace Filekin.Core.Tests.Commands.App;

[TestClass]
public sealed class FileOperationCommandsTests
{
    private static readonly ShellLocation Work = new(@"D:\Work", "FileSystem", @"D:\Work");
    private static readonly ShellLocation Registry = new(@"HKLM:\", "Registry", fileSystemPath: null);
    private static readonly string[] PartialMoveTargets =
        [@"D:\Work\out\a.txt", @"D:\Work\out\c.txt"];
    private static readonly string[] TossedATarget = [@"D:\Work\a.txt"];
    private static readonly string[] CopiedBTarget = [@"D:\Work\out\b.txt"];

    [TestMethod]
    public async Task CopyResolvesRelativePathsAgainstTheCurrentLocationAndCopies()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt b.txt", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(1, fs.Copies.Count);
        Assert.AreEqual((@"D:\Work\a.txt", @"D:\Work\b.txt"), fs.Copies[0]);
        Assert.AreEqual(1, result.AffectedPaths.Count);
        Assert.AreEqual(@"D:\Work\b.txt", result.AffectedPaths[0]);
    }

    [TestMethod]
    public async Task CopyIntoAnExistingDirectoryKeepsTheSourceName()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddDirectory(@"D:\Work\out");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt out", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual((@"D:\Work\a.txt", @"D:\Work\out\a.txt"), fs.Copies[0]);
    }

    [TestMethod]
    public async Task CopyRefusesToOverwriteAnExistingTarget()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt b.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.AreEqual(0, fs.Copies.Count);
    }

    [TestMethod]
    public async Task CopyReportsAMissingSource()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy ghost.txt b.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "Source not found");
    }

    [TestMethod]
    public async Task CopyRequiresTwoArguments()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "Usage:");
        Assert.AreEqual(0, fs.Copies.Count);
    }

    [TestMethod]
    public async Task MoveMovesAResolvedTarget()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/move a.txt archive\\a.txt", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual((@"D:\Work\a.txt", @"D:\Work\archive\a.txt"), fs.Moves[0]);
        Assert.HasCount(1, result.Relocations);
        Assert.AreEqual(@"D:\Work\a.txt", result.Relocations[0].SourcePath);
        Assert.AreEqual(@"D:\Work\archive\a.txt", result.Relocations[0].DestinationPath);
    }

    [TestMethod]
    public async Task RenameMovesToASiblingName()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/rename a.txt b.txt", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual((@"D:\Work\a.txt", @"D:\Work\b.txt"), fs.Moves[0]);
        Assert.HasCount(1, result.Relocations);
        Assert.AreEqual(@"D:\Work\a.txt", result.Relocations[0].SourcePath);
        Assert.AreEqual(@"D:\Work\b.txt", result.Relocations[0].DestinationPath);
    }

    [TestMethod]
    public async Task RenameRejectsANewNameThatIsAPath()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/rename a.txt sub\\b.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.AreEqual(0, fs.Moves.Count);
    }

    [TestMethod]
    public async Task DeleteRecyclesAResolvedTarget()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss a.txt", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(1, fs.Recycled.Count);
        Assert.AreEqual(@"D:\Work\a.txt", fs.Recycled[0]);
        Assert.HasCount(1, result.RecycleOutcomes);
        Assert.IsTrue(result.RecycleOutcomes[0].CanRestore);
    }

    [TestMethod]
    public async Task DeleteRecyclesEveryTargetInASelectionBatch()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        fs.AddFile(@"D:\Work\c.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss 'D:\\Work\\a.txt' 'D:\\Work\\b.txt' 'D:\\Work\\c.txt'", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(3, fs.Recycled.Count);
        StringAssert.Contains(result.Message, "3 items");
    }

    [TestMethod]
    public async Task TheUsageLineAdvertisesThatSeveralSourcesAreAccepted()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        foreach (var name in new[] { "copy", "move" })
        {
            var result = await dispatcher.DispatchAsync($"/{name}", Work);

            Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
            // The command has always taken several sources; the help line used to hide that.
            StringAssert.Contains(result.Message, "[<source>", $"/{name}");
        }
    }

    [TestMethod]
    public async Task AFailedMoveDoesNotStopLaterTargetsAndReportsThePartialResult()
    {
        var fs = new FakeFileSystemOperations { FailMoveOn = @"D:\Work\b.txt" };
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        fs.AddFile(@"D:\Work\c.txt");
        fs.AddDirectory(@"D:\Work\out");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync(
            @"/move 'D:\Work\a.txt' 'D:\Work\b.txt' 'D:\Work\c.txt' out",
            Work);

        Assert.AreEqual(AppCommandOutcome.PartialSuccess, result.Outcome);
        Assert.IsFalse(result.Succeeded);
        CollectionAssert.AreEqual(
            PartialMoveTargets,
            result.AffectedPaths.ToArray());
        Assert.HasCount(2, result.Relocations);
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual(@"D:\Work\b.txt", result.Failures[0].Target);
        StringAssert.Contains(result.Message, "2 moved · 1 failed");
        Assert.HasCount(2, fs.Moves);
        Assert.AreEqual(@"D:\Work\c.txt", fs.Moves[1].Source);
        Assert.IsTrue(result.TouchedFileSystem);
    }

    [TestMethod]
    public async Task AUsageErrorDoesNotAskFilesToRefresh()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/move", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.IsFalse(result.TouchedFileSystem);
    }

    [TestMethod]
    public async Task TrashAndDeleteAreTheSameRecoverableOperationAsToss()
    {
        foreach (var alias in new[] { "/trash", "/delete" })
        {
            var fs = new FakeFileSystemOperations();
            fs.AddFile(@"D:\Work\a.txt");
            var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

            var result = await dispatcher.DispatchAsync($"{alias} a.txt", Work);

            Assert.IsTrue(result.Succeeded, $"{alias}: {result.Message}");
            Assert.AreEqual(1, fs.Recycled.Count, alias);
            Assert.AreEqual(@"D:\Work\a.txt", fs.Recycled[0]);
            StringAssert.Contains(result.Message, "Recycle Bin");
        }
    }

    [TestMethod]
    public async Task ADeleteAliasReportsItsOwnNameInTheUsageError()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/delete", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "Usage: /delete");
    }

    [TestMethod]
    public async Task DeleteRecyclesValidTargetsWhenAnotherTargetIsMissing()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss a.txt ghost.txt", Work);

        Assert.AreEqual(AppCommandOutcome.PartialSuccess, result.Outcome);
        CollectionAssert.AreEqual(TossedATarget, result.AffectedPaths.ToArray());
        Assert.HasCount(1, result.Failures);
        Assert.HasCount(1, result.RecycleOutcomes);
        Assert.AreEqual(@"D:\Work\ghost.txt", result.Failures[0].Target);
        StringAssert.Contains(result.Failures[0].Message, "Target not found");
        StringAssert.Contains(result.Message, "1 moved to the Recycle Bin · 1 failed");
        Assert.AreEqual(1, fs.Recycled.Count);
        Assert.IsTrue(result.TouchedFileSystem);
    }

    [TestMethod]
    public async Task CopyMovesEverySourceIntoADestinationDirectory()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        fs.AddDirectory(@"D:\Work\out");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt b.txt out", Work);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(2, fs.Copies.Count);
        Assert.AreEqual((@"D:\Work\a.txt", @"D:\Work\out\a.txt"), fs.Copies[0]);
        Assert.AreEqual((@"D:\Work\b.txt", @"D:\Work\out\b.txt"), fs.Copies[1]);
    }

    [TestMethod]
    public async Task CopySkipsACollidingTargetAndCopiesTheOtherSources()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        fs.AddDirectory(@"D:\Work\out");
        fs.AddFile(@"D:\Work\out\a.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt b.txt out", Work);

        Assert.AreEqual(AppCommandOutcome.PartialSuccess, result.Outcome);
        CollectionAssert.AreEqual(CopiedBTarget, result.AffectedPaths.ToArray());
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual(@"D:\Work\a.txt", result.Failures[0].Target);
        StringAssert.Contains(result.Failures[0].Message, "Destination already exists");
        Assert.AreEqual((@"D:\Work\b.txt", @"D:\Work\out\b.txt"), fs.Copies.Single());
    }

    [TestMethod]
    public async Task ACompletelyInvalidBatchReportsEveryFailureWithoutClaimingAWrite()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddDirectory(@"D:\Work\out");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/move ghost.txt missing.txt out", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.HasCount(2, result.Failures);
        Assert.IsEmpty(result.AffectedPaths);
        StringAssert.Contains(result.Message, "0 moved · 2 failed");
        Assert.IsFalse(result.TouchedFileSystem);
    }

    [TestMethod]
    public async Task APlatformFailureOnEveryRecycleTargetStillAttemptsTheWholeBatchAndRefreshes()
    {
        var fs = new FakeFileSystemOperations { FailEveryRecycle = true };
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss a.txt b.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.HasCount(2, result.Failures);
        Assert.HasCount(2, fs.RecycleAttempts);
        Assert.IsEmpty(result.AffectedPaths);
        Assert.IsTrue(result.TouchedFileSystem);
    }

    [TestMethod]
    public async Task CopyingMultipleSourcesRequiresADirectoryDestination()
    {
        var fs = new FakeFileSystemOperations();
        fs.AddFile(@"D:\Work\a.txt");
        fs.AddFile(@"D:\Work\b.txt");
        fs.AddFile(@"D:\Work\c.txt");
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/copy a.txt b.txt c.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "existing folder");
        Assert.AreEqual(0, fs.Copies.Count);
    }

    [TestMethod]
    public async Task DeleteReportsAMissingTarget()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss ghost.txt", Work);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        Assert.AreEqual(0, fs.Recycled.Count);
    }

    [TestMethod]
    public async Task FileOperationsRefuseANonFilesystemLocation()
    {
        var fs = new FakeFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(fs);

        var result = await dispatcher.DispatchAsync("/toss something", Registry);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "filesystem location");
    }

    private sealed class FakeFileSystemOperations : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Source, string Destination)> Copies { get; } = [];

        public List<(string Source, string Destination)> Moves { get; } = [];

        public List<string> Recycled { get; } = [];

        public List<string> RecycleAttempts { get; } = [];

        public void AddFile(string path) => _entries[path] = FileSystemEntryKind.File;

        public void AddDirectory(string path) => _entries[path] = FileSystemEntryKind.Directory;

        public FileSystemEntryKind GetKind(string path) =>
            _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.None;

        public List<string> Directories { get; } = [];

        public void CreateDirectory(string path) => Directories.Add(path);

        public void Copy(string sourcePath, string destinationPath) => Copies.Add((sourcePath, destinationPath));

        public string? FailMoveOn { get; init; }

        public void Move(string sourcePath, string destinationPath)
        {
            if (FailMoveOn is not null &&
                sourcePath.Equals(FailMoveOn, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"{sourcePath} is in use.");
            }

            Moves.Add((sourcePath, destinationPath));
        }

        public bool FailEveryRecycle { get; init; }

        public RecycleOutcome Recycle(string path)
        {
            RecycleAttempts.Add(path);
            if (FailEveryRecycle)
            {
                throw new IOException($"{path} could not be recycled.");
            }

            Recycled.Add(path);
            return RecycleOutcome.Restorable(new RecycledItem(
                Path.GetFileName(path),
                path,
                DateTime.Now,
                SizeBytes: null,
                IsDirectory: false,
                RecycleBinIdentity: $"recycle:{Recycled.Count}"));
        }
    }
}
