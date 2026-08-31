using System.Text.Json;
using Filekin.Core.Archives;
using Filekin.Core.Commands.App;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class OperationUndoCoordinatorTests
{
    [TestMethod]
    public async Task ExactRelocationUndoAtomicallyStoresTerminalPayloadAndLifecycle()
    {
        var source = Path.GetFullPath("source.txt");
        var destination = Path.GetFullPath("destination.txt");
        var fileSystem = new FakeFileSystem((destination, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        var payload = new RelocationOperationPayload(
            [new PathRelocation(source, destination)],
            Array.Empty<AppCommandFailure>());
        var entry = Entry("move", payload);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem);
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Undone, result.Outcome);
        Assert.IsTrue(result.MayHaveChangedFileSystem);
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(source));
        Assert.AreEqual(FileSystemEntryKind.None, fileSystem.GetKind(destination));
        var stored = await journal.FindAsync(entry.Id);
        Assert.AreEqual(OperationUndoState.Undone, stored?.UndoState);
        var updated = JsonSerializer.Deserialize<RelocationOperationPayload>(stored!.PayloadJson);
        Assert.IsNotNull(updated);
        Assert.IsEmpty(updated.PendingRelocations);
    }

    [TestMethod]
    public async Task PartialRelocationRetryUsesOnlyPersistedRemainingWork()
    {
        var firstSource = Path.GetFullPath("first-source.txt");
        var firstDestination = Path.GetFullPath("first-destination.txt");
        var secondSource = Path.GetFullPath("second-source.txt");
        var secondDestination = Path.GetFullPath("second-destination.txt");
        var fileSystem = new FakeFileSystem(
            (firstDestination, FileSystemEntryKind.File),
            (secondDestination, FileSystemEntryKind.File));
        fileSystem.MoveFailures.Add(firstDestination);
        var journal = new InMemoryOperationJournal();
        var payload = new RelocationOperationPayload(
            [
                new PathRelocation(firstSource, firstDestination),
                new PathRelocation(secondSource, secondDestination),
            ],
            Array.Empty<AppCommandFailure>());
        var entry = Entry("move", payload);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem);
        var partial = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.PartiallyUndone, partial.Outcome);
        var storedPartial = await journal.FindAsync(entry.Id);
        Assert.AreEqual(OperationUndoState.PartiallyUndone, storedPartial?.UndoState);
        var pending = JsonSerializer.Deserialize<RelocationOperationPayload>(storedPartial!.PayloadJson);
        Assert.IsNotNull(pending);
        CollectionAssert.AreEqual(
            new[] { payload.Relocations[0] },
            pending.PendingRelocations.ToArray());

        fileSystem.MoveFailures.Clear();
        var retried = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Undone, retried.Outcome);
        Assert.AreEqual(OperationUndoState.Undone, (await journal.FindAsync(entry.Id))?.UndoState);
        Assert.AreEqual(1, fileSystem.MoveCounts[firstDestination]);
        Assert.AreEqual(1, fileSystem.MoveCounts[secondDestination]);
    }

    [TestMethod]
    public async Task RelocationCollisionRequestsDecisionWithoutChangingJournalOrDisk()
    {
        var source = Path.GetFullPath("occupied.txt");
        var destination = Path.GetFullPath("moved.txt");
        var fileSystem = new FakeFileSystem(
            (source, FileSystemEntryKind.File),
            (destination, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        var entry = Entry(
            "rename",
            new RelocationOperationPayload(
                [new PathRelocation(source, destination)],
                Array.Empty<AppCommandFailure>()));
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem);
        var evaluation = await coordinator.EvaluateAsync(entry.Id);
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoAvailability.NeedsDecision, evaluation.Availability);
        Assert.AreEqual(RelocationUndoSafety.NeedsConflictResolution, evaluation.Relocation?.Safety);
        Assert.AreEqual(CoordinatedUndoOutcome.NeedsDecision, result.Outcome);
        Assert.AreEqual(entry, await journal.FindAsync(entry.Id));
        Assert.AreEqual(0, fileSystem.TotalMoves);
    }

    [TestMethod]
    public async Task DeclaredKindRejectsAnotherOperationsPayloadAndBecomesUnavailable()
    {
        var source = Path.GetFullPath("source.txt");
        var destination = Path.GetFullPath("destination.txt");
        var fileSystem = new FakeFileSystem((destination, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        var wrongPayload = new RelocationOperationPayload(
            [new PathRelocation(source, destination)],
            Array.Empty<AppCommandFailure>());
        var entry = Entry("toss", wrongPayload);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem);
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Unavailable, result.Outcome);
        Assert.AreEqual(OperationUndoState.Unavailable, (await journal.FindAsync(entry.Id))?.UndoState);
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(destination));
        Assert.AreEqual(0, fileSystem.TotalMoves);
    }

    [TestMethod]
    public async Task EditedArchiveOutputRequiresExplicitReviewedDecision()
    {
        var output = Path.GetFullPath("edited.zip");
        var completed = ArchiveOutputEvidence.Captured(
            output,
            10,
            new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc),
            "AAA");
        var current = ArchiveOutputEvidence.Captured(
            output,
            12,
            new DateTime(2026, 8, 30, 10, 5, 0, DateTimeKind.Utc),
            "BBB");
        var fileSystem = new FakeFileSystem((output, FileSystemEntryKind.File));
        var evidence = new FakeEvidenceReader((output, current));
        var journal = new InMemoryOperationJournal();
        var payload = new ArchiveUndoPayload(
            ArchiveUndoOperationKind.Compression,
            [new ArchiveUndoArchiveWork(0, output, [completed], [], [])]);
        var entry = Entry("zip", payload);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem, evidence);
        var evaluation = await coordinator.EvaluateAsync(entry.Id);
        var withoutDecision = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoAvailability.NeedsDecision, evaluation.Availability);
        Assert.AreEqual(CoordinatedUndoOutcome.NeedsDecision, withoutDecision.Outcome);
        Assert.AreEqual(entry, await journal.FindAsync(entry.Id));
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(output));

        var conflict = evaluation.Archive!.EditedOutputs.Single();
        var keep = ArchiveEditedOutputResolution.FromConflict(
            conflict,
            ArchiveEditedOutputDecision.KeepEdited);
        var result = await coordinator.UndoAsync(
            entry.Id,
            new OperationUndoDecisions([keep]));

        Assert.AreEqual(CoordinatedUndoOutcome.PartiallyUndone, result.Outcome);
        Assert.AreEqual(OperationUndoState.PartiallyUndone, (await journal.FindAsync(entry.Id))?.UndoState);
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(output));
        var updated = JsonSerializer.Deserialize<ArchiveUndoPayload>(result.Entry!.PayloadJson);
        Assert.IsNotNull(updated);
        Assert.IsFalse(updated.HasPendingWork);
    }

    [TestMethod]
    public async Task TossRestoreUsesExactIdentityAndStoresCompletedPayload()
    {
        var original = Path.GetFullPath("recycled.txt");
        var recycled = new RecycledItem("recycled.txt", original, null, 3, false, "opaque-1");
        var fileSystem = new FakeFileSystem();
        var recycleBin = new FakeRecycleBin(fileSystem, recycled);
        var journal = new InMemoryOperationJournal();
        var payload = new TossOperationPayload(
            [new TossedItem(original, recycled.Name, false, recycled.RecycleBinIdentity, null)],
            Array.Empty<AppCommandFailure>());
        var entry = Entry("toss", payload);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(journal, fileSystem, recycleBin: recycleBin);
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Undone, result.Outcome);
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(original));
        Assert.AreEqual("opaque-1", recycleBin.RestoredIdentities.Single());
        var updated = JsonSerializer.Deserialize<TossOperationPayload>(result.Entry!.PayloadJson);
        Assert.IsNotNull(updated);
        Assert.IsEmpty(updated.PendingItems);
    }

    [TestMethod]
    public async Task ExistingLegacyZipJournalPayloadIsConvertedAtTheCoordinatorBoundary()
    {
        var output = Path.GetFullPath("legacy.zip");
        var evidence = ArchiveOutputEvidence.Captured(
            output,
            20,
            new DateTime(2026, 8, 30, 11, 0, 0, DateTimeKind.Utc),
            "CCC");
        var fileSystem = new FakeFileSystem((output, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        var legacy = new CompressionOutcome(output, 1, 10, 20, null, [], evidence);
        var entry = Entry("zip", legacy);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(
            journal,
            fileSystem,
            new FakeEvidenceReader((output, evidence)));
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Undone, result.Outcome);
        Assert.AreEqual(FileSystemEntryKind.None, fileSystem.GetKind(output));
        Assert.IsNotNull(JsonSerializer.Deserialize<ArchiveUndoPayload>(result.Entry!.PayloadJson));
    }

    [TestMethod]
    public async Task ExistingLegacyUnzipBatchPayloadIsConvertedAtTheCoordinatorBoundary()
    {
        var output = Path.GetFullPath("legacy-extracted.txt");
        var evidence = ArchiveOutputEvidence.Captured(
            output,
            8,
            new DateTime(2026, 8, 30, 11, 10, 0, DateTimeKind.Utc),
            "DDD");
        var fileSystem = new FakeFileSystem((output, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        var legacy = new ExtractionBatchOutcome([
            new ExtractionOutcome(
                Path.GetFullPath("legacy.zip"),
                Path.GetDirectoryName(output)!,
                [output],
                [],
                [],
                0,
                [],
                [evidence]),
        ]);
        var entry = Entry("unzip", legacy);
        await journal.RecordAsync(entry);

        using var coordinator = CreateCoordinator(
            journal,
            fileSystem,
            new FakeEvidenceReader((output, evidence)));
        var result = await coordinator.UndoAsync(entry.Id);

        Assert.AreEqual(CoordinatedUndoOutcome.Undone, result.Outcome);
        Assert.AreEqual(FileSystemEntryKind.None, fileSystem.GetKind(output));
        Assert.IsNotNull(JsonSerializer.Deserialize<ArchiveUndoPayload>(result.Entry!.PayloadJson));
    }

    [TestMethod]
    public async Task MissingExactEntryDoesNotFallThroughToAnotherCandidate()
    {
        var destination = Path.GetFullPath("destination.txt");
        var fileSystem = new FakeFileSystem((destination, FileSystemEntryKind.File));
        var journal = new InMemoryOperationJournal();
        await journal.RecordAsync(Entry(
            "move",
            new RelocationOperationPayload(
                [new PathRelocation(Path.GetFullPath("source.txt"), destination)],
                Array.Empty<AppCommandFailure>())));

        using var coordinator = CreateCoordinator(journal, fileSystem);
        var result = await coordinator.UndoAsync(Guid.NewGuid());

        Assert.AreEqual(CoordinatedUndoOutcome.NotFound, result.Outcome);
        Assert.AreEqual(FileSystemEntryKind.File, fileSystem.GetKind(destination));
        Assert.AreEqual(0, fileSystem.TotalMoves);
    }

    private static OperationUndoCoordinator CreateCoordinator(
        IOperationJournal journal,
        FakeFileSystem fileSystem,
        FakeEvidenceReader? evidence = null,
        FakeRecycleBin? recycleBin = null) =>
        new(
            journal,
            fileSystem,
            recycleBin ?? new FakeRecycleBin(fileSystem),
            evidence ?? new FakeEvidenceReader(),
            new FakeArchiveStorage(fileSystem));

    private static JournalEntry Entry<T>(string kind, T payload) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            kind,
            $"Test {kind}",
            JsonSerializer.Serialize(payload),
            OperationUndoState.Undoable);

    private sealed class FakeFileSystem : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystem(params (string Path, FileSystemEntryKind Kind)[] entries)
        {
            foreach (var entry in entries)
            {
                _entries[entry.Path] = entry.Kind;
            }
        }

        public HashSet<string> MoveFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> MoveCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int TotalMoves => MoveCounts.Values.Sum();

        public FileSystemEntryKind GetKind(string path) =>
            _entries.TryGetValue(path, out var kind) ? kind : FileSystemEntryKind.None;

        public void CreateDirectory(string path) => Set(path, FileSystemEntryKind.Directory);

        public void Copy(string sourcePath, string destinationPath) =>
            Set(destinationPath, GetKind(sourcePath));

        public void Move(string sourcePath, string destinationPath)
        {
            if (MoveFailures.Contains(sourcePath))
            {
                throw new IOException("Simulated move failure.");
            }

            var kind = GetKind(sourcePath);
            if (kind == FileSystemEntryKind.None || GetKind(destinationPath) != FileSystemEntryKind.None)
            {
                throw new IOException("The move is not valid.");
            }

            MoveCounts[sourcePath] = MoveCounts.GetValueOrDefault(sourcePath) + 1;
            Set(sourcePath, FileSystemEntryKind.None);
            Set(destinationPath, kind);
        }

        public RecycleOutcome Recycle(string path)
        {
            var kind = GetKind(path);
            if (kind == FileSystemEntryKind.None)
            {
                throw new IOException("The item is missing.");
            }

            Set(path, FileSystemEntryKind.None);
            return RecycleOutcome.Restorable(new RecycledItem(
                Path.GetFileName(path),
                path,
                DateTime.UtcNow,
                kind == FileSystemEntryKind.File ? 1 : null,
                kind == FileSystemEntryKind.Directory,
                $"recycled-{Guid.NewGuid():N}"));
        }

        public void Set(string path, FileSystemEntryKind kind)
        {
            if (kind == FileSystemEntryKind.None)
            {
                _entries.Remove(path);
            }
            else
            {
                _entries[path] = kind;
            }
        }
    }

    private sealed class FakeRecycleBin : IRecycleBin
    {
        private readonly FakeFileSystem _fileSystem;
        private readonly List<RecycledItem> _items;

        public FakeRecycleBin(FakeFileSystem fileSystem, params RecycledItem[] items)
        {
            _fileSystem = fileSystem;
            _items = [.. items];
        }

        public List<string> RestoredIdentities { get; } = [];

        public IReadOnlyList<RecycledItem> List() => [.. _items];

        public bool Restore(RecycledItem item)
        {
            var found = _items.SingleOrDefault(candidate => candidate.RecycleBinIdentity == item.RecycleBinIdentity);
            if (found is null)
            {
                return false;
            }

            _items.Remove(found);
            _fileSystem.Set(
                found.OriginalPath,
                found.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File);
            RestoredIdentities.Add(found.RecycleBinIdentity!);
            return true;
        }

        public bool DeleteForever(RecycledItem item) => _items.Remove(item);

        public void Empty() => _items.Clear();
    }

    private sealed class FakeEvidenceReader : IArchiveOutputEvidenceReader
    {
        private readonly Dictionary<string, ArchiveOutputEvidence> _evidence =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeEvidenceReader(params (string Path, ArchiveOutputEvidence Evidence)[] evidence)
        {
            foreach (var item in evidence)
            {
                _evidence[item.Path] = item.Evidence;
            }
        }

        public ArchiveOutputEvidence Read(string path) =>
            _evidence.TryGetValue(path, out var evidence)
                ? evidence
                : ArchiveOutputEvidence.Absent(path);
    }

    private sealed class FakeArchiveStorage(FakeFileSystem fileSystem) : IArchiveUndoStorage
    {
        public void DeleteFile(string path) => fileSystem.Set(path, FileSystemEntryKind.None);

        public ArchiveDirectoryRemoval RemoveDirectoryIfEmpty(string path)
        {
            if (fileSystem.GetKind(path) == FileSystemEntryKind.None)
            {
                return ArchiveDirectoryRemoval.Missing;
            }

            fileSystem.Set(path, FileSystemEntryKind.None);
            return ArchiveDirectoryRemoval.Removed;
        }
    }
}
