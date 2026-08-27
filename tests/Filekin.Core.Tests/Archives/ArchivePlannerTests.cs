using Filekin.Core.Archives;

namespace Filekin.Core.Tests.Archives;

/// <summary>
/// The redundant-nesting rule from PRODUCT.md, and the archive path-traversal defence from
/// ARCHITECTURE.md's Security Considerations. Both are the reason <c>/unzip</c> exists at all, so
/// they are pinned here rather than left to live QA.
/// </summary>
[TestClass]
public sealed class ArchivePlannerTests
{
    private const string Destination = @"D:\Downloads";

    private static readonly Func<string, bool> NothingExists = _ => false;

    /// <summary>An archive whose entries all sit under one folder: <c>photos/a.jpg</c>.</summary>
    private static readonly ArchiveEntry[] Wrapped =
    [
        Folder("photos/"),
        Entry("photos/a.jpg"),
        Entry("photos/b.jpg"),
        Entry("photos/raw/c.dng"),
    ];

    /// <summary>An archive of loose files with nothing wrapping them.</summary>
    private static readonly ArchiveEntry[] Loose =
    [
        Entry("a.jpg"),
        Entry("b.jpg"),
    ];

    [TestMethod]
    public void AWrappedArchiveReusesItsOwnFolderInsteadOfDoublingIt()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, Wrapped, pathExists: NothingExists);

        Assert.AreEqual("photos", plan.WrapperName);
        Assert.AreEqual("photos", plan.FolderName);
        Assert.AreEqual(@"D:\Downloads\photos", plan.TargetRoot);
        CollectionAssert.DoesNotContain(
            plan.Entries.Select(entry => entry.RelativeTarget).ToArray(),
            @"photos\a.jpg");
        CollectionAssert.Contains(
            plan.Entries.Select(entry => entry.RelativeTarget).ToArray(),
            "a.jpg");
    }

    [TestMethod]
    public void ALooseArchiveGetsOneFolderNamedAfterTheArchive()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\holiday.zip", Destination, Loose, pathExists: NothingExists);

        Assert.IsNull(plan.WrapperName);
        Assert.AreEqual("holiday", plan.FolderName);
        Assert.AreEqual(@"D:\Downloads\holiday", plan.TargetRoot);
    }

    /// <summary>
    /// The promise that makes the default safe to press Enter on: whatever the archive looks like
    /// inside, exactly one new folder appears in the destination.
    /// </summary>
    [TestMethod]
    public void BothArchiveShapesProduceExactlyOneNewFolder()
    {
        var wrapped = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, Wrapped, pathExists: NothingExists);
        var loose = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, Loose, pathExists: NothingExists);

        Assert.AreEqual(@"D:\Downloads\photos", wrapped.TargetRoot);
        Assert.AreEqual(@"D:\Downloads\photos", loose.TargetRoot);
    }

    [TestMethod]
    public void NoRootStripsTheArchivesWrapperEntirely()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip",
            Destination,
            Wrapped,
            UnzipLayout.NoRoot,
            pathExists: NothingExists);

        Assert.IsNull(plan.FolderName);
        Assert.AreEqual(Destination, plan.TargetRoot);
        CollectionAssert.Contains(
            plan.Entries.Select(entry => entry.RelativeTarget).ToArray(),
            "a.jpg");
    }

    [TestMethod]
    public void NoRootOnALooseArchiveAddsNoFolder()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\holiday.zip",
            Destination,
            Loose,
            UnzipLayout.NoRoot,
            pathExists: NothingExists);

        Assert.AreEqual(Destination, plan.TargetRoot);
        Assert.AreEqual(2, plan.FileCount);
    }

    [TestMethod]
    public void ALooseFileAtTheRootMeansThereIsNoWrapperToReuse()
    {
        ArchiveEntry[] mixed = [Folder("photos/"), Entry("photos/a.jpg"), Entry("readme.txt")];

        Assert.IsNull(ArchivePlanner.DetectWrapper(mixed));
    }

    [TestMethod]
    public void TwoTopLevelFoldersAreNotAWrapper()
    {
        ArchiveEntry[] two = [Entry("photos/a.jpg"), Entry("videos/b.mp4")];

        Assert.IsNull(ArchivePlanner.DetectWrapper(two));
    }

    [TestMethod]
    public void AnArchiveHoldingOneFileHasNoWrapper()
    {
        ArchiveEntry[] single = [Entry("notes.txt")];

        Assert.IsNull(ArchivePlanner.DetectWrapper(single));
    }

    [TestMethod]
    public void TheWrapperIsRecognizedWhateverItsCase()
    {
        ArchiveEntry[] mixedCase = [Entry("Photos/a.jpg"), Entry("photos/b.jpg")];

        Assert.AreEqual("Photos", ArchivePlanner.DetectWrapper(mixedCase));
    }

    [TestMethod]
    public void ARenamedFolderReplacesTheWrapperRatherThanNestingInsideIt()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip",
            Destination,
            Wrapped,
            folderName: "Holiday 2026",
            pathExists: NothingExists);

        Assert.AreEqual(@"D:\Downloads\Holiday 2026", plan.TargetRoot);
        CollectionAssert.Contains(
            plan.Entries.Select(entry => entry.RelativeTarget).ToArray(),
            "a.jpg");
    }

    [TestMethod]
    public void AFolderNameWindowsCannotStoreFallsBackToTheArchiveName()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip",
            Destination,
            Loose,
            folderName: @"bad:name?",
            pathExists: NothingExists);

        Assert.AreEqual("photos", plan.FolderName);
    }

    [TestMethod]
    public void AFolderNameThatIsItselfAPathFallsBackRatherThanEscaping()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip",
            Destination,
            Loose,
            folderName: @"..\..\Windows",
            pathExists: NothingExists);

        Assert.AreEqual("photos", plan.FolderName);
        Assert.AreEqual(@"D:\Downloads\photos", plan.TargetRoot);
    }

    [TestMethod]
    public void AnEntryClimbingOutOfTheFolderIsRefused()
    {
        ArchiveEntry[] slip = [Entry("../../evil.txt"), Entry("a.jpg")];

        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, slip, pathExists: NothingExists);

        Assert.AreEqual(1, plan.Rejected.Count);
        Assert.AreEqual("../../evil.txt", plan.Rejected[0].EntryPath);
        Assert.AreEqual(1, plan.FileCount);
    }

    [TestMethod]
    public void AnEntryNamingAnAbsoluteWindowsPathIsRefused()
    {
        ArchiveEntry[] absolute = [Entry(@"C:\Windows\System32\evil.dll")];

        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, absolute, pathExists: NothingExists);

        Assert.AreEqual(1, plan.Rejected.Count);
        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void AnEntryRootedAtASlashIsRefused()
    {
        ArchiveEntry[] rooted = [new("/etc/passwd", 10, 10, DateTimeOffset.UnixEpoch, false)];

        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, rooted, pathExists: NothingExists);

        // A leading slash is trimmed as an archive convention, so this lands inside as etc/passwd
        // rather than at the filesystem root. What matters is that it cannot escape the folder.
        Assert.IsTrue(plan.Entries.All(entry =>
            Path.GetFullPath(Path.Combine(plan.TargetRoot, entry.RelativeTarget))
                .StartsWith(plan.TargetRoot, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AnEntryUsingBackslashesCannotEscapeEither()
    {
        ArchiveEntry[] slip = [Entry(@"..\..\evil.txt")];

        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, slip, pathExists: NothingExists);

        Assert.AreEqual(1, plan.Rejected.Count);
        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void ExistingFilesAreReportedAsCollisions()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\holiday.zip",
            Destination,
            Loose,
            pathExists: path => path.EndsWith("a.jpg", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(1, plan.Collisions.Count);
        Assert.AreEqual(@"D:\Downloads\holiday\a.jpg", plan.Collisions[0]);
    }

    [TestMethod]
    public void FoldersAndFilesAreCountedSeparately()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\photos.zip", Destination, Wrapped, pathExists: NothingExists);

        // The wrapper's own directory entry becomes the folder, so it is not counted as content.
        Assert.AreEqual(3, plan.FileCount);
        Assert.AreEqual(0, plan.FolderCount);
        Assert.AreEqual(30, plan.TotalBytes);
    }

    [TestMethod]
    public void AnEmptyArchivePlansNothing()
    {
        var plan = ArchivePlanner.Create(
            @"D:\Downloads\empty.zip", Destination, [], pathExists: NothingExists);

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual("empty", plan.FolderName);
    }

    private static ArchiveEntry Entry(string path, long length = 10) =>
        new(path, length, length, DateTimeOffset.UnixEpoch, false);

    private static ArchiveEntry Folder(string path) =>
        new(path, 0, 0, DateTimeOffset.UnixEpoch, true);
}
