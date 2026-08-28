using Filekin.Core.Commands.App;
using Filekin.Core.Shell;
using Filekin.Infrastructure.Windows.FileSystem;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

/// <summary>
/// End-to-end cover for the saved-Location rebase: real <c>/move</c> and <c>/rename</c> dispatch over
/// a real temporary filesystem, wired to a real settings-backed catalog. The WPF command bar adds
/// only presentation on top of this path, so this is the durable substitute for live UI verification
/// of the rebase contract.
/// </summary>
[TestClass]
public sealed class LocationRebaseIntegrationTests
{
    private string _root = null!;
    private string _settingsPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-location-rebase-{Guid.NewGuid():N}");
        _settingsPath = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task MoveCarriesExactAndNestedSavedLocationsToTheNewPath()
    {
        var projects = Path.Combine(_root, "Projects");
        var nested = Path.Combine(projects, "Client", "Current");
        var archive = Path.Combine(_root, "Archive");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(archive);

        var catalog = await CreateCatalogAsync();
        await catalog.AddAsync("Projects", projects);
        await catalog.AddAsync("Current", nested);

        var result = await DispatchAsync(catalog, "/move Projects Archive");

        Assert.IsTrue(result.Succeeded, result.Message);
        var moved = Path.Combine(archive, "Projects");
        Assert.IsTrue(Directory.Exists(moved));
        Assert.IsFalse(Directory.Exists(projects));

        var reloaded = await CreateCatalogAsync();
        Assert.IsTrue(reloaded.TryResolve("projects", out var reloadedProjects));
        Assert.AreEqual(moved, reloadedProjects);
        Assert.IsTrue(reloaded.TryResolve("current", out var reloadedNested));
        Assert.AreEqual(Path.Combine(moved, "Client", "Current"), reloadedNested);
    }

    [TestMethod]
    public async Task RenameCarriesTheSavedLocationToTheNewName()
    {
        var original = Path.Combine(_root, "Notes");
        Directory.CreateDirectory(original);

        var catalog = await CreateCatalogAsync();
        await catalog.AddAsync("Notes", original);

        var result = await DispatchAsync(catalog, "/rename Notes Journal");

        Assert.IsTrue(result.Succeeded, result.Message);
        var renamed = Path.Combine(_root, "Journal");
        Assert.IsTrue(Directory.Exists(renamed));

        var reloaded = await CreateCatalogAsync();
        Assert.IsTrue(reloaded.TryResolve("notes", out var reloadedPath));
        Assert.AreEqual(renamed, reloadedPath);
    }

    [TestMethod]
    public async Task CopyLeavesSavedLocationsOnTheOriginalPath()
    {
        var source = Path.Combine(_root, "Source");
        var destination = Path.Combine(_root, "Backup");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);

        var catalog = await CreateCatalogAsync();
        await catalog.AddAsync("Source", source);

        var result = await DispatchAsync(catalog, "/copy Source Backup");

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsEmpty(result.Relocations);

        var reloaded = await CreateCatalogAsync();
        Assert.IsTrue(reloaded.TryResolve("source", out var reloadedPath));
        Assert.AreEqual(source, reloadedPath);
    }

    [TestMethod]
    public async Task PartialMoveStillRebasesTheLocationForTheItemThatMoved()
    {
        var projects = Path.Combine(_root, "Projects");
        var blocked = Path.Combine(_root, "Blocked");
        var archive = Path.Combine(_root, "Archive");
        Directory.CreateDirectory(projects);
        Directory.CreateDirectory(blocked);
        Directory.CreateDirectory(archive);
        Directory.CreateDirectory(Path.Combine(archive, "Blocked"));

        var catalog = await CreateCatalogAsync();
        await catalog.AddAsync("Projects", projects);

        var result = await DispatchAsync(catalog, "/move Projects Blocked Archive");

        Assert.AreEqual(AppCommandOutcome.PartialSuccess, result.Outcome);
        Assert.HasCount(1, result.Relocations);
        Assert.HasCount(1, result.Failures);
        var moved = Path.Combine(archive, "Projects");
        Assert.IsTrue(Directory.Exists(moved));
        Assert.IsTrue(Directory.Exists(blocked));

        var reloaded = await CreateCatalogAsync();
        Assert.IsTrue(reloaded.TryResolve("projects", out var reloadedProjects));
        Assert.AreEqual(moved, reloadedProjects);
    }

    private async Task<AppCommandResult> DispatchAsync(SettingsBackedLocationCatalog catalog, string input)
    {
        var operations = new WindowsFileSystemOperations();
        var dispatcher = BuiltInAppCommands.CreateDispatcher(operations);
        var result = await dispatcher.DispatchAsync(input, new ShellLocation(_root, "FileSystem", _root));
        if (result.Relocations.Count == 0)
        {
            return result;
        }

        var coordinator = new LocationRebaseCoordinator(operations, catalog);
        var rebase = await coordinator.RebaseOrRollbackAsync(result.Relocations);
        Assert.IsTrue(rebase.Succeeded, rebase.Message);
        return result;
    }

    private async Task<SettingsBackedLocationCatalog> CreateCatalogAsync()
    {
        var catalog = new SettingsBackedLocationCatalog(new FilekinSettingsStore(_settingsPath));
        await catalog.InitializeAsync();
        return catalog;
    }
}
