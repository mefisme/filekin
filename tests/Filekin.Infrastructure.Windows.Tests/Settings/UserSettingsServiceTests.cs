using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

/// <summary>
/// The settings document has more than one writer — the Location catalog and the Settings surface —
/// so these cover the single-owner guarantee that keeps one from erasing the other's half.
/// </summary>
[TestClass]
public sealed class UserSettingsServiceTests
{
    private string _directory = null!;
    private string _settingsPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-settings-service-{Guid.NewGuid():N}");
        _settingsPath = Path.Combine(_directory, "settings.json");
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
    public async Task InitializeCreatesTheFileOnFirstRun()
    {
        var settings = CreateService();

        var result = await settings.InitializeAsync();

        Assert.IsFalse(result.FileExists);
        Assert.IsTrue(File.Exists(_settingsPath));
    }

    [TestMethod]
    public async Task AnUpdatePublishesAndPersistsTheNewSnapshot()
    {
        var settings = CreateService();
        await settings.InitializeAsync();
        var changed = 0;
        settings.Changed += (_, _) => changed++;

        var result = await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(ThemePreference.Light, settings.Current.Theme);
        Assert.AreEqual(ThemePreference.Light, (await CreateService().InitializeAsync()).Settings.Theme);
    }

    [TestMethod]
    public async Task ALocationEditDoesNotDiscardAPreference()
    {
        // The regression this class exists for: the catalog used to rebuild the whole document from
        // its own fields, which silently dropped everything the Settings surface owned.
        var settings = CreateService();
        var catalog = new SettingsBackedLocationCatalog(settings);
        await catalog.InitializeAsync();
        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light, Accent = "pink" });

        var added = await catalog.AddAsync("projects", _directory);

        Assert.IsTrue(added.Succeeded);
        var reloaded = (await CreateService().InitializeAsync()).Settings;
        Assert.AreEqual(ThemePreference.Light, reloaded.Theme);
        Assert.AreEqual("pink", reloaded.Accent);
        Assert.HasCount(1, reloaded.Locations);
    }

    [TestMethod]
    public async Task RenamingALocationFollowsThroughToTheStartupTarget()
    {
        var settings = CreateService();
        var catalog = new SettingsBackedLocationCatalog(settings);
        await catalog.InitializeAsync();
        await catalog.AddAsync("projects", _directory);
        await settings.UpdateAsync(current => current with
        {
            OpenFilesAtLaunch = new StartupLocation { Target = StartupTarget.Location, Name = "projects" },
        });

        var renamed = await catalog.RenameAsync("projects", "work");

        Assert.IsTrue(renamed.Succeeded);
        var reloaded = (await CreateService().InitializeAsync()).Settings;
        Assert.AreEqual("work", reloaded.OpenFilesAtLaunch.Name);
    }

    [TestMethod]
    public async Task RenamingAnUnrelatedLocationLeavesTheStartupTargetAlone()
    {
        var settings = CreateService();
        var catalog = new SettingsBackedLocationCatalog(settings);
        await catalog.InitializeAsync();
        await catalog.AddAsync("projects", _directory);
        await catalog.AddAsync("photos", _directory);
        await settings.UpdateAsync(current => current with
        {
            OpenFilesAtLaunch = new StartupLocation { Target = StartupTarget.Location, Name = "projects" },
        });

        await catalog.RenameAsync("photos", "pictures");

        Assert.AreEqual("projects", settings.Current.OpenFilesAtLaunch.Name);
    }

    [TestMethod]
    public async Task RemovingTheStartupLocationLeavesThePreferenceForRepair()
    {
        // Removing a Location must not quietly rewrite the startup setting to Home: the user should
        // find the broken target in Settings and choose a replacement.
        var settings = CreateService();
        var catalog = new SettingsBackedLocationCatalog(settings);
        await catalog.InitializeAsync();
        await catalog.AddAsync("projects", _directory);
        await settings.UpdateAsync(current => current with
        {
            OpenFilesAtLaunch = new StartupLocation { Target = StartupTarget.Location, Name = "projects" },
        });

        await catalog.RemoveAsync("projects");

        Assert.AreEqual(StartupTarget.Location, settings.Current.OpenFilesAtLaunch.Target);
        Assert.AreEqual("projects", settings.Current.OpenFilesAtLaunch.Name);
    }

    private UserSettingsService CreateService() => new(new FilekinSettingsStore(_settingsPath));
}
