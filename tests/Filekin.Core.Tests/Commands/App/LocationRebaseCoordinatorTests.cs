using Filekin.Core.Commands.App;
using Filekin.Core.Commands.References;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Commands.App;

[TestClass]
public sealed class LocationRebaseCoordinatorTests
{
    private static readonly PathRelocation[] Move =
        [new(@"D:\Work\Source", @"D:\Archive\Source")];

    [TestMethod]
    public async Task SuccessfulSettingsRebaseKeepsTheFilesystemMove()
    {
        var operations = MovedFileSystem();
        var locations = new FakeRebaser(UserLocationPathRebaseResult.Ok(2));
        var coordinator = new LocationRebaseCoordinator(operations, locations);

        var result = await coordinator.RebaseOrRollbackAsync(Move);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, result.UpdatedCount);
        CollectionAssert.AreEqual(Move, result.RemainingRelocations.ToArray());
        Assert.AreEqual(FileSystemEntryKind.None, operations.GetKind(Move[0].SourcePath));
        Assert.AreEqual(FileSystemEntryKind.Directory, operations.GetKind(Move[0].DestinationPath));
        Assert.HasCount(0, operations.Moves);
    }

    [TestMethod]
    public async Task FailedSettingsWriteRollsTheFilesystemMoveBack()
    {
        var operations = MovedFileSystem();
        var locations = new FakeRebaser(UserLocationPathRebaseResult.Fail("settings are read-only"));
        var coordinator = new LocationRebaseCoordinator(operations, locations);

        var result = await coordinator.RebaseOrRollbackAsync(Move);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.RolledBack);
        Assert.IsEmpty(result.RemainingRelocations);
        StringAssert.Contains(result.Message, "rolled back");
        Assert.AreEqual(FileSystemEntryKind.Directory, operations.GetKind(Move[0].SourcePath));
        Assert.AreEqual(FileSystemEntryKind.None, operations.GetKind(Move[0].DestinationPath));
        Assert.AreEqual((Move[0].DestinationPath, Move[0].SourcePath), operations.Moves.Single());
    }

    [TestMethod]
    public async Task FailedCompensationReportsTheInconsistentState()
    {
        var operations = new TrackingFileSystemOperations();
        operations.Add(Move[0].SourcePath, FileSystemEntryKind.Directory);
        operations.Add(Move[0].DestinationPath, FileSystemEntryKind.Directory);
        var locations = new FakeRebaser(UserLocationPathRebaseResult.Fail("settings are read-only"));
        var coordinator = new LocationRebaseCoordinator(operations, locations);

        var result = await coordinator.RebaseOrRollbackAsync(Move);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RolledBack);
        CollectionAssert.AreEqual(Move, result.RemainingRelocations.ToArray());
        StringAssert.Contains(result.Message, "rollback failed");
        StringAssert.Contains(result.Message, Move[0].SourcePath);
    }

    [TestMethod]
    public async Task MultipleMovesAreCompensatedInReverseOrder()
    {
        var relocations = new PathRelocation[]
        {
            new(@"D:\Work\One", @"D:\Archive\One"),
            new(@"D:\Work\Two", @"D:\Archive\Two"),
        };
        var operations = new TrackingFileSystemOperations();
        operations.Add(relocations[0].DestinationPath, FileSystemEntryKind.Directory);
        operations.Add(relocations[1].DestinationPath, FileSystemEntryKind.Directory);
        var coordinator = new LocationRebaseCoordinator(
            operations,
            new FakeRebaser(UserLocationPathRebaseResult.Fail("settings are read-only")));

        var result = await coordinator.RebaseOrRollbackAsync(relocations);

        Assert.IsTrue(result.RolledBack);
        Assert.IsEmpty(result.RemainingRelocations);
        CollectionAssert.AreEqual(
            new[]
            {
                (relocations[1].DestinationPath, relocations[1].SourcePath),
                (relocations[0].DestinationPath, relocations[0].SourcePath),
            },
            operations.Moves);
    }

    [TestMethod]
    public async Task PartialCompensationReportsHowManyItemsWereReturned()
    {
        var relocations = new PathRelocation[]
        {
            // The first item is missing from its destination, so its compensation fails after the
            // second one has already been returned.
            new(@"D:\Work\One", @"D:\Archive\One"),
            new(@"D:\Work\Two", @"D:\Archive\Two"),
        };
        var operations = new TrackingFileSystemOperations();
        operations.Add(relocations[0].SourcePath, FileSystemEntryKind.Directory);
        operations.Add(relocations[0].DestinationPath, FileSystemEntryKind.Directory);
        operations.Add(relocations[1].DestinationPath, FileSystemEntryKind.Directory);
        var coordinator = new LocationRebaseCoordinator(
            operations,
            new FakeRebaser(UserLocationPathRebaseResult.Fail("settings are read-only")));

        var result = await coordinator.RebaseOrRollbackAsync(relocations);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RolledBack);
        StringAssert.Contains(result.Message, "1 of 2 items were returned");
        StringAssert.Contains(result.Message, "1 item remains at its moved destination");
        CollectionAssert.AreEqual(
            new[] { relocations[0] },
            result.RemainingRelocations.ToArray());
        Assert.AreEqual(FileSystemEntryKind.Directory, operations.GetKind(relocations[1].SourcePath));
    }

    [TestMethod]
    public async Task MissingMovedDestinationIsNotReportedAsARemainingRelocation()
    {
        var operations = new TrackingFileSystemOperations();
        var coordinator = new LocationRebaseCoordinator(
            operations,
            new FakeRebaser(UserLocationPathRebaseResult.Fail("settings are read-only")));

        var result = await coordinator.RebaseOrRollbackAsync(Move);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RolledBack);
        Assert.IsEmpty(result.RemainingRelocations);
        StringAssert.Contains(result.Message, "could not be found at either expected destination");
    }

    private static TrackingFileSystemOperations MovedFileSystem()
    {
        var operations = new TrackingFileSystemOperations();
        operations.Add(Move[0].DestinationPath, FileSystemEntryKind.Directory);
        return operations;
    }

    private sealed class FakeRebaser(UserLocationPathRebaseResult result) : IUserLocationPathRebaser
    {
        public Task<UserLocationPathRebaseResult> RebaseAsync(
            IReadOnlyList<PathRelocation> relocations,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class TrackingFileSystemOperations : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Source, string Destination)> Moves { get; } = [];

        public void Add(string path, FileSystemEntryKind kind) => _entries[path] = kind;

        public FileSystemEntryKind GetKind(string path) =>
            _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.None;

        public List<string> Directories { get; } = [];

        public void CreateDirectory(string path) => Directories.Add(path);

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath)
        {
            var kind = GetKind(sourcePath);
            if (kind == FileSystemEntryKind.None)
            {
                throw new IOException($"Missing {sourcePath}");
            }

            _entries.Remove(sourcePath);
            _entries[destinationPath] = kind;
            Moves.Add((sourcePath, destinationPath));
        }

        public RecycleOutcome Recycle(string path) => throw new NotSupportedException();
    }
}
