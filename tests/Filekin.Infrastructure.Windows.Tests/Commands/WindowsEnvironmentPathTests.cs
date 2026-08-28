using Filekin.Infrastructure.Windows.Commands;

namespace Filekin.Infrastructure.Windows.Tests.Commands;

[TestClass]
public sealed class WindowsEnvironmentPathTests
{
    [TestMethod]
    public void MergePreservesProcessEntriesAndAddsCurrentConfiguredEntries()
    {
        var result = WindowsEnvironmentPath.Merge(
            @"C:\Filekin\Private;C:\Windows\System32",
            @"C:\Windows\System32;C:\MachineTools",
            @"C:\UserTools;C:\Filekin\Private");

        Assert.AreEqual(
            @"C:\Filekin\Private;C:\Windows\System32;C:\MachineTools;C:\UserTools",
            result);
    }

    [TestMethod]
    public void MergeExpandsEnvironmentVariablesAndIgnoresEmptyEntries()
    {
        var result = WindowsEnvironmentPath.Merge(
            @";%SystemRoot%\System32;;",
            string.Empty,
            null);

        Assert.AreEqual(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32"), result);
    }

    [TestMethod]
    public void ContainsDirectoryIgnoresCaseQuotesAndTrailingSeparators()
    {
        Assert.IsTrue(WindowsEnvironmentPath.ContainsDirectory(
            "\"C:\\Tools\\\";C:\\Other",
            @"c:\tools"));
    }

    [TestMethod]
    public void WithoutRemovesPreviouslyConfiguredEntriesAndKeepsProcessOnlyFolders()
    {
        var result = WindowsEnvironmentPath.Without(
            @"C:\Filekin\Private;C:\Machine;C:\User",
            @"C:\Machine;C:\User");

        Assert.AreEqual(@"C:\Filekin\Private", result);
    }
}
