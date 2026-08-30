using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;

namespace Filekin.Mcp.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PackagedMcpCompanionTests
{
    private const string PackagedAppDirectoryVariable = "FILEKIN_PACKAGED_APP_DIRECTORY";

    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-packaged-mcp-{Guid.NewGuid():N}");
        _databasePath = Path.Combine(_directory, "state.db");
    }

    [TestCleanup]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AppPayloadContainsAWorkingProjectScopedMcpCompanion()
    {
        var appPayloadDirectory = FindAppPayloadDirectory();
        var executablePath = FilekinMcpExecutableLocator.Resolve(appPayloadDirectory);
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = executablePath,
                Arguments =
                [
                    "--project",
                    project.Id.ToString("D"),
                    "--provider",
                    "codex",
                    "--state-db",
                    _databasePath,
                ],
                Name = "Packaged Filekin MCP companion test",
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.IsTrue(tools.Any(tool => tool.Name == "filekin_read_state"));

        var state = await client.CallToolAsync(
            "filekin_read_state",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);
        Assert.AreNotEqual(true, state.IsError);
        StringAssert.Contains(state.StructuredContent?.ToString(), project.Id.ToString("D"));
    }

    private static string FindAppPayloadDirectory()
    {
        if (Environment.GetEnvironmentVariable(PackagedAppDirectoryVariable) is { Length: > 0 } packagedDirectory)
        {
            if (!Path.IsPathFullyQualified(packagedDirectory))
            {
                throw new InvalidOperationException(
                    $"{PackagedAppDirectoryVariable} must contain a fully qualified path.");
            }

            return Path.GetFullPath(packagedDirectory);
        }

        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutputDirectory.Parent?.Name;
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new DirectoryNotFoundException("Could not determine the active test build configuration.");
        }

        var repositoryRoot = FindRepositoryRoot();
        return Path.Combine(
            repositoryRoot,
            "src",
            "Filekin.App",
            "bin",
            configuration,
            "net10.0-windows");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Filekin.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Filekin repository root.");
    }
}
