using Filekin.Infrastructure.Windows.References;

namespace Filekin.Infrastructure.Windows.Tests.References;

[TestClass]
public sealed class WindowsKnownFolderLocationsTests
{
    private readonly WindowsKnownFolderLocations _locations = new();

    [TestMethod]
    [DataRow("desktop")]
    [DataRow("documents")]
    [DataRow("pictures")]
    [DataRow("music")]
    [DataRow("videos")]
    [DataRow("home")]
    public void SpecialFolderReferencesResolveToRootedPaths(string name)
    {
        Assert.IsTrue(_locations.TryResolve(name, out var path), $"'{name}' should resolve.");
        Assert.IsTrue(Path.IsPathRooted(path), $"'{name}' should resolve to a rooted path but was '{path}'.");
    }

    [TestMethod]
    public void NameMatchingIsCaseInsensitive()
    {
        Assert.IsTrue(_locations.TryResolve("Desktop", out var upper));
        Assert.IsTrue(_locations.TryResolve("desktop", out var lower));
        Assert.AreEqual(lower, upper);
    }

    [TestMethod]
    public void DownloadsResolvesThroughTheKnownFolderApi()
    {
        Assert.IsTrue(_locations.TryResolve("downloads", out var path), "'downloads' should resolve.");
        Assert.IsTrue(Path.IsPathRooted(path), $"Downloads should be a rooted path but was '{path}'.");
        StringAssert.Contains(path, "Downloads", "The resolved Downloads folder path should name the Downloads folder.");
    }

    [TestMethod]
    public void UnknownNamesDoNotResolve()
    {
        Assert.IsFalse(_locations.TryResolve("nowhere", out var path));
        Assert.AreEqual(string.Empty, path);
    }
}
