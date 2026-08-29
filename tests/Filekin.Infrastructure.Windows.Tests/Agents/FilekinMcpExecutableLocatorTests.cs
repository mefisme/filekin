using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class FilekinMcpExecutableLocatorTests
{
    private string _directory = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-mcp-locator-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveReturnsTheFixedPackagedCompanionPath()
    {
        var expectedPath = Path.Combine(
            _directory,
            FilekinMcpExecutableLocator.ExecutableFileName);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(expectedPath, string.Empty);

        var resolved = FilekinMcpExecutableLocator.Resolve(_directory);

        Assert.AreEqual(expectedPath, resolved);
    }

    [TestMethod]
    public void ResolveReportsTheExactMissingCompanion()
    {
        var exception = Assert.Throws<FileNotFoundException>(
            () => FilekinMcpExecutableLocator.Resolve(_directory));

        var expectedPath = Path.Combine(
            _directory,
            FilekinMcpExecutableLocator.ExecutableFileName);
        Assert.AreEqual(expectedPath, exception.FileName);
    }

    [TestMethod]
    public void ResolveRejectsARelativeApplicationDirectory()
    {
        Assert.Throws<ArgumentException>(
            () => FilekinMcpExecutableLocator.Resolve("relative-app"));
    }
}
