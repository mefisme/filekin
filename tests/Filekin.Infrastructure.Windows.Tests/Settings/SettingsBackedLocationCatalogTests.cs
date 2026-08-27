using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

[TestClass]
public sealed class SettingsBackedLocationCatalogTests
{
    private string _directory = null!;
    private string _settingsPath = null!;
    private string _firstPath = null!;
    private string _secondPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-location-catalog-{Guid.NewGuid():N}");
        _settingsPath = Path.Combine(_directory, "settings.json");
        _firstPath = Path.Combine(_directory, "First");
        _secondPath = Path.Combine(_directory, "Second");
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
    public async Task InitializeCreatesAnEmptySettingsFile()
    {
        var catalog = CreateCatalog();

        var result = await catalog.InitializeAsync();

        Assert.IsFalse(result.FileExists);
        Assert.IsTrue(File.Exists(_settingsPath));
        Assert.IsEmpty(catalog.Locations);
    }

    [TestMethod]
    public async Task AddPersistsAndPublishesTheNewReference()
    {
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();

        var result = await catalog.AddAsync("Projects", _firstPath);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(catalog.TryResolve("projects", out var path));
        Assert.AreEqual(_firstPath, path);

        var reloaded = CreateCatalog();
        await reloaded.InitializeAsync();
        Assert.IsTrue(reloaded.TryResolve("PROJECTS", out var reloadedPath));
        Assert.AreEqual(_firstPath, reloadedPath);
    }

    [TestMethod]
    public async Task SetRequiresAnExistingLocationAndChangesOnlyItsPath()
    {
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();
        await catalog.AddAsync("Projects", _firstPath);

        var missing = await catalog.SetPathAsync("Archive", _secondPath);
        var updated = await catalog.SetPathAsync("projects", _secondPath);

        Assert.IsFalse(missing.Succeeded);
        Assert.IsTrue(updated.Succeeded);
        Assert.IsFalse(catalog.TryResolve("archive", out _));
        Assert.IsTrue(catalog.TryResolve("projects", out var path));
        Assert.AreEqual(_secondPath, path);
    }

    [TestMethod]
    public async Task UpdateAtomicallyChangesTheNameAndPath()
    {
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();
        await catalog.AddAsync("Projects", _firstPath);

        var result = await catalog.UpdateAsync("projects", "Client-Work", _secondPath);

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(catalog.TryResolve("projects", out _));
        Assert.IsTrue(catalog.TryResolve("client-work", out var path));
        Assert.AreEqual(_secondPath, path);
    }

    [TestMethod]
    public async Task RenameRejectsDuplicatesWithoutChangingTheCatalog()
    {
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();
        await catalog.AddAsync("Projects", _firstPath);
        await catalog.AddAsync("Archive", _secondPath);

        var result = await catalog.RenameAsync("Projects", "archive");

        Assert.IsFalse(result.Succeeded);
        Assert.HasCount(2, catalog.Locations);
        Assert.AreEqual("Projects", catalog.Locations[0].Name);
        Assert.AreEqual("Archive", catalog.Locations[1].Name);
    }

    [TestMethod]
    public async Task RemoveDeletesOnlyTheSavedPointer()
    {
        Directory.CreateDirectory(_firstPath);
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();
        await catalog.AddAsync("Projects", _firstPath);

        var result = await catalog.RemoveAsync("projects");

        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.Message, "folder was not deleted");
        Assert.IsFalse(catalog.TryResolve("projects", out _));
        Assert.IsTrue(Directory.Exists(_firstPath));
    }

    [TestMethod]
    public async Task InvalidAndReservedNamesAreRejected()
    {
        var catalog = CreateCatalog();
        await catalog.InitializeAsync();

        var spaced = await catalog.AddAsync("my projects", _firstPath);
        var reserved = await catalog.AddAsync("selection", _firstPath);

        Assert.IsFalse(spaced.Succeeded);
        Assert.IsFalse(reserved.Succeeded);
        Assert.IsEmpty(catalog.Locations);
    }

    private SettingsBackedLocationCatalog CreateCatalog() =>
        new(new FilekinSettingsStore(_settingsPath));
}
