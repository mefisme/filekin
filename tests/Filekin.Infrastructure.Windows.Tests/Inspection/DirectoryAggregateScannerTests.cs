using System.Diagnostics;
using Filekin.Core.Inspection;
using Filekin.Infrastructure.Windows.Inspection;

namespace Filekin.Infrastructure.Windows.Tests.Inspection;

[TestClass]
public sealed class DirectoryAggregateScannerTests
{
    private string _root = null!;
    private readonly DirectoryAggregateScanner _scanner = new();

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deeper"));
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "b.bin"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "deeper", "c.bin"), new byte[300]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // A recursive delete refuses to walk through a junction, so any reparse point is unlinked
        // first. Deleting the link itself never touches what it points at.
        foreach (var entry in new DirectoryInfo(_root).EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void AFolderTotalsItsWholeTreeWithoutCountingItself()
    {
        var totals = _scanner.Scan([_root], countRootFoldersThemselves: false, Ignore, CancellationToken.None);

        Assert.AreEqual(600, totals.Bytes);
        Assert.AreEqual(3, totals.Files);
        Assert.AreEqual(2, totals.Folders, "The subject folder is not one of its own contents.");
        Assert.IsTrue(totals.IsComplete);
        Assert.IsFalse(totals.HasUnreadableFolders);
    }

    [TestMethod]
    public void ASelectionCountsItsOwnFoldersSoTheItemCountAddsUp()
    {
        var file = Path.Combine(_root, "a.bin");
        var folder = Path.Combine(_root, "sub");

        var totals = _scanner.Scan([file, folder], countRootFoldersThemselves: true, Ignore, CancellationToken.None);

        // Two selected items: one file plus one folder — and the folder's contents are still totalled.
        Assert.AreEqual(3, totals.Files);
        Assert.AreEqual(2, totals.Folders);
        Assert.AreEqual(600, totals.Bytes);
    }

    [TestMethod]
    public void AMissingRootIsReportedRatherThanThrowing()
    {
        var totals = _scanner.Scan(
            [Path.Combine(_root, "not-here")],
            countRootFoldersThemselves: true,
            Ignore,
            CancellationToken.None);

        Assert.AreEqual(0, totals.Files);
        Assert.AreEqual(0, totals.Folders);
        Assert.IsTrue(totals.IsComplete);
    }

    [TestMethod]
    public void CancellationStopsTheWalk()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => _scanner.Scan([_root], countRootFoldersThemselves: false, Ignore, cancellation.Token));
    }

    [TestMethod]
    public void AJunctionIsCountedAsALinkAndNeverFollowed()
    {
        var target = Path.Combine(_root, "sub");
        var junction = Path.Combine(_root, "loop");
        if (!TryCreateJunction(junction, target))
        {
            Assert.Inconclusive("This environment would not create a directory junction.");
        }

        var totals = _scanner.Scan([_root], countRootFoldersThemselves: false, Ignore, CancellationToken.None);

        // Following the junction would count sub's two files a second time and add its folder again.
        Assert.AreEqual(600, totals.Bytes);
        Assert.AreEqual(4, totals.Files, "The junction itself counts once, as a link.");
        Assert.AreEqual(2, totals.Folders);
    }

    [TestMethod]
    public void ProgressIsReportedForALargeTree()
    {
        var noisy = Directory.CreateDirectory(Path.Combine(_root, "many")).FullName;
        for (var index = 0; index < 4000; index++)
        {
            File.WriteAllBytes(Path.Combine(noisy, $"{index}.bin"), new byte[1]);
        }

        var reports = 0;
        var totals = _scanner.Scan(
            [_root],
            countRootFoldersThemselves: false,
            _ => reports++,
            CancellationToken.None);

        Assert.IsTrue(totals.IsComplete);
        Assert.AreEqual(4003, totals.Files);

        // Progress is throttled to a timer, so a few thousand files must not mean a few thousand
        // updates pushed at the dispatcher.
        Assert.IsTrue(reports < 100, $"Expected throttled progress, got {reports} reports.");
    }

    private static void Ignore(AggregateTotals totals)
    {
    }

    private static bool TryCreateJunction(string junction, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", junction, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(junction);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
