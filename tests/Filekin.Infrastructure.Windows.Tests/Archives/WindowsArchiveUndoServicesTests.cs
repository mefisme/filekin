using System.Security.Cryptography;
using System.Text;
using Filekin.Core.Archives;
using Filekin.Infrastructure.Windows.Archives;

namespace Filekin.Infrastructure.Windows.Tests.Archives;

[TestClass]
public sealed class WindowsArchiveUndoServicesTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Archive-Undo-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_root);
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
    public void ReaderCapturesTheCurrentLengthTimestampAndSha256()
    {
        var path = Path.Combine(_root, "output.txt");
        const string content = "archive output";
        File.WriteAllText(path, content);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        var evidence = new WindowsArchiveOutputEvidenceReader().Read(path);

        Assert.AreEqual(true, evidence.ExistedAtCompletion);
        Assert.AreEqual(new FileInfo(path).Length, evidence.Length);
        Assert.AreEqual(File.GetLastWriteTimeUtc(path), evidence.LastWriteTimeUtc);
        Assert.AreEqual(expectedHash, evidence.Sha256);
    }

    [TestMethod]
    public void ReaderReportsAMissingFileExplicitly()
    {
        var path = Path.Combine(_root, "missing.txt");

        var evidence = new WindowsArchiveOutputEvidenceReader().Read(path);

        Assert.AreEqual(false, evidence.ExistedAtCompletion);
        Assert.IsTrue(evidence.CanVerify);
    }

    [TestMethod]
    public void StorageRemovesOnlyEmptyDirectories()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var occupied = Directory.CreateDirectory(Path.Combine(_root, "occupied")).FullName;
        File.WriteAllText(Path.Combine(occupied, "user.txt"), "keep");
        var storage = new WindowsArchiveUndoStorage();

        var removed = storage.RemoveDirectoryIfEmpty(empty);
        var kept = storage.RemoveDirectoryIfEmpty(occupied);

        Assert.AreEqual(ArchiveDirectoryRemoval.Removed, removed);
        Assert.IsFalse(Directory.Exists(empty));
        Assert.AreEqual(ArchiveDirectoryRemoval.NotEmpty, kept);
        Assert.IsTrue(File.Exists(Path.Combine(occupied, "user.txt")));
    }
}
