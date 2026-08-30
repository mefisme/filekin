using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.Archives;

namespace Filekin.Infrastructure.Windows.Tests.Archives;

/// <summary>
/// Extraction against real zips on a real disk. The Recycle Bin is faked so these stay CI-safe;
/// the real-bin path is covered separately by the interactive-shell tests.
/// </summary>
[TestClass]
public sealed class ZipExtractorTests
{
    private string _root = string.Empty;
    private string _destination = string.Empty;
    private RecordingOperations _operations = new();
    private ZipExtractor _extractor = null!;
    private readonly ZipArchiveReader _reader = new();

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Zip-{Guid.NewGuid():N}");
        _destination = Path.Combine(_root, "out");
        _ = Directory.CreateDirectory(_root);
        _operations = new RecordingOperations();
        _extractor = new ZipExtractor(_operations);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A cleanup failure must not mask the assertion that already ran.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [TestMethod]
    public async Task AWrappedArchiveDoesNotProduceADoubledFolder()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "A"), ("photos/b.txt", "B"));

        var outcome = await ExtractAsync(archive);

        Assert.AreEqual(Path.Combine(_destination, "photos"), outcome.TargetRoot);
        Assert.IsTrue(File.Exists(Path.Combine(_destination, "photos", "a.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_destination, "photos", "photos")));
    }

    [TestMethod]
    public async Task ALooseArchiveGetsOneFolderNamedAfterIt()
    {
        var archive = CreateArchive("holiday.zip", ("a.txt", "A"), ("b.txt", "B"));

        var outcome = await ExtractAsync(archive);

        Assert.AreEqual(Path.Combine(_destination, "holiday"), outcome.TargetRoot);
        Assert.IsTrue(File.Exists(Path.Combine(_destination, "holiday", "a.txt")));
    }

    [TestMethod]
    public async Task NoRootPutsTheContentStraightIntoTheDestination()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "A"));

        var outcome = await ExtractAsync(archive, UnzipLayout.NoRoot);

        Assert.AreEqual(_destination, outcome.TargetRoot);
        Assert.IsTrue(File.Exists(Path.Combine(_destination, "a.txt")));
    }

    [TestMethod]
    public async Task ADestinationThatDoesNotExistYetIsCreated()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "A"));
        var fresh = Path.Combine(_root, "brand", "new", "place");

        var entries = _reader.ReadEntries(archive);
        var plan = ArchivePlanner.Create(archive, fresh, entries);
        var outcome = await _extractor.ExtractAsync(plan);

        Assert.IsTrue(File.Exists(Path.Combine(fresh, "photos", "a.txt")));
        CollectionAssert.Contains(outcome.CreatedDirectories.ToArray(), Path.Combine(fresh, "photos"));
    }

    [TestMethod]
    public async Task SkipLeavesAnExistingFileAlone()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "from archive"));
        var target = Path.Combine(_destination, "photos", "a.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "mine");

        var outcome = await ExtractAsync(archive, collisions: CollisionPolicy.Skip);

        Assert.AreEqual("mine", await File.ReadAllTextAsync(target));
        Assert.AreEqual(1, outcome.SkippedCount);
        Assert.AreEqual(0, _operations.Recycled.Count);
    }

    [TestMethod]
    public async Task OverwriteSendsTheOriginalToTheRecycleBinBeforeReplacingIt()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "from archive"));
        var target = Path.Combine(_destination, "photos", "a.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "mine");

        var outcome = await ExtractAsync(archive, collisions: CollisionPolicy.Overwrite);

        Assert.AreEqual("from archive", await File.ReadAllTextAsync(target));
        CollectionAssert.Contains(_operations.Recycled, target);
        CollectionAssert.Contains(outcome.ReplacedOriginals.ToArray(), target);
        var replacement = outcome.ReplacementEvidence.Single();
        Assert.IsTrue(replacement.CanRestore);
        Assert.AreEqual("test-recycle:1", replacement.RecycledItem!.RecycleBinIdentity);
        AssertOutputEvidence(outcome.CreatedFileEvidence.Single(evidence => evidence.Path == target));
    }

    [TestMethod]
    public async Task UnidentifiedReplacementIsRecordedWithoutClaimingRestore()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "from archive"));
        var target = Path.Combine(_destination, "photos", "a.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "mine");
        _operations.ExposeExactRecycleIdentity = false;

        var outcome = await ExtractAsync(archive, collisions: CollisionPolicy.Overwrite);

        var replacement = outcome.ReplacementEvidence.Single();
        Assert.IsFalse(replacement.CanRestore);
        StringAssert.Contains(replacement.RestoreUnavailableReason, "Test double");
    }

    /// <summary>
    /// The archive path-traversal defence, end to end: a hostile entry name must not write outside
    /// the destination, whatever the archive claims.
    /// </summary>
    [TestMethod]
    public async Task AnEntryClimbingOutOfTheFolderNeverLandsOutsideIt()
    {
        var archive = CreateArchive("evil.zip", ("../../escaped.txt", "boom"), ("safe.txt", "fine"));

        var outcome = await ExtractAsync(archive);

        Assert.IsFalse(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_destination, "evil", "safe.txt")));
        Assert.AreEqual(1, outcome.CreatedFiles.Count);
    }

    [TestMethod]
    public async Task ProgressIsReportedAndEndsAtTheTotal()
    {
        var archive = CreateArchive(
            "many.zip",
            [.. Enumerable.Range(0, 40).Select(index => ($"many/file{index}.txt", new string('x', 1000)))]);

        var reports = new List<ExtractionProgress>();
        var entries = _reader.ReadEntries(archive);
        var plan = ArchivePlanner.Create(archive, _destination, entries);
        _ = await _extractor.ExtractAsync(plan, new InlineProgress<ExtractionProgress>(reports.Add));

        Assert.IsTrue(reports.Count > 0, "No progress was published.");
        Assert.AreEqual(40, reports[^1].FilesDone);
        Assert.AreEqual(40, reports[^1].FilesTotal);
    }

    [TestMethod]
    public async Task UndoRemovesEverythingTheExtractionCreated()
    {
        var archive = CreateArchive("photos.zip", ("photos/a.txt", "A"), ("photos/raw/b.txt", "B"));

        var outcome = await ExtractAsync(archive);
        Assert.IsTrue(Directory.Exists(Path.Combine(_destination, "photos")));

        var undo = new ZipExtractionUndo(new EmptyRecycleBin());
        _ = await undo.UndoAsync(outcome);

        Assert.IsFalse(Directory.Exists(Path.Combine(_destination, "photos")));
    }

    /// <summary>
    /// Undo must not take a folder the user already had, even when the extraction wrote into it.
    /// </summary>
    [TestMethod]
    public async Task UndoLeavesAFolderThatWasAlreadyThere()
    {
        var mine = Path.Combine(_destination, "photos");
        _ = Directory.CreateDirectory(mine);
        await File.WriteAllTextAsync(Path.Combine(mine, "mine.txt"), "keep me");

        var archive = CreateArchive("photos.zip", ("photos/a.txt", "A"));
        var outcome = await ExtractAsync(archive);

        var undo = new ZipExtractionUndo(new EmptyRecycleBin());
        _ = await undo.UndoAsync(outcome);

        Assert.IsTrue(Directory.Exists(mine), "Undo removed a folder it did not create.");
        Assert.IsTrue(File.Exists(Path.Combine(mine, "mine.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(mine, "a.txt")));
    }

    [TestMethod]
    public async Task CancellationStopsEarlyAndStillReportsWhatWasWritten()
    {
        var archive = CreateArchive(
            "many.zip",
            [.. Enumerable.Range(0, 200).Select(index => ($"many/file{index}.txt", new string('x', 5000)))]);

        using var cancellation = new CancellationTokenSource();
        var entries = _reader.ReadEntries(archive);
        var plan = ArchivePlanner.Create(archive, _destination, entries);

        var progress = new InlineProgress<ExtractionProgress>(report =>
        {
            if (report.FilesDone > 0)
            {
                cancellation.Cancel();
            }
        });

        var outcome = await _extractor.ExtractAsync(plan, progress, cancellation.Token);

        Assert.IsTrue(outcome.CreatedFiles.Count < 200, "Cancellation did not stop the extraction.");
        Assert.AreEqual(
            outcome.CreatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            outcome.CreatedFileEvidence.Count);

        // Whatever was written is recorded, so a cancelled extraction is still fully undoable.
        var undo = new ZipExtractionUndo(new EmptyRecycleBin());
        _ = await undo.UndoAsync(outcome);
        Assert.IsFalse(Directory.Exists(Path.Combine(_destination, "many")));
    }

    private Task<ExtractionOutcome> ExtractAsync(
        string archive,
        UnzipLayout layout = UnzipLayout.NewFolder,
        CollisionPolicy collisions = CollisionPolicy.Skip)
    {
        var entries = _reader.ReadEntries(archive);
        var plan = ArchivePlanner.Create(archive, _destination, entries, layout, collisions);
        return _extractor.ExtractAsync(plan);
    }

    private string CreateArchive(string name, params (string Path, string Content)[] entries)
    {
        var archivePath = Path.Combine(_root, name);
        using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (path, content) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return archivePath;
    }

    private static void AssertOutputEvidence(ArchiveOutputEvidence evidence)
    {
        Assert.AreEqual(true, evidence.ExistedAtCompletion);
        Assert.IsTrue(evidence.CanVerify);
        Assert.AreEqual(new FileInfo(evidence.Path).Length, evidence.Length);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidence.Path))),
            evidence.Sha256);
    }

    /// <summary>
    /// Publishes on the caller's thread. <see cref="Progress{T}"/> posts asynchronously, which lets a
    /// short extraction finish before its reports arrive and makes the assertion race.
    /// </summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public InlineProgress(Action<T> onReport) => _onReport = onReport;

        public void Report(T value) => _onReport(value);
    }

    /// <summary>Records what would have gone to the Recycle Bin, and deletes it as the bin would.</summary>
    private sealed class RecordingOperations : IFileSystemOperations
    {
        public List<string> Recycled { get; } = [];

        public bool ExposeExactRecycleIdentity { get; set; } = true;

        public FileSystemEntryKind GetKind(string path) =>
            Directory.Exists(path) ? FileSystemEntryKind.Directory
            : File.Exists(path) ? FileSystemEntryKind.File
            : FileSystemEntryKind.None;

        public List<string> Directories { get; } = [];

        public void CreateDirectory(string path) => Directories.Add(path);

        public void Copy(string sourcePath, string destinationPath) =>
            throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath) =>
            throw new NotSupportedException();

        public RecycleOutcome Recycle(string path)
        {
            Recycled.Add(path);
            File.Delete(path);
            if (ExposeExactRecycleIdentity)
            {
                return RecycleOutcome.Restorable(new RecycledItem(
                    Path.GetFileName(path),
                    path,
                    DateTime.Now,
                    SizeBytes: null,
                    IsDirectory: false,
                    RecycleBinIdentity: $"test-recycle:{Recycled.Count}"));
            }

            return RecycleOutcome.Informational(
                path,
                FileSystemEntryKind.File,
                "Test double does not expose Recycle Bin identity.");
        }
    }

    private sealed class EmptyRecycleBin : IRecycleBin
    {
        public IReadOnlyList<RecycledItem> List() => [];

        public bool Restore(RecycledItem item) => false;

        public bool DeleteForever(RecycledItem item) => false;

        public void Empty()
        {
        }
    }
}
