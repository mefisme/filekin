using Filekin.Core.Commands.References;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

[TestClass]
public sealed class StartupLocationResolverTests
{
    private static readonly string Home = StartupLocationResolver.HomePath;

    [TestMethod]
    public void NoPreferenceOpensHome()
    {
        var result = StartupLocationResolver.Resolve(new StartupLocation(), Catalog(), Missing);

        Assert.AreEqual(Home, result.Path);
        Assert.IsNull(result.Notice);
    }

    [TestMethod]
    public void ASavedLocationResolvesThroughTheCatalog()
    {
        var preference = new StartupLocation { Target = StartupTarget.Location, Name = "projects" };

        var result = StartupLocationResolver.Resolve(
            preference,
            Catalog(("projects", @"D:\work")),
            Present);

        Assert.AreEqual(@"D:\work", result.Path);
        Assert.IsNull(result.Notice);
    }

    [TestMethod]
    public void ARemovedLocationFallsBackToHomeAndSaysWhy()
    {
        var preference = new StartupLocation { Target = StartupTarget.Location, Name = "projects" };

        var result = StartupLocationResolver.Resolve(preference, Catalog(), Present);

        Assert.AreEqual(Home, result.Path);
        StringAssert.Contains(result.Notice, "@projects");
        StringAssert.Contains(result.Notice, "Home");
    }

    [TestMethod]
    public void AnUnavailableLocationPathFallsBackWithoutLosingThePreference()
    {
        // A network share that is offline this morning must not silently become Home forever; the
        // resolver only decides this launch and never rewrites settings.
        var preference = new StartupLocation { Target = StartupTarget.Location, Name = "share" };

        var result = StartupLocationResolver.Resolve(
            preference,
            Catalog(("share", @"\\nas\team")),
            Missing);

        Assert.AreEqual(Home, result.Path);
        StringAssert.Contains(result.Notice, "not available right now");
    }

    [TestMethod]
    public void AnExplicitFolderIsUsedWhenItExists()
    {
        var preference = new StartupLocation { Target = StartupTarget.Folder, Path = @"D:\photos" };

        var result = StartupLocationResolver.Resolve(preference, Catalog(), Present);

        Assert.AreEqual(@"D:\photos", result.Path);
        Assert.IsNull(result.Notice);
    }

    [TestMethod]
    public void AnUnavailableFolderFallsBackToHomeAndSaysWhy()
    {
        var preference = new StartupLocation { Target = StartupTarget.Folder, Path = @"E:\camera" };

        var result = StartupLocationResolver.Resolve(preference, Catalog(), Missing);

        Assert.AreEqual(Home, result.Path);
        StringAssert.Contains(result.Notice, @"E:\camera");
    }

    [TestMethod]
    public void AFolderTargetWithNoPathOpensHomeQuietly()
    {
        // Nothing was ever chosen, so there is nothing to warn about.
        var preference = new StartupLocation { Target = StartupTarget.Folder };

        var result = StartupLocationResolver.Resolve(preference, Catalog(), Missing);

        Assert.AreEqual(Home, result.Path);
        Assert.IsNull(result.Notice);
    }

    private static bool Present(string path) => true;

    private static bool Missing(string path) => false;

    private static StubCatalog Catalog(params (string Name, string Path)[] locations) => new(locations);

    private sealed class StubCatalog(params (string Name, string Path)[] locations) : INamedLocationResolver
    {
        public bool TryResolve(string name, out string path)
        {
            foreach (var location in locations)
            {
                if (string.Equals(location.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    path = location.Path;
                    return true;
                }
            }

            path = string.Empty;
            return false;
        }
    }
}
