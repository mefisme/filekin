using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

/// <summary>
/// Covers the preference sections of <c>settings.json</c> — theme, accent, startup location,
/// interactive programs, and archive behavior — as they survive a load, a hand edit, and a save.
/// </summary>
[TestClass]
public sealed class FilekinSettingsPreferenceTests
{
    private static readonly string[] JustVim = ["vim"];
    private static readonly string[] VimThenHtop = ["vim", "htop"];

    private string _directory = null!;
    private string _settingsPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-prefs-{Guid.NewGuid():N}");
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
    public async Task DefaultsAreDarkBlueAndHome()
    {
        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(ThemePreference.Dark, result.Settings.Theme);
        Assert.AreEqual("blue", result.Settings.Accent);
        Assert.AreEqual(StartupTarget.Home, result.Settings.OpenFilesAtLaunch.Target);
        Assert.IsEmpty(result.Settings.InteractivePrograms);
        Assert.IsTrue(result.Settings.Archives.PreviewBeforeExtracting);
        Assert.AreEqual(CollisionPreference.Skip, result.Settings.Archives.WhenAFileExists);
    }

    [TestMethod]
    public async Task PreferencesSurviveASaveAndReload()
    {
        var store = new FilekinSettingsStore(_settingsPath);
        await store.SaveAsync(new FilekinSettings
        {
            Theme = ThemePreference.System,
            Accent = "teal",
            OpenFilesAtLaunch = new StartupLocation { Target = StartupTarget.Location, Name = "projects" },
            InteractivePrograms = ["vim"],
            Archives = new ArchiveSettings
            {
                PreviewBeforeExtracting = false,
                WhenAFileExists = CollisionPreference.Overwrite,
            },
        });

        var reloaded = (await store.LoadAsync()).Settings;

        Assert.AreEqual(ThemePreference.System, reloaded.Theme);
        Assert.AreEqual("teal", reloaded.Accent);
        Assert.AreEqual(StartupTarget.Location, reloaded.OpenFilesAtLaunch.Target);
        Assert.AreEqual("projects", reloaded.OpenFilesAtLaunch.Name);
        CollectionAssert.AreEqual(JustVim, reloaded.InteractivePrograms);
        Assert.IsFalse(reloaded.Archives.PreviewBeforeExtracting);
        Assert.AreEqual(CollisionPreference.Overwrite, reloaded.Archives.WhenAFileExists);
    }

    [TestMethod]
    public async Task ArchiveCollisionPreferenceIsCaseInsensitive()
    {
        await WriteAsync("""{ "archives": { "whenAFileExists": " OVERWRITE " } }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(CollisionPreference.Overwrite, result.Settings.Archives.WhenAFileExists);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public async Task AnUnknownArchiveCollisionPreferenceFallsBackToSkipWithAWarning()
    {
        await WriteAsync("""{ "archives": { "whenAFileExists": "rename" } }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(CollisionPreference.Skip, result.Settings.Archives.WhenAFileExists);
        Assert.HasCount(1, result.Warnings);
    }

    [TestMethod]
    public async Task AnUnknownArchiveFieldSurvivesASave()
    {
        await WriteAsync(
            """{ "archives": { "previewBeforeExtracting": false, "futureArchiveChoice": 7 } }""");
        var store = new FilekinSettingsStore(_settingsPath);
        var loaded = await store.LoadAsync();

        await store.SaveAsync(loaded.Settings);

        var json = await File.ReadAllTextAsync(_settingsPath);
        StringAssert.Contains(json, "futureArchiveChoice");
        Assert.IsFalse(loaded.Settings.Archives.PreviewBeforeExtracting);
    }

    [TestMethod]
    public async Task AnUnknownThemeFallsBackToDarkWithAWarning()
    {
        await WriteAsync("""{ "theme": "midnight" }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(ThemePreference.Dark, result.Settings.Theme);
        Assert.HasCount(1, result.Warnings);
    }

    [TestMethod]
    public async Task ThemeAndAccentAreCaseInsensitive()
    {
        await WriteAsync("""{ "theme": "LIGHT", "accent": "Pink" }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(ThemePreference.Light, result.Settings.Theme);
        Assert.AreEqual("pink", result.Settings.Accent);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public async Task AnAccentThisBuildDoesNotShipIsKeptRatherThanDiscarded()
    {
        // The app layer owns the accent list and falls back to blue for one it cannot draw. The file
        // must still round-trip the name, or opening Settings in an older build would erase it.
        await WriteAsync("""{ "accent": "sunset" }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual("sunset", result.Settings.Accent);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public async Task AnUnavailableStartupFolderIsPreservedNotErased()
    {
        // The whole point of the fallback is that an unplugged drive comes back later.
        await WriteAsync("""{ "openFilesAtLaunch": { "target": "folder", "path": "E:\\camera" } }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(StartupTarget.Folder, result.Settings.OpenFilesAtLaunch.Target);
        Assert.AreEqual(@"E:\camera", result.Settings.OpenFilesAtLaunch.Path);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public async Task ARelativeStartupFolderIsRejectedBackToHome()
    {
        await WriteAsync("""{ "openFilesAtLaunch": { "target": "folder", "path": "work" } }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(StartupTarget.Home, result.Settings.OpenFilesAtLaunch.Target);
        Assert.HasCount(1, result.Warnings);
    }

    [TestMethod]
    public async Task AnUnknownStartupTargetFallsBackToHomeWithAWarning()
    {
        await WriteAsync("""{ "openFilesAtLaunch": { "target": "lastUsed" } }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        Assert.AreEqual(StartupTarget.Home, result.Settings.OpenFilesAtLaunch.Target);
        Assert.HasCount(1, result.Warnings);
    }

    [TestMethod]
    public async Task InteractiveProgramsAreReducedToPlainNamesAndDeduplicated()
    {
        await WriteAsync("""{ "interactivePrograms": ["C:\\tools\\vim.exe", "VIM", " htop ", ""] }""");

        var result = await new FilekinSettingsStore(_settingsPath).LoadAsync();

        CollectionAssert.AreEqual(VimThenHtop, result.Settings.InteractivePrograms);
        Assert.HasCount(1, result.Warnings);
    }

    [TestMethod]
    public void AProgramNameIsNormalizedTheWayTheClassifierCompares()
    {
        Assert.IsTrue(FilekinSettingsStore.TryNormalizeProgramName(@"C:\tools\nvim.exe", out var name, out _));
        Assert.AreEqual("nvim", name);

        Assert.IsFalse(FilekinSettingsStore.TryNormalizeProgramName("two words", out _, out var error));
        Assert.IsNotEmpty(error);
    }

    [TestMethod]
    public async Task AnUnrecognisedTopLevelFieldSurvivesASave()
    {
        await WriteAsync("""{ "theme": "light", "futureSetting": 7 }""");
        var store = new FilekinSettingsStore(_settingsPath);
        var loaded = await store.LoadAsync();

        await store.SaveAsync(loaded.Settings);

        StringAssert.Contains(await File.ReadAllTextAsync(_settingsPath), "futureSetting");
    }

    private async Task WriteAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
