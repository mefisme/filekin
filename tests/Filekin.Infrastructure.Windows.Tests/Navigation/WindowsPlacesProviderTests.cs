using Filekin.Core.Commands.References;
using Filekin.Core.Navigation;
using Filekin.Infrastructure.Windows.Navigation;

namespace Filekin.Infrastructure.Windows.Tests.Navigation;

[TestClass]
public sealed class WindowsPlacesProviderTests
{
    private static readonly string[] CommonNames =
        ["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos"];

    private static readonly string[] DesktopThenCloudNames = ["Desktop", "Dropbox", "OneDrive - Personal"];

    private static readonly string[] DesktopAndDropbox = ["Desktop", "Dropbox"];

    [TestMethod]
    public void CommonPlacesAreShortOrderedListWithoutHome()
    {
        var known = new FakeKnownFolders(
            ("desktop", @"C:\Users\Test\Desktop"),
            ("documents", @"C:\Users\Test\Documents"),
            ("downloads", @"C:\Users\Test\Downloads"),
            ("pictures", @"C:\Users\Test\Pictures"),
            ("music", @"C:\Users\Test\Music"),
            ("videos", @"C:\Users\Test\Videos"),
            ("home", @"C:\Users\Test"));
        var provider = new WindowsPlacesProvider(known, new FakeCloudRoots(), _ => true);

        var places = provider.GetPlaces();

        CollectionAssert.AreEqual(CommonNames, places.Select(place => place.Name).ToArray());
        Assert.IsTrue(places.All(place => place.Kind == PlaceKind.Common));
    }

    [TestMethod]
    public void CloudRootsFollowCommonPlacesAndSortByDisplayName()
    {
        var provider = new WindowsPlacesProvider(
            new FakeKnownFolders(("desktop", @"C:\Users\Test\Desktop")),
            new FakeCloudRoots(
                new("OneDrive - Personal", @"C:\Users\Test\OneDrive"),
                new("Dropbox", @"C:\Users\Test\Dropbox")),
            _ => true);

        var places = provider.GetPlaces();

        CollectionAssert.AreEqual(DesktopThenCloudNames, places.Select(place => place.Name).ToArray());
        Assert.AreEqual(PlaceKind.Cloud, places[1].Kind);
        Assert.AreEqual(PlaceKind.Cloud, places[2].Kind);
    }

    [TestMethod]
    public void MissingAndDuplicatePathsAreIgnoredIndependently()
    {
        var desktop = @"C:\Users\Test\Desktop";
        var provider = new WindowsPlacesProvider(
            new FakeKnownFolders(("desktop", desktop), ("documents", @"C:\Missing")),
            new FakeCloudRoots(
                new("Duplicate", desktop),
                new("Missing cloud", @"C:\AlsoMissing"),
                new("Dropbox", @"C:\Users\Test\Dropbox")),
            path => !path.Contains("Missing", StringComparison.OrdinalIgnoreCase));

        var places = provider.GetPlaces();

        CollectionAssert.AreEqual(DesktopAndDropbox, places.Select(place => place.Name).ToArray());
    }

    [TestMethod]
    public void RegisteredCloudRootReaderReturnsOnlyUsableShapes()
    {
        var roots = new WindowsRegisteredCloudRootSource().GetCurrentUserRoots();

        Assert.IsTrue(roots.All(root =>
            !string.IsNullOrWhiteSpace(root.DisplayName) && Path.IsPathFullyQualified(root.Path)));
    }

    private sealed class FakeKnownFolders(params (string Name, string Path)[] entries) : INamedLocationResolver
    {
        private readonly Dictionary<string, string> _entries = entries.ToDictionary(
            entry => entry.Name,
            entry => entry.Path,
            StringComparer.OrdinalIgnoreCase);

        public bool TryResolve(string name, out string path) => _entries.TryGetValue(name, out path!);
    }

    private sealed class FakeCloudRoots(params RegisteredCloudRoot[] roots) : IRegisteredCloudRootSource
    {
        public IReadOnlyList<RegisteredCloudRoot> GetCurrentUserRoots() => roots;
    }
}
