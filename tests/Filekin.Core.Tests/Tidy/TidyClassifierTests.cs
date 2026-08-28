using Filekin.Core.Tidy;

namespace Filekin.Core.Tests.Tidy;

[TestClass]
public sealed class TidyClassifierTests
{
    [TestMethod]
    [DataRow("report.pdf", TidyCategory.Documents)]
    [DataRow("notes.MD", TidyCategory.Documents)]
    [DataRow("budget.xlsx", TidyCategory.Documents)]
    [DataRow("holiday.jpg", TidyCategory.Photos)]
    [DataRow("logo.svg", TidyCategory.Photos)]
    [DataRow("shot.CR2", TidyCategory.Photos)]
    [DataRow("song.flac", TidyCategory.Audio)]
    [DataRow("podcast.m4a", TidyCategory.Audio)]
    [DataRow("clip.mkv", TidyCategory.Videos)]
    [DataRow("movie.MP4", TidyCategory.Videos)]
    [DataRow("backup.7z", TidyCategory.Archives)]
    [DataRow("source.tar.gz", TidyCategory.Archives)]
    [DataRow("archive.rar", TidyCategory.Archives)]
    [DataRow("setup.exe", TidyCategory.Installers)]
    [DataRow("driver.msi", TidyCategory.Installers)]
    public void KnownExtensionsGoToTheirCategory(string name, TidyCategory expected) =>
        Assert.AreEqual(expected, TidyClassifier.Classify(name));

    [TestMethod]
    [DataRow("ubuntu.iso")]
    [DataRow("disk.img")]
    [DataRow("machine.vhdx")]
    public void DiscImagesAreArchivesBecauseTheyAreContainers(string name) =>
        Assert.AreEqual(TidyCategory.Archives, TidyClassifier.Classify(name));

    [TestMethod]
    [DataRow("poster.psd", TidyCategory.Photos)]
    [DataRow("cover.ai", TidyCategory.Photos)]
    [DataRow("edit.prproj", TidyCategory.Videos)]
    [DataRow("mix.flp", TidyCategory.Audio)]
    public void ProjectFilesFollowTheirMedium(string name, TidyCategory expected) =>
        Assert.AreEqual(expected, TidyClassifier.Classify(name));

    [TestMethod]
    [DataRow("scene.blend")]
    [DataRow("app.sln")]
    [DataRow("data.qqq")]
    public void ProjectFilesWithNoObviousMediumFallToOther(string name) =>
        Assert.AreEqual(TidyCategory.Other, TidyClassifier.Classify(name));

    [TestMethod]
    [DataRow("movie.mp4.crdownload")]
    [DataRow("big.zip.part")]
    [DataRow("scratch.tmp")]
    public void AnUnfinishedDownloadIsNeverClassified(string name)
    {
        Assert.IsNull(TidyClassifier.Classify(name), name);
        Assert.IsTrue(TidyClassifier.IsInProgressDownload(name), name);
    }

    [TestMethod]
    [DataRow("LICENSE")]
    [DataRow("Makefile")]
    [DataRow("noextension.")]
    public void AFileWithNoExtensionIsLeftAlone(string name) =>
        Assert.IsNull(TidyClassifier.Classify(name));
}
