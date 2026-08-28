using Filekin.Core.FileSystem;
using Filekin.Core.Tidy;

namespace Filekin.Core.Tests.Tidy;

[TestClass]
public sealed class TidyPlannerTests
{
    private const string Folder = @"D:\Downloads";

    private static readonly string[] UnmovableNames = ["movie.mp4.crdownload", "LICENSE"];

    [TestMethod]
    public void LooseFilesAreGroupedByCategoryInPresentationOrder()
    {
        var planner = Build(
            out _,
            File("holiday.jpg"),
            File("report.pdf"),
            File("song.mp3"),
            File("second.jpg"));

        var plan = planner.Plan(Folder);

        Assert.AreEqual(4, plan.FileCount);
        CollectionAssert.AreEqual(
            new[] { TidyCategory.Documents, TidyCategory.Photos, TidyCategory.Audio },
            plan.Groups.Select(group => group.Category).ToArray());
        Assert.AreEqual(2, plan.Groups.Single(g => g.Category == TidyCategory.Photos).Count);
        Assert.AreEqual(
            Path.Combine(Folder, "Photos"),
            plan.Groups.Single(g => g.Category == TidyCategory.Photos).DestinationPath);
    }

    [TestMethod]
    public void ExistingSubfoldersAreNeverTouchedIncludingOurOwnCategoryFolders()
    {
        var planner = Build(
            out _,
            Directory("Photos"),
            Directory("Some Project"),
            File("new.png"));

        var plan = planner.Plan(Folder);

        Assert.AreEqual(1, plan.FileCount);
        Assert.AreEqual("new.png", plan.Groups.Single().Items.Single().Name);
    }

    [TestMethod]
    public void UnknownTypesGoToOtherButUnfinishedDownloadsAndExtensionlessFilesDoNot()
    {
        var planner = Build(
            out _,
            File("data.qqq"),
            File("movie.mp4.crdownload"),
            File("LICENSE"));

        var plan = planner.Plan(Folder);

        Assert.AreEqual(1, plan.FileCount);
        Assert.AreEqual(TidyCategory.Other, plan.Groups.Single().Category);
        Assert.AreEqual("data.qqq", plan.Groups.Single().Items.Single().Name);

        CollectionAssert.AreEquivalent(
            UnmovableNames,
            plan.Skipped.Select(skip => skip.Name).ToArray());
        StringAssert.Contains(
            plan.Skipped.Single(s => s.Name == "movie.mp4.crdownload").Reason,
            "downloading");
    }

    [TestMethod]
    public void AFileAlreadyPresentInItsDestinationIsSkippedNotOverwritten()
    {
        var planner = Build(out var fs, File("holiday.jpg"));
        fs.Add(Path.Combine(Folder, "Photos", "holiday.jpg"), FileSystemEntryKind.File);

        var plan = planner.Plan(Folder);

        Assert.IsFalse(plan.HasWork);
        StringAssert.Contains(plan.Skipped.Single().Reason, "already in Photos");
    }

    [TestMethod]
    public void AnEmptyFolderPlansNoWork()
    {
        var plan = Build(out _).Plan(Folder);

        Assert.IsFalse(plan.HasWork);
        Assert.IsEmpty(plan.Groups);
        Assert.IsEmpty(plan.Skipped);
    }

    private static DirectoryEntry File(string name) =>
        new(name, Path.Combine(Folder, name), IsDirectory: false, SizeBytes: 1, DateTime.UnixEpoch);

    private static DirectoryEntry Directory(string name) =>
        new(name, Path.Combine(Folder, name), IsDirectory: true, SizeBytes: null, DateTime.UnixEpoch);

    private static TidyPlanner Build(out FakeOperations operations, params DirectoryEntry[] entries)
    {
        operations = new FakeOperations();
        return new TidyPlanner(new FakeLister(entries), operations);
    }

    private sealed class FakeLister(IReadOnlyList<DirectoryEntry> entries) : IDirectoryLister
    {
        public IReadOnlyList<DirectoryEntry> List(string path) => entries;
    }

    private sealed class FakeOperations : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Directories { get; } = [];

        public List<(string Source, string Destination)> Moves { get; } = [];

        public void Add(string path, FileSystemEntryKind kind) => _entries[path] = kind;

        public FileSystemEntryKind GetKind(string path) =>
            _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.None;

        public void CreateDirectory(string path) => Directories.Add(path);

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath)
        {
            Moves.Add((sourcePath, destinationPath));
            _entries.Remove(sourcePath);
            _entries[destinationPath] = FileSystemEntryKind.File;
        }

        public void Recycle(string path) => throw new NotSupportedException();
    }
}
