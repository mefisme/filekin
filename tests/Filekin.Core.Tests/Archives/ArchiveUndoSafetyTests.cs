using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.Archives;

[TestClass]
public sealed class ArchiveUndoSafetyTests
{
    private static readonly string[] ArchiveOrder =
        [@"D:\Archives\first.zip", @"D:\Archives\second.zip"];
    private static readonly int[] ArchiveIndexes = [0, 1];
    private static readonly DateTime WrittenAt =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void UnchangedOutputAndExactReplacementAreReady()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(output.Path, "bin:a");
        var fixture = Fixture(output, replacement);

        var assessment = fixture.Evaluator.Evaluate(Batch(output, replacement));

        Assert.AreEqual(ArchiveUndoSafety.Ready, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Unchanged, assessment.Archives.Single().Outputs.Single().State);
        Assert.AreEqual(
            ArchiveReplacementState.Ready,
            assessment.Archives.Single().Replacements.Single().State);
        Assert.IsEmpty(assessment.EditedOutputs);
        Assert.IsEmpty(assessment.Issues);
    }

    [TestMethod]
    public void HashChangeWithSameLengthAndTimestampRequiresExplicitEditedOutputDecision()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var fixture = Fixture(output);
        fixture.Reader.Set(Captured(output.Path, "BB"));

        var assessment = fixture.Evaluator.Evaluate(Batch(output));

        Assert.AreEqual(ArchiveUndoSafety.NeedsEditedOutputDecision, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Edited, assessment.Archives.Single().Outputs.Single().State);
        Assert.AreEqual(
            ArchiveEditedOutputDecision.KeepEdited,
            ArchiveEditedOutputConflict.DefaultChoice);
        CollectionAssert.AreEqual(
            new[]
            {
                ArchiveEditedOutputDecision.KeepEdited,
                ArchiveEditedOutputDecision.RecycleEdited,
                ArchiveEditedOutputDecision.Cancel,
            },
            ArchiveEditedOutputConflict.Choices.ToArray());
    }

    [TestMethod]
    public void MissingOutputIsDetectedWithoutMakingUndoUnavailable()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var fixture = Fixture(output);
        fixture.FileSystem.Remove(output.Path);

        var assessment = fixture.Evaluator.Evaluate(Batch(output));

        Assert.AreEqual(ArchiveUndoSafety.Ready, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Missing, assessment.Archives.Single().Outputs.Single().State);
    }

    [TestMethod]
    public void UnavailableCompletionFingerprintFailsClosed()
    {
        var output = ArchiveOutputEvidence.Unavailable(@"D:\Work\a.txt", "The file was locked.");
        var fixture = Fixture();

        var assessment = fixture.Evaluator.Evaluate(Batch(output));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Unverifiable, assessment.Archives.Single().Outputs.Single().State);
        Assert.AreEqual(ArchiveUndoIssueKind.OutputEvidenceUnavailable, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void CurrentFingerprintInspectionFailureFailsClosed()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var fixture = Fixture(output);
        fixture.Reader.FailurePath = output.Path;

        var assessment = fixture.Evaluator.Evaluate(Batch(output));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveUndoIssueKind.OutputInspectionFailed, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void PathCreatedAfterAnAbsentWriteIsAnUnresolvedOccupation()
    {
        var output = ArchiveOutputEvidence.Absent(@"D:\Work\a.txt");
        var replacement = Replacement(output.Path, "bin:a");
        var fixture = Fixture(replacement: replacement);
        fixture.FileSystem.AddFile(output.Path);

        var assessment = fixture.Evaluator.Evaluate(Batch(output, replacement));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Unverifiable, assessment.Archives.Single().Outputs.Single().State);
        Assert.AreEqual(ArchiveUndoIssueKind.OriginalPathOccupied, assessment.Issues.Single().Kind);
        Assert.AreEqual(
            ArchiveReplacementState.OriginalPathOccupied,
            assessment.Archives.Single().Replacements.Single().State);
    }

    [TestMethod]
    public void MissingExactReplacementIdentityFailsClosed()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(output.Path, "bin:a");
        var fixture = Fixture(output);

        var assessment = fixture.Evaluator.Evaluate(Batch(output, replacement));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveUndoIssueKind.RecycledOriginalMissing, assessment.Issues.Single().Kind);
        Assert.AreEqual(
            ArchiveReplacementState.RecycledOriginalMissing,
            assessment.Archives.Single().Replacements.Single().State);
    }

    [TestMethod]
    public void DuplicateExactReplacementIdentityIsAmbiguous()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(output.Path, "bin:a");
        var fixture = Fixture(output, replacement);
        fixture.RecycleBin.Add(Recycled(output.Path, "bin:a"));

        var assessment = fixture.Evaluator.Evaluate(Batch(output, replacement));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveUndoIssueKind.RecycledOriginalAmbiguous, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void MissingModernEvidenceMakesLegacyPayloadUnavailable()
    {
        var outcome = new ExtractionOutcome(
            @"D:\Archives\a.zip",
            @"D:\Work",
            [@"D:\Work\a.txt"],
            [],
            [],
            0,
            []);
        var fixture = Fixture();

        var assessment = fixture.Evaluator.Evaluate(new ExtractionBatchOutcome([outcome]));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveUndoIssueKind.OutputEvidenceUnavailable, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void MultiArchiveAssessmentPreservesInvocationExecutionOrder()
    {
        var first = Captured(@"D:\Work\first.txt", "AA");
        var second = Captured(@"D:\Work\second.txt", "BB");
        var fixture = Fixture(first);
        fixture.FileSystem.AddFile(second.Path);
        fixture.Reader.Set(second);
        var batch = new ExtractionBatchOutcome([
            Outcome(@"D:\Archives\first.zip", first),
            Outcome(@"D:\Archives\second.zip", second),
        ]);

        var assessment = fixture.Evaluator.Evaluate(batch);

        CollectionAssert.AreEqual(
            ArchiveOrder,
            assessment.Archives.Select(archive => archive.ArchivePath).ToArray());
        CollectionAssert.AreEqual(
            ArchiveIndexes,
            assessment.Archives.Select(archive => archive.ArchiveIndex).ToArray());
    }

    [TestMethod]
    public void RecycleBinInspectionFailureMakesReplacementUnavailable()
    {
        var output = Captured(@"D:\Work\a.txt", "AA");
        var replacement = Replacement(output.Path, "bin:a");
        var fixture = Fixture(output, replacement);
        fixture.RecycleBin.FailInspection = true;

        var assessment = fixture.Evaluator.Evaluate(Batch(output, replacement));

        Assert.AreEqual(ArchiveUndoSafety.Unavailable, assessment.Safety);
        Assert.AreEqual(ArchiveUndoIssueKind.RecycleBinInspectionFailed, assessment.Issues.Single().Kind);
    }

    [TestMethod]
    public void CompressionUsesTheSameSafetyModel()
    {
        var output = Captured(@"D:\Work\archive.zip", "AA");
        var fixture = Fixture(output);
        var outcome = new CompressionOutcome(
            output.Path,
            filesStored: 2,
            bytesRead: 20,
            archiveBytes: output.Length!.Value,
            replacedOriginal: null,
            failures: [],
            output);

        var assessment = fixture.Evaluator.Evaluate(outcome);

        Assert.AreEqual(ArchiveUndoSafety.Ready, assessment.Safety);
        Assert.AreEqual(ArchiveOutputState.Unchanged, assessment.Archives.Single().Outputs.Single().State);
    }

    private static TestFixture Fixture(
        ArchiveOutputEvidence? output = null,
        ArchiveReplacementEvidence? replacement = null)
    {
        var fileSystem = new FakeFileSystem();
        var reader = new FakeEvidenceReader();
        if (output?.ExistedAtCompletion == true)
        {
            fileSystem.AddFile(output.Path);
            reader.Set(output);
        }

        var recycleBin = new FakeRecycleBin();
        if (replacement?.RecycledItem is not null)
        {
            recycleBin.Add(replacement.RecycledItem);
        }

        return new TestFixture(
            fileSystem,
            reader,
            recycleBin,
            new ArchiveUndoEvaluator(fileSystem, recycleBin, reader));
    }

    private static ExtractionBatchOutcome Batch(
        ArchiveOutputEvidence output,
        ArchiveReplacementEvidence? replacement = null) =>
        new([Outcome(@"D:\Archives\a.zip", output, replacement)]);

    private static ExtractionOutcome Outcome(
        string archivePath,
        ArchiveOutputEvidence output,
        ArchiveReplacementEvidence? replacement = null) =>
        new(
            archivePath,
            @"D:\Work",
            [output.Path],
            [],
            replacement is null ? [] : [replacement.OriginalPath],
            0,
            [],
            [output],
            replacement is null ? [] : [replacement]);

    private static ArchiveOutputEvidence Captured(string path, string hash) =>
        ArchiveOutputEvidence.Captured(path, length: 12, WrittenAt, hash);

    private static ArchiveReplacementEvidence Replacement(string path, string identity) =>
        new(path, Recycled(path, identity), restoreUnavailableReason: null);

    private static RecycledItem Recycled(string path, string identity) =>
        new(Path.GetFileName(path), path, WrittenAt, SizeBytes: 12, IsDirectory: false, identity);

    private sealed record TestFixture(
        FakeFileSystem FileSystem,
        FakeEvidenceReader Reader,
        FakeRecycleBin RecycleBin,
        ArchiveUndoEvaluator Evaluator);

    private sealed class FakeFileSystem : IFileSystemOperations
    {
        private readonly Dictionary<string, FileSystemEntryKind> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path) => _entries[path] = FileSystemEntryKind.File;

        public void Remove(string path) => _entries.Remove(path);

        public FileSystemEntryKind GetKind(string path) => _entries.GetValueOrDefault(path);

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public RecycleOutcome Recycle(string path) => throw new NotSupportedException();
    }

    private sealed class FakeEvidenceReader : IArchiveOutputEvidenceReader
    {
        private readonly Dictionary<string, ArchiveOutputEvidence> _evidence =
            new(StringComparer.OrdinalIgnoreCase);

        public string? FailurePath { get; set; }

        public void Set(ArchiveOutputEvidence evidence) => _evidence[evidence.Path] = evidence;

        public ArchiveOutputEvidence Read(string path)
        {
            if (string.Equals(path, FailurePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The file could not be read.");
            }

            return _evidence.GetValueOrDefault(path) ?? ArchiveOutputEvidence.Absent(path);
        }
    }

    private sealed class FakeRecycleBin : IRecycleBin
    {
        private readonly List<RecycledItem> _items = [];

        public bool FailInspection { get; set; }

        public void Add(RecycledItem item) => _items.Add(item);

        public IReadOnlyList<RecycledItem> List() =>
            FailInspection ? throw new IOException("Recycle Bin unavailable.") : [.. _items];

        public bool Restore(RecycledItem item) => throw new NotSupportedException();

        public bool DeleteForever(RecycledItem item) => throw new NotSupportedException();

        public void Empty() => throw new NotSupportedException();
    }
}
