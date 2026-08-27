using System.IO.Compression;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.Archives;

namespace Filekin.Infrastructure.Windows.Tests.Archives;

/// <summary>
/// Compression against a real disk, including the round trip that proves <c>/zip</c> and
/// <c>/unzip</c> agree with each other.
/// </summary>
[TestClass]
public sealed class ZipCompressorTests
{
    private static readonly string[] WrappedEntries = ["photos/a.txt", "photos/raw/b.txt"];
    private static readonly string[] UnwrappedEntries = ["a.txt", "raw/b.txt"];
    private static readonly string[] LooseEntries = ["one.txt", "two.txt"];

    private string _root = string.Empty;
    private string _source = string.Empty;
    private RecordingOperations _operations = new();
    private ZipCompressor _compressor = null!;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Compress-{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "photos");
        _ = Directory.CreateDirectory(Path.Combine(_source, "raw"));
        File.WriteAllText(Path.Combine(_source, "a.txt"), "A");
        File.WriteAllText(Path.Combine(_source, "raw", "b.txt"), "B");
        _operations = new RecordingOperations();
        _compressor = new ZipCompressor(_operations);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [TestMethod]
    public async Task AFolderKeepsItsOwnNameInsideTheArchive()
    {
        var output = Path.Combine(_root, "photos.zip");
        var plan = ZipPlanner.Create([_source], output);

        _ = await _compressor.CompressAsync(plan);

        CollectionAssert.AreEquivalent(WrappedEntries, ReadEntryNames(output));
    }

    [TestMethod]
    public async Task NoRootStoresTheContentsWithoutTheFolder()
    {
        var output = Path.Combine(_root, "photos.zip");
        var plan = ZipPlanner.Create([_source], output, includeRoot: false);

        _ = await _compressor.CompressAsync(plan);

        CollectionAssert.AreEquivalent(UnwrappedEntries, ReadEntryNames(output));
    }

    [TestMethod]
    public async Task SeveralFilesLandAtTheArchiveRoot()
    {
        var output = Path.Combine(_root, "bits.zip");
        var one = Path.Combine(_root, "one.txt");
        var two = Path.Combine(_root, "two.txt");
        await File.WriteAllTextAsync(one, "1");
        await File.WriteAllTextAsync(two, "2");

        var plan = ZipPlanner.Create([one, two], output);
        _ = await _compressor.CompressAsync(plan);

        CollectionAssert.AreEquivalent(LooseEntries, ReadEntryNames(output));
    }

    /// <summary>
    /// The round trip: what <c>/zip</c> writes, <c>/unzip</c> must put back unchanged — including
    /// the folder, which is the whole reason the root is kept by default.
    /// </summary>
    [TestMethod]
    public async Task ZippingThenUnzippingRestoresTheSameTree()
    {
        var output = Path.Combine(_root, "photos.zip");
        _ = await _compressor.CompressAsync(ZipPlanner.Create([_source], output));

        var destination = Path.Combine(_root, "restored");
        var reader = new ZipArchiveReader();
        var plan = ArchivePlanner.Create(output, destination, reader.ReadEntries(output));
        _ = await new ZipExtractor(_operations).ExtractAsync(plan);

        Assert.AreEqual("A", await File.ReadAllTextAsync(Path.Combine(destination, "photos", "a.txt")));
        Assert.AreEqual("B", await File.ReadAllTextAsync(Path.Combine(destination, "photos", "raw", "b.txt")));
    }

    [TestMethod]
    public async Task AnExistingArchiveIsLeftAloneWhenSkipping()
    {
        var output = Path.Combine(_root, "photos.zip");
        await File.WriteAllTextAsync(output, "not really a zip");

        var plan = ZipPlanner.Create([_source], output, collisions: CollisionPolicy.Skip);
        var outcome = await _compressor.CompressAsync(plan);

        Assert.AreEqual("not really a zip", await File.ReadAllTextAsync(output));
        Assert.AreEqual(1, outcome.Failures.Count);
        StringAssert.Contains(outcome.Failures[0], "-overwrite");
    }

    [TestMethod]
    public async Task AnExistingArchiveGoesToTheRecycleBinWhenOverwriting()
    {
        var output = Path.Combine(_root, "photos.zip");
        await File.WriteAllTextAsync(output, "the old one");

        var plan = ZipPlanner.Create([_source], output, collisions: CollisionPolicy.Overwrite);
        var outcome = await _compressor.CompressAsync(plan);

        CollectionAssert.Contains(_operations.Recycled, output);
        Assert.AreEqual(output, outcome.ReplacedOriginal);
        CollectionAssert.AreEquivalent(WrappedEntries, ReadEntryNames(output));
    }

    /// <summary>
    /// A truncated zip is worse than no zip, because it opens and lies about what is inside.
    /// </summary>
    [TestMethod]
    public async Task CancellationLeavesNoArchiveAtAll()
    {
        // Random bytes do not compress, so this takes real time and the cancel lands mid-archive
        // rather than racing a compression that already finished.
        var big = Path.Combine(_root, "big");
        _ = Directory.CreateDirectory(big);
        var noise = new byte[256 * 1024];
        Random.Shared.NextBytes(noise);
        for (var index = 0; index < 120; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(big, $"f{index}.bin"), noise);
            Random.Shared.NextBytes(noise);
        }

        var output = Path.Combine(_root, "big.zip");
        using var cancellation = new CancellationTokenSource();
        var plan = ZipPlanner.Create([big], output);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(30));

        // TaskCanceledException derives from OperationCanceledException; either is a correct stop.
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => _compressor.CompressAsync(plan, null, cancellation.Token));

        Assert.IsFalse(File.Exists(output), "A cancelled /zip left an archive behind.");
        Assert.IsFalse(File.Exists(output + ".filekin-part"), "The partial file was not cleaned up.");
    }

    [TestMethod]
    public void AnArchiveIsNeverStoredInsideItself()
    {
        var output = Path.Combine(_source, "photos.zip");
        File.WriteAllText(output, "placeholder");

        var plan = ZipPlanner.Create([_source], output);

        Assert.IsFalse(
            plan.Entries.Any(entry => entry.SourcePath.Equals(output, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task UndoRemovesTheArchiveItCreated()
    {
        var output = Path.Combine(_root, "photos.zip");
        var outcome = await _compressor.CompressAsync(ZipPlanner.Create([_source], output));
        Assert.IsTrue(File.Exists(output));

        var undo = new ZipCompressionUndo(new EmptyRecycleBin());
        _ = await undo.UndoAsync(outcome);

        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public async Task AMissingSourceIsReportedRatherThanIgnored()
    {
        var output = Path.Combine(_root, "gone.zip");
        var plan = ZipPlanner.Create([Path.Combine(_root, "not-here")], output);

        Assert.AreEqual(1, plan.Skipped.Count);
        Assert.IsTrue(plan.IsEmpty);

        var outcome = await _compressor.CompressAsync(plan);
        Assert.AreEqual(0, outcome.FilesStored);
    }

    private static string[] ReadEntryNames(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return [.. archive.Entries.Select(entry => entry.FullName)];
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public InlineProgress(Action<T> onReport) => _onReport = onReport;

        public void Report(T value) => _onReport(value);
    }

    private sealed class RecordingOperations : IFileSystemOperations
    {
        public List<string> Recycled { get; } = [];

        public FileSystemEntryKind GetKind(string path) =>
            Directory.Exists(path) ? FileSystemEntryKind.Directory
            : File.Exists(path) ? FileSystemEntryKind.File
            : FileSystemEntryKind.None;

        public void Copy(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public void Recycle(string path)
        {
            Recycled.Add(path);
            File.Delete(path);
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
