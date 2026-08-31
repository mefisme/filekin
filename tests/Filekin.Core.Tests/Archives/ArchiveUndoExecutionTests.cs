using System.Text.Json;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.Archives;

[TestClass]
public sealed class ArchiveUndoExecutionTests
{
    private static readonly DateTime WrittenAt =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void PayloadRoundTripRetainsOriginalAndExactPendingWork()
    {
        var first = Captured(@"D:\Work\first.txt", "AA");
        var second = Captured(@"D:\Work\second.txt", "BB");
        var archive = Work(0, first, second).WithPending([second], [], []);
        var payload = new ArchiveUndoPayload(ArchiveUndoOperationKind.Extraction, [archive]);

        var roundTripped = JsonSerializer.Deserialize<ArchiveUndoPayload>(JsonSerializer.Serialize(payload));

        Assert.IsNotNull(roundTripped);
        CollectionAssert.AreEqual(new[] { first, second }, roundTripped.Archives.Single().Outputs.ToArray());
        CollectionAssert.AreEqual(new[] { second }, roundTripped.Archives.Single().PendingOutputs.ToArray());
    }

    [TestMethod]
    public void OverlappingArchivesReverseInInvocationOrderAndReevaluateDependencies()
    {
        const string path = @"D:\Work\same.txt";
        var firstOutput = Captured(path, "AA");
        var secondOutput = Captured(path, "BB");
        var replacement = Replacement(path, "bin:first");
        var fixture = new Fixture();
        fixture.SetCurrent(secondOutput);
        fixture.AddRecycled(replacement, firstOutput);
        var payload = new ArchiveUndoPayload(
            ArchiveUndoOperationKind.Extraction,
            [
                Work(0, firstOutput),
                Work(1, [secondOutput], [], [replacement]),
            ]);

        var result = fixture.Executor.Execute(payload);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Succeeded, result.Outcome);
        CollectionAssert.AreEqual(
            new[] { $"delete:{path}:BB", "restore:bin:first", $"delete:{path}:AA" },
            fixture.Actions.ToArray());
        Assert.IsFalse(result.UpdatedPayload.HasPendingWork);
        Assert.HasCount(2, result.RemovedOutputs);
        Assert.HasCount(1, result.RestoredOriginals);
    }

    [TestMethod]
    public void EditedOutputWithoutDecisionBlocksBeforeChangingAnything()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var fixture = new Fixture();
        fixture.SetCurrent(Captured(completion.Path, "BB"));

        var result = fixture.Executor.Execute(Payload(Work(0, completion)));

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Blocked, result.Outcome);
        Assert.AreEqual(ArchiveUndoSafety.NeedsEditedOutputDecision, result.BlockedBy!.Safety);
        Assert.IsEmpty(fixture.Actions);
        Assert.IsTrue(result.UpdatedPayload.HasPendingWork);
    }

    [TestMethod]
    public void KeepEditedProducesAnAccuratePartialResultWithoutRestoringOverIt()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(completion.Path, "bin:old");
        var fixture = new Fixture();
        fixture.SetCurrent(Captured(completion.Path, "BB"));
        fixture.AddRecycled(replacement);
        var payload = Payload(Work(0, [completion], [], [replacement]));
        var conflict = fixture.Evaluator.Evaluate(payload).EditedOutputs.Single();
        var resolution = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.KeepEdited);

        var result = fixture.Executor.Execute(payload, [resolution]);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.PartiallyUndone, result.Outcome);
        Assert.IsFalse(result.UpdatedPayload.HasPendingWork);
        CollectionAssert.AreEqual(new[] { completion }, result.KeptEditedOutputs.ToArray());
        CollectionAssert.AreEqual(new[] { replacement }, result.KeptRecycledOriginals.ToArray());
        Assert.IsEmpty(fixture.Actions);
        Assert.IsFalse(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void RecycleEditedUsesNativeRecycleThenRestoresTheExactOriginal()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var edited = Captured(completion.Path, "BB");
        var replacement = Replacement(completion.Path, "bin:old");
        var fixture = new Fixture();
        fixture.SetCurrent(edited);
        fixture.AddRecycled(replacement);
        var payload = Payload(Work(0, [completion], [], [replacement]));
        var conflict = fixture.Evaluator.Evaluate(payload).EditedOutputs.Single();
        var resolution = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.RecycleEdited);

        var result = fixture.Executor.Execute(payload, [resolution]);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Succeeded, result.Outcome);
        CollectionAssert.AreEqual(
            new[] { $"recycle:{completion.Path}:BB", "restore:bin:old" },
            fixture.Actions.ToArray());
        Assert.HasCount(1, result.RecycledEditedOutputs);
        Assert.HasCount(1, result.RestoredOriginals);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
    }

    [TestMethod]
    public void NonrecoverableNativeRecycleCannotBeReportedAsACompleteUndo()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var fixture = new Fixture { RecycleIsInformational = true };
        fixture.SetCurrent(Captured(completion.Path, "BB"));
        var payload = Payload(Work(0, completion));
        var conflict = fixture.Evaluator.Evaluate(payload).EditedOutputs.Single();
        var resolution = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.RecycleEdited);

        var result = fixture.Executor.Execute(payload, [resolution]);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.PartiallyUndone, result.Outcome);
        Assert.IsFalse(result.RecycledEditedOutputs.Single().CanRestore);
        Assert.IsFalse(result.UpdatedPayload.HasPendingWork);
    }

    [TestMethod]
    public void CancelDecisionMakesNoChanges()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var fixture = new Fixture();
        fixture.SetCurrent(Captured(completion.Path, "BB"));
        var payload = Payload(Work(0, completion));
        var conflict = fixture.Evaluator.Evaluate(payload).EditedOutputs.Single();
        var resolution = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.Cancel);

        var result = fixture.Executor.Execute(payload, [resolution]);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Cancelled, result.Outcome);
        Assert.AreSame(payload, result.UpdatedPayload);
        Assert.IsEmpty(fixture.Actions);
    }

    [TestMethod]
    public void RecycleDecisionIsRejectedIfTheEditedFileChangedAgain()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var fixture = new Fixture();
        fixture.SetCurrent(Captured(completion.Path, "BB"));
        var payload = Payload(Work(0, completion));
        var conflict = fixture.Evaluator.Evaluate(payload).EditedOutputs.Single();
        var resolution = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.RecycleEdited);
        fixture.SetCurrent(Captured(completion.Path, "CC"));

        var result = fixture.Executor.Execute(payload, [resolution]);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Blocked, result.Outcome);
        Assert.IsEmpty(fixture.Actions);
        Assert.IsTrue(result.UpdatedPayload.HasPendingWork);
    }

    [TestMethod]
    public void FreshPerActionRecheckCatchesAnEditAfterPreflight()
    {
        var completion = Captured(@"D:\Work\a.txt", "AA");
        var fixture = new Fixture();
        fixture.SetCurrent(completion);
        fixture.ReadSequence(completion.Path, completion, Captured(completion.Path, "BB"));

        var result = fixture.Executor.Execute(Payload(Work(0, completion)));

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Blocked, result.Outcome);
        Assert.AreEqual(ArchiveUndoSafety.NeedsEditedOutputDecision, result.BlockedBy!.Safety);
        Assert.IsEmpty(fixture.Actions);
    }

    [TestMethod]
    public void ExactReplacementIdentityIsRecheckedImmediatelyBeforeRestore()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(output.Path, "bin:old");
        var fixture = new Fixture { RemoveRecycledItemsAfterFirstList = true };
        fixture.SetCurrent(output);
        fixture.AddRecycled(replacement);

        var result = fixture.Executor.Execute(Payload(Work(0, [output], [], [replacement])));

        Assert.AreEqual(ArchiveUndoAttemptOutcome.PartiallyUndone, result.Outcome);
        CollectionAssert.AreEqual(new[] { $"delete:{output.Path}:AA" }, fixture.Actions.ToArray());
        Assert.IsEmpty(result.UpdatedPayload.Archives.Single().PendingOutputs);
        Assert.HasCount(1, result.UpdatedPayload.Archives.Single().PendingReplacements);
        Assert.AreEqual(ArchiveUndoIssueKind.RecycledOriginalMissing, result.BlockedBy!.Issues.Single().Kind);
    }

    [TestMethod]
    public void FailedOutputRemovalRetainsItAndStopsBeforeAnEarlierArchive()
    {
        var first = Captured(@"D:\Work\first.txt", "AA");
        var second = Captured(@"D:\Work\second.txt", "BB");
        var fixture = new Fixture { DeleteFailurePath = second.Path };
        fixture.SetCurrent(first);
        fixture.SetCurrent(second);
        var payload = Payload(Work(0, first), Work(1, second));

        var result = fixture.Executor.Execute(payload);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Failed, result.Outcome);
        Assert.HasCount(1, result.Failures);
        Assert.HasCount(1, result.UpdatedPayload.Archives[0].PendingOutputs);
        Assert.HasCount(1, result.UpdatedPayload.Archives[1].PendingOutputs);
        CollectionAssert.AreEqual(new[] { $"delete-failed:{second.Path}" }, fixture.Actions.ToArray());
    }

    [TestMethod]
    public void RetryTouchesOnlyExactRemainingWork()
    {
        var first = Captured(@"D:\Work\first.txt", "AA");
        var second = Captured(@"D:\Work\second.txt", "BB");
        var fixture = new Fixture { DeleteFailurePath = first.Path };
        fixture.SetCurrent(first);
        fixture.SetCurrent(second);
        var partial = fixture.Executor.Execute(Payload(Work(0, first, second)));
        fixture.DeleteFailurePath = null;
        fixture.Actions.Clear();

        var retry = fixture.Executor.Execute(partial.UpdatedPayload);

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Succeeded, retry.Outcome);
        CollectionAssert.AreEqual(new[] { $"delete:{first.Path}:AA" }, fixture.Actions.ToArray());
        Assert.IsFalse(retry.UpdatedPayload.HasPendingWork);
    }

    [TestMethod]
    public void DirectoryFailureRetainsItAndEveryShallowerDirectory()
    {
        var root = @"D:\Work\Extracted";
        var child = Path.Combine(root, "Child");
        var fixture = new Fixture { DirectoryFailurePath = child };
        fixture.AddDirectory(root);
        fixture.AddDirectory(child);
        var work = new ArchiveUndoArchiveWork(0, "a.zip", [], [root, child], []);

        var result = fixture.Executor.Execute(Payload(work));

        Assert.AreEqual(ArchiveUndoAttemptOutcome.Failed, result.Outcome);
        CollectionAssert.AreEqual(
            new[] { root, child },
            result.UpdatedPayload.Archives.Single().PendingDirectories.ToArray());
        CollectionAssert.AreEqual(new[] { $"directory-failed:{child}" }, fixture.Actions.ToArray());
    }

    [TestMethod]
    public void PendingWorkCannotInjectAPathOutsideTheOriginalOperation()
    {
        var original = Captured(@"D:\Work\a.txt", "AA");
        var injected = Captured(@"D:\Work\unrelated.txt", "BB");

        _ = Assert.ThrowsExactly<ArgumentException>(() => new ArchiveUndoArchiveWork(
            0,
            "a.zip",
            [original],
            [],
            [],
            [injected],
            [],
            []));
    }

    [TestMethod]
    public void NonemptyCreatedDirectoryIsKeptAndReportedPartial()
    {
        var directory = @"D:\Work\Extracted";
        var fixture = new Fixture();
        fixture.AddDirectory(directory, ArchiveDirectoryRemoval.NotEmpty);
        var work = new ArchiveUndoArchiveWork(0, "a.zip", [], [directory], []);

        var result = fixture.Executor.Execute(Payload(work));

        Assert.AreEqual(ArchiveUndoAttemptOutcome.PartiallyUndone, result.Outcome);
        CollectionAssert.AreEqual(new[] { directory }, result.KeptDirectories.ToArray());
        Assert.IsFalse(result.UpdatedPayload.HasPendingWork);
    }

    private static ArchiveUndoPayload Payload(params ArchiveUndoArchiveWork[] archives) =>
        new(ArchiveUndoOperationKind.Extraction, archives);

    private static ArchiveUndoArchiveWork Work(int index, params ArchiveOutputEvidence[] outputs) =>
        Work(index, outputs, [], []);

    private static ArchiveUndoArchiveWork Work(
        int index,
        IReadOnlyList<ArchiveOutputEvidence> outputs,
        IReadOnlyList<string> directories,
        IReadOnlyList<ArchiveReplacementEvidence> replacements) =>
        new(index, $"archive-{index}.zip", outputs, directories, replacements);

    private static ArchiveOutputEvidence Captured(string path, string hash) =>
        ArchiveOutputEvidence.Captured(path, length: 12, WrittenAt, hash);

    private static ArchiveReplacementEvidence Replacement(string path, string identity) =>
        new(path, Recycled(path, identity), restoreUnavailableReason: null);

    private static RecycledItem Recycled(string path, string identity) =>
        new(Path.GetFileName(path), path, WrittenAt, SizeBytes: 12, IsDirectory: false, identity);

    private sealed class Fixture :
        IFileSystemOperations,
        IRecycleBin,
        IArchiveOutputEvidenceReader,
        IArchiveUndoStorage
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ArchiveOutputEvidence> _currentEvidence =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<ArchiveOutputEvidence>> _readSequences =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RecycledItem> _recycled = [];
        private readonly Dictionary<string, ArchiveOutputEvidence> _restoreEvidence =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ArchiveDirectoryRemoval> _directories =
            new(StringComparer.OrdinalIgnoreCase);
        private int _listCalls;

        public Fixture()
        {
            Evaluator = new ArchiveUndoEvaluator(this, this, this);
            Executor = new ArchiveUndoExecutor(this, this, this, this);
        }

        public ArchiveUndoEvaluator Evaluator { get; }

        public ArchiveUndoExecutor Executor { get; }

        public List<string> Actions { get; } = [];

        public string? DeleteFailurePath { get; set; }

        public string? DirectoryFailurePath { get; set; }

        public bool RemoveRecycledItemsAfterFirstList { get; set; }

        public bool RecycleIsInformational { get; set; }

        public void SetCurrent(ArchiveOutputEvidence evidence)
        {
            _currentEvidence[evidence.Path] = evidence;
            if (evidence.ExistedAtCompletion == true)
            {
                _entries[evidence.Path] = FileSystemEntryKind.File;
            }
            else
            {
                _entries.Remove(evidence.Path);
            }
        }

        public void ReadSequence(string path, params ArchiveOutputEvidence[] evidence) =>
            _readSequences[path] = new Queue<ArchiveOutputEvidence>(evidence);

        public void AddRecycled(
            ArchiveReplacementEvidence evidence,
            ArchiveOutputEvidence? restoredEvidence = null)
        {
            _recycled.Add(evidence.RecycledItem!);
            if (restoredEvidence is not null)
            {
                _restoreEvidence[evidence.RecycledItem!.RecycleBinIdentity!] = restoredEvidence;
            }
        }

        public void AddDirectory(
            string path,
            ArchiveDirectoryRemoval removal = ArchiveDirectoryRemoval.Removed)
        {
            _entries[path] = FileSystemEntryKind.Directory;
            _directories[path] = removal;
        }

        public FileSystemEntryKind GetKind(string path) => _entries.GetValueOrDefault(path);

        public ArchiveOutputEvidence Read(string path)
        {
            if (_readSequences.TryGetValue(path, out var sequence) && sequence.Count > 0)
            {
                return sequence.Dequeue();
            }

            return _currentEvidence.GetValueOrDefault(path) ?? ArchiveOutputEvidence.Absent(path);
        }

        public void DeleteFile(string path)
        {
            if (string.Equals(path, DeleteFailurePath, StringComparison.OrdinalIgnoreCase))
            {
                Actions.Add($"delete-failed:{path}");
                throw new IOException("Delete failed.");
            }

            var hash = _currentEvidence.GetValueOrDefault(path)?.Sha256;
            Actions.Add($"delete:{path}:{hash}");
            _entries.Remove(path);
            _currentEvidence.Remove(path);
        }

        public ArchiveDirectoryRemoval RemoveDirectoryIfEmpty(string path)
        {
            if (string.Equals(path, DirectoryFailurePath, StringComparison.OrdinalIgnoreCase))
            {
                Actions.Add($"directory-failed:{path}");
                throw new IOException("Directory removal failed.");
            }

            var result = _directories.GetValueOrDefault(path, ArchiveDirectoryRemoval.Missing);
            Actions.Add($"directory:{path}:{result}");
            if (result is ArchiveDirectoryRemoval.Removed or ArchiveDirectoryRemoval.Missing)
            {
                _entries.Remove(path);
            }

            return result;
        }

        public RecycleOutcome Recycle(string path)
        {
            var hash = _currentEvidence.GetValueOrDefault(path)?.Sha256;
            Actions.Add($"recycle:{path}:{hash}");
            _entries.Remove(path);
            _currentEvidence.Remove(path);
            if (RecycleIsInformational)
            {
                return RecycleOutcome.Informational(
                    path,
                    FileSystemEntryKind.File,
                    "Windows did not retain a recoverable item.");
            }

            var item = Recycled(path, $"bin:edited:{_recycled.Count}");
            _recycled.Add(item);
            return RecycleOutcome.Restorable(item);
        }

        public IReadOnlyList<RecycledItem> List()
        {
            _listCalls++;
            var result = _recycled.ToArray();
            if (RemoveRecycledItemsAfterFirstList && _listCalls == 1)
            {
                _recycled.Clear();
            }

            return result;
        }

        public bool Restore(RecycledItem item)
        {
            Actions.Add($"restore:{item.RecycleBinIdentity}");
            if (_recycled.RemoveAll(candidate => string.Equals(
                    candidate.RecycleBinIdentity,
                    item.RecycleBinIdentity,
                    StringComparison.OrdinalIgnoreCase)) != 1)
            {
                return false;
            }

            if (_restoreEvidence.TryGetValue(item.RecycleBinIdentity!, out var evidence))
            {
                SetCurrent(evidence);
            }
            else
            {
                _entries[item.OriginalPath] = FileSystemEntryKind.File;
            }

            return true;
        }

        public bool DeleteForever(RecycledItem item) => throw new NotSupportedException();

        public void Empty() => throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();
    }
}
