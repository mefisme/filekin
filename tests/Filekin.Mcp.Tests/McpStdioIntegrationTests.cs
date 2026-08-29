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

    [TestMethod]
    public async Task ProjectScopedServersRouteCodexMessageToClaudeWithoutAModelTurn()
    {
        const string message = "Runtime checkpoint is ready for Claude.";
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using (var codex = await CreateClientAsync(project.Id, "codex", timeout.Token))
        {
            var sent = await codex.CallToolAsync(
                "filekin_send_message",
                new Dictionary<string, object?> { ["text"] = message },
                cancellationToken: timeout.Token);
            Assert.AreNotEqual(true, sent.IsError);
        }

        await using (var claude = await CreateClientAsync(project.Id, "claude", timeout.Token))
        {
            var received = await claude.CallToolAsync(
                "filekin_read_state",
                new Dictionary<string, object?>(),
                cancellationToken: timeout.Token);
            Assert.AreNotEqual(true, received.IsError);
            StringAssert.Contains(received.StructuredContent?.ToString(), message);
        }

        using var reader = new SqliteAgentProjectStore(_databasePath);
        var persisted = await reader.LoadAsync(project.Id);
        Assert.IsNotNull(persisted);
        Assert.HasCount(1, persisted.Messages);
        var routed = persisted.Messages[0];
        Assert.AreEqual(AgentProvider.Codex, routed.From);
        Assert.AreEqual(AgentProvider.ClaudeCode, routed.To);
        Assert.AreEqual(message, routed.Text);
    }

    private Task<McpClient> CreateClientAsync(
        Guid projectId,
        string provider,
        CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    typeof(FilekinAgentTools).Assembly.Location,
                    "--project",
                    projectId.ToString("D"),
                    "--provider",
                    provider,
                    "--state-db",
                    _databasePath,
                ],
                Name = $"Filekin MCP {provider} integration test",
            });

        return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }
}
