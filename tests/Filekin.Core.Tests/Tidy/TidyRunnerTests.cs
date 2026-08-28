using Filekin.Core.FileSystem;
using Filekin.Core.Tidy;

namespace Filekin.Core.Tests.Tidy;

[TestClass]
public sealed class TidyRunnerTests
{
    private const string Folder = @"D:\Downloads";

    [TestMethod]
    public void OnlyTickedCategoriesAreMoved()
    {
        var plan = Plan(("holiday.jpg", TidyCategory.Photos), ("setup.exe", TidyCategory.Installers));
        var operations = new FakeOperations();

        var outcome = new TidyRunner(operations).Run(plan, [TidyCategory.Photos]);

        Assert.AreEqual(1, outcome.MovedCount);
        Assert.AreEqual(
            (Path.Combine(Folder, "holiday.jpg"), Path.Combine(Folder, "Photos", "holiday.jpg")),
            operations.Moves.Single());
        CollectionAssert.AreEqual(new[] { Path.Combine(Folder, "Photos") }, operations.Directories);
    }

    [TestMethod]
    public void TheCategoryFolderIsCreatedOnlyOnceAndOnlyWhenSomethingGoesIn()
    {
        var plan = Plan(("a.jpg", TidyCategory.Photos), ("b.jpg", TidyCategory.Photos));
        var operations = new FakeOperations();

        var outcome = new TidyRunner(operations).Run(plan, [TidyCategory.Photos, TidyCategory.Audio]);

        Assert.AreEqual(2, outcome.MovedCount);
        Assert.HasCount(1, operations.Directories);
        CollectionAssert.AreEqual(new[] { TidyCategory.Photos }, outcome.FoldersUsed.ToArray());
    }

    [TestMethod]
    public void AFileThatArrivedSincePlanningIsSkippedNotOverwritten()
    {
        var plan = Plan(("holiday.jpg", TidyCategory.Photos));
        var operations = new FakeOperations();
        operations.Add(Path.Combine(Folder, "Photos", "holiday.jpg"), FileSystemEntryKind.File);

        var outcome = new TidyRunner(operations).Run(plan, [TidyCategory.Photos]);

        Assert.AreEqual(0, outcome.MovedCount);
        Assert.IsEmpty(operations.Moves);
        StringAssert.Contains(outcome.Skipped.Single().Reason, "already in Photos");
    }

    [TestMethod]
    public void OneFailedFileDoesNotStopTheRest()
    {
        var plan = Plan(("bad.jpg", TidyCategory.Photos), ("good.jpg", TidyCategory.Photos));
        var operations = new FakeOperations { FailOn = "bad.jpg" };

        var outcome = new TidyRunner(operations).Run(plan, [TidyCategory.Photos]);

        Assert.AreEqual(1, outcome.MovedCount);
        Assert.AreEqual("bad.jpg", outcome.Failures.Single().Name);
    }

    [TestMethod]
    public void ProgressReportsEachFileAndThenFinishes()
    {
        var plan = Plan(("a.jpg", TidyCategory.Photos), ("b.jpg", TidyCategory.Photos));
        var seen = new List<TidyProgress>();

        // Progress<T> marshals to a captured context, so the test uses a synchronous sink instead.
        new TidyRunner(new FakeOperations()).Run(plan, [TidyCategory.Photos], new ImmediateProgress(seen));

        Assert.AreEqual(3, seen.Count);
        Assert.AreEqual(2, seen[^1].FilesDone);
        Assert.AreEqual(string.Empty, seen[^1].CurrentName);
    }

    [TestMethod]
    public void CancellationStopsTheRun()
    {
        var plan = Plan(("a.jpg", TidyCategory.Photos));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new TidyRunner(new FakeOperations()).Run(plan, [TidyCategory.Photos], null, cancelled.Token));
    }

    private static TidyPlan Plan(params (string Name, TidyCategory Category)[] files)
    {
        var groups = files
            .GroupBy(file => file.Category)
            .Select(group => new TidyGroup(
                group.Key,
                Path.Combine(Folder, group.Key.FolderName()),
                [.. group.Select(file => new TidyItem(Path.Combine(Folder, file.Name), file.Name, group.Key))]))
            .ToList();
        return new TidyPlan(Folder, groups, []);
    }

    private sealed class ImmediateProgress(List<TidyProgress> sink) : IProgress<TidyProgress>
    {
        public void Report(TidyProgress value) => sink.Add(value);
    }

    private sealed class FakeOperations : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Directories { get; } = [];

        public List<(string Source, string Destination)> Moves { get; } = [];

        public string? FailOn { get; init; }

        public void Add(string path, FileSystemEntryKind kind) => _entries[path] = kind;

        public FileSystemEntryKind GetKind(string path) =>
            _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.None;

        public void CreateDirectory(string path) => Directories.Add(path);

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath)
        {
            if (FailOn is not null && Path.GetFileName(sourcePath).Equals(FailOn, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"{FailOn} is in use.");
            }

            Moves.Add((sourcePath, destinationPath));
        }

        public void Recycle(string path) => throw new NotSupportedException();
    }
}
