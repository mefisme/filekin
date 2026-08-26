using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.FileSystem;

[TestClass]
public sealed class FileTypeCodeTests
{
    [TestMethod]
    public void DirectoryIsAlwaysDir()
    {
        Assert.AreEqual("DIR", FileTypeCode.For("anything.txt", isDirectory: true));
    }

    [TestMethod]
    public void KnownExtensionsMapToFamilyCodes()
    {
        Assert.AreEqual("SLN", FileTypeCode.For("Filekin.sln", isDirectory: false));
        Assert.AreEqual("MD", FileTypeCode.For("README.md", isDirectory: false));
        Assert.AreEqual("PROJ", FileTypeCode.For("Filekin.App.csproj", isDirectory: false));
        Assert.AreEqual("XML", FileTypeCode.For("Directory.Build.props", isDirectory: false));
        Assert.AreEqual("IMG", FileTypeCode.For("cover.PNG", isDirectory: false));
    }

    [TestMethod]
    public void DotfilesAreConfiguration()
    {
        Assert.AreEqual("CFG", FileTypeCode.For(".gitignore", isDirectory: false));
        Assert.AreEqual("CFG", FileTypeCode.For(".editorconfig", isDirectory: false));
    }

    [TestMethod]
    public void ExtensionlessFileIsFile()
    {
        Assert.AreEqual("FILE", FileTypeCode.For("LICENSE", isDirectory: false));
    }

    [TestMethod]
    public void UnknownExtensionFallsBackToItsUppercasedForm()
    {
        Assert.AreEqual("FOO", FileTypeCode.For("data.foo", isDirectory: false));
    }

    [TestMethod]
    public void MultiDotNameUsesTheFinalExtension()
    {
        Assert.AreEqual("ZIP", FileTypeCode.For("archive.tar.gz", isDirectory: false));
    }

    [TestMethod]
    public void ForEntryDerivesFromTheEntry()
    {
        var entry = new DirectoryEntry("notes.md", @"D:\notes.md", IsDirectory: false, SizeBytes: 12, LastModified: DateTime.UnixEpoch);
        Assert.AreEqual("MD", FileTypeCode.ForEntry(entry));
    }
}
