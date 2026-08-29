using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;

namespace Filekin.Mcp.Tests;

[TestClass]
public sealed class McpStdioIntegrationTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "filekin_clock_in",
        "filekin_read_state",
        "filekin_send_message",
        "filekin_submit_handoff",
        "filekin_accept_handoff",
        "filekin_report_blocked",
        "filekin_report_completed",
    ];

    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-mcp-{Guid.NewGuid():N}");
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
    public async Task StdioServerPublishesOnlyTheProjectCoordinationTools()
    {
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    typeof(FilekinAgentTools).Assembly.Location,
                    "--project",
                    project.Id.ToString("D"),
                    "--provider",
                    "codex",
                    "--state-db",
                    _databasePath,
                ],
                Name = "Filekin MCP integration test",
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            tools.Select(tool => tool.Name).ToArray());

        var result = await client.CallToolAsync(
            "filekin_read_state",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);

        Assert.AreNotEqual(true, result.IsError);
        Assert.IsNotNull(result.StructuredContent);
        StringAssert.Contains(result.StructuredContent.ToString(), "ClockingIn");
        StringAssert.Contains(result.StructuredContent.ToString(), "Codex");
    }
}
