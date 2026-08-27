using System.Text.Json;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.Infrastructure.Windows.Tests.Settings;

[TestClass]
public sealed class FilekinSettingsStoreTests
{
    private string _directory = null!;
    private string _settingsPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-settings-{Guid.NewGuid():N}");
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
    public async Task MissingFileLoadsEmptySettingsWithoutCreatingIt()
    {
        var store = new FilekinSettingsStore(_settingsPath);

        var result = await store.LoadAsync();

        Assert.IsFalse(result.FileExists);
        Assert.IsFalse(result.IsMalformed);
        Assert.IsEmpty(result.Settings.Locations);
        Assert.IsFalse(File.Exists(_settingsPath));
    }

    [TestMethod]
    public async Task SaveCreatesTheProductDirectoryAndReadableEmptyFile()
    {
        var store = new FilekinSettingsStore(_settingsPath);

        await store.SaveAsync(new FilekinSettings());

        Assert.IsTrue(File.Exists(_settingsPath));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_settingsPath));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.GetProperty("locations").ValueKind);
        Assert.AreEqual(0, document.RootElement.GetProperty("locations").GetArrayLength());
    }

    [TestMethod]
    public async Task ValidLocationsKeepTheirOrderAndNormalizeTheirPaths()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _settingsPath,
            """
            {
              // Hand-edited comments and trailing commas are accepted.
              "locations": [
                { "name": "Projects", "path": "D:\\Work\\Projects" },
                { "name": "Archive", "path": "E:\\Archive" },
              ],
              "futureSetting": true
            }
            """);
        var store = new FilekinSettingsStore(_settingsPath);

        var result = await store.LoadAsync();

        Assert.IsFalse(result.IsMalformed);
        Assert.IsEmpty(result.Warnings);
        Assert.HasCount(2, result.Settings.Locations);
        Assert.AreEqual("Projects", result.Settings.Locations[0].Name);
        Assert.AreEqual("Archive", result.Settings.Locations[1].Name);
        Assert.AreEqual(@"D:\Work\Projects", result.Settings.Locations[0].Path);
        Assert.IsNotNull(result.Settings.AdditionalProperties);
        Assert.IsTrue(result.Settings.AdditionalProperties.ContainsKey("futureSetting"));
    }

    [TestMethod]
    public async Task InvalidIndividualLocationsAreSkippedWithoutRejectingValidOnes()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _settingsPath,
            """
            {
              "locations": [
                { "name": "Good", "path": "D:\\Good" },
                { "name": "has spaces", "path": "D:\\Ignored" },
                { "name": "selection", "path": "D:\\Reserved" },
                { "name": "GOOD", "path": "D:\\Duplicate" },
                { "name": "Relative", "path": "folder" }
              ]
            }
            """);
        var store = new FilekinSettingsStore(_settingsPath);

        var result = await store.LoadAsync();

        Assert.HasCount(1, result.Settings.Locations);
        Assert.AreEqual("Good", result.Settings.Locations[0].Name);
        Assert.HasCount(4, result.Warnings);
        Assert.IsFalse(result.IsMalformed);
    }

    [TestMethod]
    public async Task MalformedFileIsLeftByteForByteUnchanged()
    {
        Directory.CreateDirectory(_directory);
        const string malformed = "{ not-json";
        await File.WriteAllTextAsync(_settingsPath, malformed);
        var store = new FilekinSettingsStore(_settingsPath);

        var result = await store.LoadAsync();

        Assert.IsTrue(result.IsMalformed);
        Assert.IsEmpty(result.Settings.Locations);
        Assert.AreEqual(malformed, await File.ReadAllTextAsync(_settingsPath));
    }

    [TestMethod]
    public async Task SaveRoundTripsAndPreservesUnknownFieldsFromALoadedDocument()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _settingsPath,
            """
            {
              "locations": [
                { "name": "Projects", "path": "D:\\Projects", "futureLocationField": 42 }
              ],
              "futureRootField": "keep"
            }
            """);
        var store = new FilekinSettingsStore(_settingsPath);
        var loaded = await store.LoadAsync();

        await store.SaveAsync(loaded.Settings);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_settingsPath));
        var root = document.RootElement;
        Assert.AreEqual("keep", root.GetProperty("futureRootField").GetString());
        Assert.AreEqual(42, root.GetProperty("locations")[0].GetProperty("futureLocationField").GetInt32());
        Assert.AreEqual("Projects", root.GetProperty("locations")[0].GetProperty("name").GetString());
        Assert.IsFalse(Directory.EnumerateFiles(_directory, "*.tmp").Any());
    }

    [TestMethod]
    public void DefaultPathUsesTheConfirmedProductDirectory()
    {
        Assert.AreEqual(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Filekin",
                "settings.json"),
            FilekinSettingsStore.DefaultSettingsPath);
    }
}
