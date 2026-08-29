using System.Text;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeBillingOverrideDetectorTests
{
    private string _directory = null!;
    private string _projectDirectory = null!;
    private string _configurationDirectory = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-Claude-billing-{Guid.NewGuid():N}");
        _projectDirectory = Path.Combine(_directory, "project");
        _configurationDirectory = Path.Combine(_directory, "user-config");
        Directory.CreateDirectory(_projectDirectory);
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
    public void InheritedCredentialsAndEnabledProviderSelectorsAreRefused()
    {
        var apiKey = Detector(new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = "not-retained",
        });
        var bedrock = Detector(new Dictionary<string, string?>
        {
            ["CLAUDE_CODE_USE_BEDROCK"] = "true",
        });

        AssertRefused(apiKey);
        AssertRefused(bedrock);
    }

    [TestMethod]
    public void DisabledProviderSelectorsDoNotCauseAFalseRefusal()
    {
        var zero = Detector(new Dictionary<string, string?>
        {
            ["CLAUDE_CODE_USE_BEDROCK"] = "0",
        });
        var falseValue = Detector(new Dictionary<string, string?>
        {
            ["CLAUDE_CODE_USE_VERTEX"] = "FALSE",
        });

        zero.ThrowIfConfigured(_projectDirectory);
        falseValue.ThrowIfConfigured(_projectDirectory);
    }

    [TestMethod]
    public void UserSharedAndLocalSettingsAreAllInspected()
    {
        WriteSettings(
            Path.Combine(_configurationDirectory, "settings.json"),
            """{"env":{"ANTHROPIC_PROFILE":"work"}}""");
        AssertRefused(Detector());

        File.Delete(Path.Combine(_configurationDirectory, "settings.json"));
        WriteProjectSettings("settings.json", """{"env":{"ANTHROPIC_API_KEY":"secret"}}""");
        AssertRefused(Detector());

        File.Delete(Path.Combine(_projectDirectory, ".claude", "settings.json"));
        WriteProjectSettings(
            "settings.local.json",
            """{"env":{"CLAUDE_CODE_USE_FOUNDRY":"1"}}""");
        AssertRefused(Detector());
    }

    [TestMethod]
    public void ApiKeyHelperIsRefusedWithoutExecutingOrRetainingIt()
    {
        WriteProjectSettings(
            "settings.local.json",
            """{"apiKeyHelper":"C:\\secrets\\get-key.exe"}""");

        AssertRefused(Detector());
    }

    [TestMethod]
    public async Task ClientRefusesBeforeStartingTheConfiguredExecutable()
    {
        WriteProjectSettings(
            "settings.local.json",
            """{"env":{"ANTHROPIC_API_KEY":"not-retained"}}""");
        var client = new ClaudeCliClient("executable-that-does-not-exist", Detector());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadAccountAsync(_projectDirectory));

        StringAssert.Contains(exception.Message, "refused to start Claude Code");
    }

    [TestMethod]
    public void EmptyCredentialsAndDisabledSettingsSelectorsAreAllowed()
    {
        WriteProjectSettings(
            "settings.json",
            """
            {
              "env": {
                "ANTHROPIC_API_KEY": "",
                "CLAUDE_CODE_USE_BEDROCK": "0",
                "CLAUDE_CODE_USE_VERTEX": false
              }
            }
            """);

        Detector().ThrowIfConfigured(_projectDirectory);
    }

    [TestMethod]
    public void ClaudeConfigDirectorySelectsTheApplicableUserSettings()
    {
        var alternateConfiguration = Path.Combine(_directory, "alternate-config");
        WriteSettings(
            Path.Combine(alternateConfiguration, "settings.json"),
            """{"env":{"ANTHROPIC_BASE_URL":"https://gateway.example"}}""");
        var detector = Detector(new Dictionary<string, string?>
        {
            ["CLAUDE_CONFIG_DIR"] = alternateConfiguration,
        });

        AssertRefused(detector);
    }

    [TestMethod]
    public void MalformedSettingsFailClosedWithoutEchoingTheirContents()
    {
        const string secret = "should-not-appear";
        WriteProjectSettings(
            "settings.json",
            $$"""
            {
              "note": "{{secret}}"
            """);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Detector().ThrowIfConfigured(_projectDirectory));

        Assert.IsFalse(exception.ToString().Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnrelatedSettingsDoNotCauseARefusal()
    {
        WriteProjectSettings(
            "settings.json",
            """
            {
              "env": { "CLAUDE_CODE_ENABLE_TELEMETRY": "1" },
              "permissions": { "allow": ["Read(./src/**)"] }
            }
            """);

        Detector().ThrowIfConfigured(_projectDirectory);
    }

    private ClaudeBillingOverrideDetector Detector(
        Dictionary<string, string?>? environment = null) =>
        new(
            variable => environment is not null && environment.TryGetValue(variable, out var value)
                ? value
                : null,
            _configurationDirectory);

    private void AssertRefused(ClaudeBillingOverrideDetector detector)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => detector.ThrowIfConfigured(_projectDirectory));
        StringAssert.Contains(exception.Message, "refused to start Claude Code");
    }

    private void WriteProjectSettings(string fileName, string contents) =>
        WriteSettings(Path.Combine(_projectDirectory, ".claude", fileName), contents);

    private static void WriteSettings(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
