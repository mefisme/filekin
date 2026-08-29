using Filekin.Core.Archives;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.Archives;

namespace Filekin.Infrastructure.Windows.Tests.Archives;

[TestClass]
public sealed class ZipExtractionUndoBatchTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Filekin.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task BatchUndoReversesArchivesInOppositeExecutionOrder()
    {
        var path = Path.Combine(_root, "shared.txt");
        await File.WriteAllTextAsync(path, "written by second archive");

        var recycledFirstOutput = new RecycledItem(
            "shared.txt",
            path,
            DateTime.Now,
            SizeBytes: 24,
            IsDirectory: false);
        var recycleBin = new RestoringRecycleBin(recycledFirstOutput, "written by first archive");
        var first = new ExtractionOutcome("first.zip", _root, [path], [], [], 0, []);
        var second = new ExtractionOutcome("second.zip", _root, [path], [], [path], 0, []);

        var message = await new ZipExtractionUndo(recycleBin).UndoAsync(
            new ExtractionBatchOutcome([first, second]));

        Assert.IsFalse(File.Exists(path), "Undo left an output created by the first archive behind.");
        Assert.AreEqual(1, recycleBin.RestoreCount);
        StringAssert.StartsWith(message, "Undid 2 archives");
    }

    private sealed class RestoringRecycleBin : IRecycleBin
    {
        private readonly RecycledItem _item;
        private readonly string _content;
        private bool _available = true;

        public RestoringRecycleBin(RecycledItem item, string content)
        {
            _item = item;
            _content = content;
        }

        public int RestoreCount { get; private set; }

        public IReadOnlyList<RecycledItem> List() => _available ? [_item] : [];

        public bool Restore(RecycledItem item)
        {
            if (!_available || item != _item)
            {
                return false;
            }

            File.WriteAllText(item.OriginalPath, _content);
            _available = false;
            RestoreCount++;
            return true;
        }

        public bool DeleteForever(RecycledItem item) => false;

        public void Empty() => _available = false;
    }
}
