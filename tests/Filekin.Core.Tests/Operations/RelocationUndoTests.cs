using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class RelocationUndoTests
{
    private static readonly PathRelocation First =
        new(@"D:\Work\a.txt", @"D:\Work\out\a.txt");
    private static readonly PathRelocation Second =
        new(@"D:\Work\b.txt", @"D:\Work\out\b.txt");

    [TestMethod]
    public void ExistingDestinationsAndAbsentOriginalsAreReady()
    {
        var operations = ReadyOperations();

        var assessment = new RelocationUndoEvaluator(operations).Evaluate(Payload());

        Assert.AreEqual(RelocationUndoSafety.Ready, assessment.Safety);
        Assert.IsEmpty(assessment.Issues);
    }

    [TestMethod]
    public void OccupiedOriginalPathRequiresConflictResolution()
    {
        var operations = ReadyOperations();
        operations.AddFile(First.SourcePath);

        var assessment = new RelocationUndoEvaluator(operations).Evaluate(Payload());

        Assert.AreEqual(RelocationUndoSafety.NeedsConflictResolution, assessment.Safety);
        Assert.AreEqual(RelocationUndoIssueKind.OriginalPathOccupied, assessment.Issues[0].Kind);
    }

    [TestMethod]
    public void MissingMovedItemMakesUndoUnavailable()
    {
        var operations = ReadyOperations();
        operations.Remove(First.DestinationPath);

        var assessment = new RelocationUndoEvaluator(operations).Evaluate(Payload());

        Assert.AreEqual(RelocationUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(RelocationUndoIssueKind.MovedItemMissing, assessment.Issues[0].Kind);
    }

    [TestMethod]
    public void InspectionFailureMakesUndoUnavailable()
    {
        var operations = ReadyOperations();
        operations.InspectionFailurePath = First.DestinationPath;

        var assessment = new RelocationUndoEvaluator(operations).Evaluate(Payload());

        Assert.AreEqual(RelocationUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(RelocationUndoIssueKind.InspectionFailed, assessment.Issues[0].Kind);
    }

    [TestMethod]
    public void UndoReversesTheBatchInReverseExecutionOrder()
    {
        var operations = ReadyOperations();

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.Succeeded, result.Outcome);
        CollectionAssert.AreEqual(
            new[]
            {
                (Second.DestinationPath, Second.SourcePath),
                (First.DestinationPath, First.SourcePath),
            },
            operations.Moves.ToArray());
        CollectionAssert.AreEqual(new[] { Second, First }, result.ReversedRelocations.ToArray());
        Assert.IsEmpty(result.RemainingRelocations);
        Assert.IsEmpty(result.UpdatedPayload.PendingRelocations);
        CollectionAssert.AreEqual(
            new[] { First, Second },
            result.UpdatedPayload.Relocations.ToArray());
        Assert.IsEmpty(result.Failures);
        Assert.IsNull(result.BlockedBy);
    }

    [TestMethod]
    public void PreflightConflictMakesNoFilesystemChanges()
    {
        var operations = ReadyOperations();
        operations.AddFile(First.SourcePath);

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.Blocked, result.Outcome);
        Assert.IsEmpty(operations.Moves);
        Assert.HasCount(2, result.RemainingRelocations);
        Assert.AreEqual(RelocationUndoSafety.NeedsConflictResolution, result.BlockedBy!.Safety);
        Assert.IsFalse(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void MissingMovedItemBlocksTheWholeAttemptBeforeAnyFilesystemChange()
    {
        var operations = ReadyOperations();
        operations.Remove(First.DestinationPath);

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.Blocked, result.Outcome);
        Assert.IsEmpty(operations.Moves);
        Assert.AreEqual(RelocationUndoSafety.Unavailable, result.BlockedBy!.Safety);
        Assert.IsFalse(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void OneRuntimeFailureProducesAnExactPartialResultAndContinues()
    {
        var operations = ReadyOperations();
        operations.MoveFailurePath = Second.DestinationPath;

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.PartiallyUndone, result.Outcome);
        CollectionAssert.AreEqual(new[] { First }, result.ReversedRelocations.ToArray());
        CollectionAssert.AreEqual(new[] { Second }, result.RemainingRelocations.ToArray());
        CollectionAssert.AreEqual(
            new[] { Second },
            result.UpdatedPayload.PendingRelocations.ToArray());
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual(Second, result.Failures[0].Relocation);
        Assert.IsTrue(result.Failures[0].MayHaveReversed);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void PartialResultPayloadRetriesOnlyTheRemainingRelocation()
    {
        var operations = ReadyOperations();
        operations.MoveFailurePath = Second.DestinationPath;
        var undo = new RelocationUndo(operations);
        var partial = undo.Undo(Payload());
        operations.MoveFailurePath = null;

        var retry = undo.Undo(partial.UpdatedPayload);

        Assert.AreEqual(RelocationUndoOutcome.Succeeded, retry.Outcome);
        CollectionAssert.AreEqual(new[] { Second }, retry.ReversedRelocations.ToArray());
        Assert.IsEmpty(retry.UpdatedPayload.PendingRelocations);
        CollectionAssert.AreEqual(
            new[] { First, Second },
            retry.UpdatedPayload.Relocations.ToArray());
    }

    [TestMethod]
    public void FailuresOnEveryMoveReportFailureWithoutClaimingAReversal()
    {
        var operations = ReadyOperations();
        operations.FailEveryMove = true;

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.Failed, result.Outcome);
        Assert.IsEmpty(result.ReversedRelocations);
        Assert.HasCount(2, result.RemainingRelocations);
        Assert.HasCount(2, result.Failures);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void CollisionAppearingMidRunStopsWithoutOverwritingAndReportsPartialState()
    {
        var operations = ReadyOperations();
        operations.AfterFirstSuccessfulMove = () => operations.AddFile(First.SourcePath);

        var result = new RelocationUndo(operations).Undo(Payload());

        Assert.AreEqual(RelocationUndoOutcome.PartiallyUndone, result.Outcome);
        CollectionAssert.AreEqual(new[] { Second }, result.ReversedRelocations.ToArray());
        CollectionAssert.AreEqual(new[] { First }, result.RemainingRelocations.ToArray());
        CollectionAssert.AreEqual(
            new[] { First },
            result.UpdatedPayload.PendingRelocations.ToArray());
        Assert.AreEqual(RelocationUndoSafety.NeedsConflictResolution, result.BlockedBy!.Safety);
        Assert.HasCount(1, operations.Moves);
    }

    private static RelocationOperationPayload Payload() => new([First, Second], []);

    private static FakeFileSystemOperations ReadyOperations()
    {
        var operations = new FakeFileSystemOperations();
        operations.AddFile(First.DestinationPath);
        operations.AddFile(Second.DestinationPath);
        return operations;
    }

    private sealed class FakeFileSystemOperations : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private int _successfulMoves;

        public List<(string Source, string Destination)> Moves { get; } = [];

        public string? InspectionFailurePath { get; set; }

        public string? MoveFailurePath { get; set; }

        public bool FailEveryMove { get; set; }

        public Action? AfterFirstSuccessfulMove { get; set; }

        public void AddFile(string path) => _entries[path] = FileSystemEntryKind.File;

        public void Remove(string path) => _entries.Remove(path);

        public FileSystemEntryKind GetKind(string path)
        {
            if (path.Equals(InspectionFailurePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Cannot inspect {path}.");
            }

            return _entries.GetValueOrDefault(path);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            if (FailEveryMove || sourcePath.Equals(MoveFailurePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Cannot move {sourcePath}.");
            }

            if (GetKind(destinationPath) != FileSystemEntryKind.None)
            {
                throw new IOException($"Destination exists: {destinationPath}");
            }

            var kind = GetKind(sourcePath);
            if (kind == FileSystemEntryKind.None)
            {
                throw new IOException($"Source missing: {sourcePath}");
            }

            _entries.Remove(sourcePath);
            _entries[destinationPath] = kind;
            Moves.Add((sourcePath, destinationPath));
            _successfulMoves++;
            if (_successfulMoves == 1)
            {
                AfterFirstSuccessfulMove?.Invoke();
            }
        }

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public RecycleOutcome Recycle(string path) => throw new NotSupportedException();
    }
}
