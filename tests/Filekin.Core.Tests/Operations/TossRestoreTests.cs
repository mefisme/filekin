using System.Text.Json;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class TossRestoreTests
{
    private static readonly string[] ReverseIdentityOrder = ["id-b", "id-a"];

    [TestMethod]
    public void ExactIdentityIsReadyEvenWhenAnOlderItemHasTheSameOriginalPath()
    {
        var item = Tossed(@"D:\Work\same.txt", "new-id");
        var recycleBin = new FakeRecycleBin(
            Recycled(@"D:\Work\same.txt", "old-id"),
            Recycled(@"D:\Work\same.txt", "new-id"));
        var evaluator = new TossRestoreEvaluator(new FakeFileSystem(), recycleBin);

        var assessment = evaluator.Evaluate(Payload(item));

        Assert.IsTrue(assessment.IsReady);
    }

    [TestMethod]
    public void MissingExactIdentityMakesRestoreUnavailable()
    {
        var item = Tossed(@"D:\Work\same.txt", "new-id");
        var recycleBin = new FakeRecycleBin(Recycled(@"D:\Work\same.txt", "old-id"));
        var evaluator = new TossRestoreEvaluator(new FakeFileSystem(), recycleBin);

        var assessment = evaluator.Evaluate(Payload(item));

        Assert.AreEqual(TossRestoreSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(TossRestoreIssueKind.RecycledItemMissing, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void OccupiedOriginalPathNeedsConflictResolution()
    {
        var item = Tossed(@"D:\Work\same.txt", "new-id");
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(item.OriginalPath);
        var evaluator = new TossRestoreEvaluator(
            fileSystem,
            new FakeRecycleBin(Recycled(item.OriginalPath, item.RecycleBinIdentity!)));

        var assessment = evaluator.Evaluate(Payload(item));

        Assert.AreEqual(TossRestoreSafety.NeedsConflictResolution, assessment.Safety);
        Assert.AreEqual(TossRestoreIssueKind.OriginalPathOccupied, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void InformationalTossPayloadFailsClosed()
    {
        var item = new TossedItem(
            @"D:\Work\same.txt",
            "same.txt",
            false,
            recycleBinIdentity: null,
            "Windows did not expose an exact item.");
        var evaluator = new TossRestoreEvaluator(new FakeFileSystem(), new FakeRecycleBin());

        var assessment = evaluator.Evaluate(Payload(item));

        Assert.AreEqual(TossRestoreSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(TossRestoreIssueKind.RestoreIdentityUnavailable, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void RecycleBinInspectionFailureMakesEveryPendingItemUnavailable()
    {
        var first = Tossed(@"D:\Work\a.txt", "id-a");
        var second = Tossed(@"D:\Work\b.txt", "id-b");
        var recycleBin = new FakeRecycleBin { ListFailureOnCall = 1 };
        var evaluator = new TossRestoreEvaluator(new FakeFileSystem(), recycleBin);

        var assessment = evaluator.Evaluate(Payload(first, second));

        Assert.AreEqual(TossRestoreSafety.Unavailable, assessment.Safety);
        Assert.HasCount(2, assessment.Issues);
        Assert.IsTrue(assessment.Issues.All(issue => issue.Kind == TossRestoreIssueKind.InspectionFailed));
    }

    [TestMethod]
    public void RestoreRunsInReverseOrderAndClearsPendingItems()
    {
        var first = Tossed(@"D:\Work\a.txt", "id-a");
        var second = Tossed(@"D:\Work\b.txt", "id-b");
        var recycleBin = new FakeRecycleBin(
            Recycled(first.OriginalPath, first.RecycleBinIdentity!),
            Recycled(second.OriginalPath, second.RecycleBinIdentity!));
        var restore = new TossRestore(new FakeFileSystem(), recycleBin);

        var result = restore.Restore(Payload(first, second));

        Assert.AreEqual(TossRestoreOutcome.Succeeded, result.Outcome);
        CollectionAssert.AreEqual(ReverseIdentityOrder, recycleBin.RestoredIdentities.ToArray());
        Assert.IsEmpty(result.RemainingItems);
        Assert.IsEmpty(result.UpdatedPayload.PendingItems);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void FreshRecheckStopsAfterAnotherOriginalPathBecomesOccupied()
    {
        var first = Tossed(@"D:\Work\a.txt", "id-a");
        var second = Tossed(@"D:\Work\b.txt", "id-b");
        var fileSystem = new FakeFileSystem();
        var recycleBin = new FakeRecycleBin(
            Recycled(first.OriginalPath, first.RecycleBinIdentity!),
            Recycled(second.OriginalPath, second.RecycleBinIdentity!));
        recycleBin.AfterRestore = identity =>
        {
            if (identity == "id-b")
            {
                fileSystem.AddFile(first.OriginalPath);
            }
        };
        var restore = new TossRestore(fileSystem, recycleBin);

        var result = restore.Restore(Payload(first, second));

        Assert.AreEqual(TossRestoreOutcome.PartiallyRestored, result.Outcome);
        CollectionAssert.AreEqual(new[] { second }, result.RestoredItems.ToArray());
        CollectionAssert.AreEqual(new[] { first }, result.RemainingItems.ToArray());
        Assert.AreEqual(
            TossRestoreIssueKind.OriginalPathOccupied,
            result.BlockedBy!.Issues.Single().Kind);
        CollectionAssert.AreEqual(new[] { first }, result.UpdatedPayload.PendingItems.ToArray());
    }

    [TestMethod]
    public void RestoreReturningFalsePreservesTheExactItemForRetry()
    {
        var item = Tossed(@"D:\Work\a.txt", "id-a");
        var recycleBin = new FakeRecycleBin(Recycled(item.OriginalPath, item.RecycleBinIdentity!))
        {
            ReturnFalseForIdentity = item.RecycleBinIdentity,
        };
        var restore = new TossRestore(new FakeFileSystem(), recycleBin);

        var result = restore.Restore(Payload(item));

        Assert.AreEqual(TossRestoreOutcome.Failed, result.Outcome);
        Assert.IsFalse(result.Failures.Single().MayHaveRestored);
        CollectionAssert.AreEqual(new[] { item }, result.UpdatedPayload.PendingItems.ToArray());
        Assert.IsFalse(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void IoFailureReportsThatShellStateMayHaveChanged()
    {
        var item = Tossed(@"D:\Work\a.txt", "id-a");
        var recycleBin = new FakeRecycleBin(Recycled(item.OriginalPath, item.RecycleBinIdentity!))
        {
            ThrowForIdentity = item.RecycleBinIdentity,
        };
        var restore = new TossRestore(new FakeFileSystem(), recycleBin);

        var result = restore.Restore(Payload(item));

        Assert.AreEqual(TossRestoreOutcome.Failed, result.Outcome);
        Assert.IsTrue(result.Failures.Single().MayHaveRestored);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
        CollectionAssert.AreEqual(new[] { item }, result.RemainingItems.ToArray());
    }

    [TestMethod]
    public void PartialPayloadRoundTripsWithoutReintroducingRestoredItems()
    {
        var first = Tossed(@"D:\Work\a.txt", "id-a");
        var second = Tossed(@"D:\Work\b.txt", "id-b");
        var partial = Payload(first, second).WithPendingItems([first]);

        var roundTripped = JsonSerializer.Deserialize<TossOperationPayload>(
            JsonSerializer.Serialize(partial));

        Assert.IsNotNull(roundTripped);
        CollectionAssert.AreEqual(new[] { first, second }, roundTripped.Items.ToArray());
        CollectionAssert.AreEqual(new[] { first }, roundTripped.PendingItems.ToArray());
    }

    private static TossOperationPayload Payload(params TossedItem[] items) => new(items, []);

    private static TossedItem Tossed(string originalPath, string identity) => new(
        originalPath,
        Path.GetFileName(originalPath),
        isDirectory: false,
        identity,
        restoreUnavailableReason: null);

    private static RecycledItem Recycled(string originalPath, string identity) => new(
        Path.GetFileName(originalPath),
        originalPath,
        DateTime.Now,
        SizeBytes: 1,
        IsDirectory: false,
        identity);

    private sealed class FakeFileSystem : IFileSystemOperations
    {
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path) => _files.Add(path);

        public FileSystemEntryKind GetKind(string path) =>
            _files.Contains(path) ? FileSystemEntryKind.File : FileSystemEntryKind.None;

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public RecycleOutcome Recycle(string path) => throw new NotSupportedException();
    }

    private sealed class FakeRecycleBin : IRecycleBin
    {
        private readonly List<RecycledItem> _items;
        private int _listCalls;

        public FakeRecycleBin(params RecycledItem[] items)
        {
            _items = [.. items];
        }

        public int? ListFailureOnCall { get; init; }

        public string? ReturnFalseForIdentity { get; init; }

        public string? ThrowForIdentity { get; init; }

        public Action<string>? AfterRestore { get; set; }

        public List<string> RestoredIdentities { get; } = [];

        public IReadOnlyList<RecycledItem> List()
        {
            _listCalls++;
            if (ListFailureOnCall == _listCalls)
            {
                throw new IOException("Recycle Bin unavailable.");
            }

            return [.. _items];
        }

        public bool Restore(RecycledItem item)
        {
            var identity = item.RecycleBinIdentity!;
            if (identity == ThrowForIdentity)
            {
                throw new IOException("Restore failed after shell work began.");
            }

            if (identity == ReturnFalseForIdentity)
            {
                return false;
            }

            var removed = _items.RemoveAll(candidate =>
                string.Equals(
                    candidate.RecycleBinIdentity,
                    identity,
                    StringComparison.OrdinalIgnoreCase));
            if (removed != 1)
            {
                return false;
            }

            RestoredIdentities.Add(identity);
            AfterRestore?.Invoke(identity);
            return true;
        }

        public bool DeleteForever(RecycledItem item) => throw new NotSupportedException();

        public void Empty() => throw new NotSupportedException();
    }
}
