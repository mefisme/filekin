using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;

namespace Filekin.Mcp.Tests;

[TestClass]
[DoNotParallelize]
public sealed class McpStdioIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedToolNames =
    [
        "filekin_clock_in",
        "filekin_read_state",
        "filekin_send_message",
        "filekin_submit_handoff",
        "filekin_accept_handoff",
        "filekin_report_blocked",
        "filekin_report_usage_limit",
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

        var limited = await client.CallToolAsync(
            "filekin_report_usage_limit",
            new Dictionary<string, object?> { ["nativeSessionId"] = "codex-session" },
            cancellationToken: timeout.Token);

        Assert.AreNotEqual(true, limited.IsError);
        StringAssert.Contains(limited.StructuredContent?.ToString(), "Paused");
        StringAssert.Contains(limited.StructuredContent?.ToString(), "Unavailable");
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

    [TestMethod]
    public async Task ConcurrentProjectScopedServersPreserveMessagesFromBothAgents()
    {
        const int messagesPerAgent = 12;
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var codex = await CreateClientAsync(project.Id, "codex", timeout.Token);
        await using var claude = await CreateClientAsync(project.Id, "claude", timeout.Token);

        await Task.WhenAll(
            SendMessagesAsync(codex, "codex", messagesPerAgent, timeout.Token),
            SendMessagesAsync(claude, "claude", messagesPerAgent, timeout.Token));

        using var reader = new SqliteAgentProjectStore(_databasePath);
        var persisted = await reader.LoadAsync(project.Id, timeout.Token);
        Assert.IsNotNull(persisted);
        Assert.HasCount(messagesPerAgent * 2, persisted.Messages);

        var expectedText = Enumerable.Range(0, messagesPerAgent)
            .SelectMany(index => new[] { $"codex-{index}", $"claude-{index}" })
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualText = persisted.Messages
            .Select(message => message.Text)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(expectedText, actualText);
        Assert.AreEqual(
            messagesPerAgent,
            persisted.Messages.Count(
                message => message.From == AgentProvider.Codex && message.To == AgentProvider.ClaudeCode));
        Assert.AreEqual(
            messagesPerAgent,
            persisted.Messages.Count(
                message => message.From == AgentProvider.ClaudeCode && message.To == AgentProvider.Codex));
    }

    [TestMethod]
    public async Task StdioProcessesEnforceTheAppOwnedHandoffLifecycle()
    {
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var codex = await CreateClientAsync(project.Id, "codex", timeout.Token);
        await using var claude = await CreateClientAsync(project.Id, "claude", timeout.Token);

        await AssertSuccessfulCallAsync(
            codex,
            "filekin_clock_in",
            new Dictionary<string, object?> { ["nativeSessionId"] = "codex-native-session" },
            timeout.Token);
        await AssertSuccessfulCallAsync(
            claude,
            "filekin_clock_in",
            new Dictionary<string, object?> { ["nativeSessionId"] = "claude-native-session" },
            timeout.Token);

        using var appStore = new SqliteAgentProjectStore(_databasePath);
        var selected = await appStore.UpdateAsync(
            project.Id,
            state =>
            {
                state = AgentProjectCoordinator.UpdateUsage(
                    state,
                    AgentProvider.Codex,
                    Usage(AgentProvider.Codex, 10));
                state = AgentProjectCoordinator.UpdateUsage(
                    state,
                    AgentProvider.ClaudeCode,
                    Usage(AgentProvider.ClaudeCode, 20));
                return Coordinator().SelectInitialAgent(state, Now);
            },
            timeout.Token);
        Assert.AreEqual(AgentProvider.Codex, selected.ActiveAgent);

        await appStore.UpdateAsync(
            project.Id,
            state => AgentProjectCoordinator.RequestHandoff(
                state,
                AgentProvider.Codex,
                AgentHandoffReason.UsageThreshold),
            timeout.Token);
        await AssertSuccessfulCallAsync(
            codex,
            "filekin_submit_handoff",
            new Dictionary<string, object?>
            {
                ["reason"] = "usage_threshold",
                ["summary"] = "Codex completed the persistence checkpoint.",
                ["completedWork"] = "Verified transactional state.",
                ["remainingWork"] = "Claude should review the next boundary.",
                ["verification"] = "Focused tests passed.",
                ["blockers"] = string.Empty,
            },
            timeout.Token);

        var earlyAcceptance = await claude.CallToolAsync(
            "filekin_accept_handoff",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);
        Assert.AreEqual(true, earlyAcceptance.IsError);

        var transferred = await appStore.UpdateAsync(
            project.Id,
            state => Coordinator().CompleteActiveTurn(
                state,
                AgentProvider.Codex,
                Now.AddMinutes(1)),
            timeout.Token);
        Assert.AreEqual(AgentProvider.ClaudeCode, transferred.ActiveAgent);

        await AssertSuccessfulCallAsync(
            claude,
            "filekin_accept_handoff",
            new Dictionary<string, object?>(),
            timeout.Token);
        await AssertSuccessfulCallAsync(
            claude,
            "filekin_report_completed",
            new Dictionary<string, object?>(),
            timeout.Token);

        var completionPending = await appStore.LoadAsync(project.Id, timeout.Token);
        Assert.IsNotNull(completionPending);
        Assert.AreEqual(AgentProjectStatus.CompletionPending, completionPending.Status);
        Assert.AreEqual(AgentProvider.ClaudeCode, completionPending.Lease?.Owner);
        Assert.AreEqual("codex-native-session", completionPending.Participant(AgentProvider.Codex).NativeSessionId);
        Assert.AreEqual("claude-native-session", completionPending.Participant(AgentProvider.ClaudeCode).NativeSessionId);
        Assert.IsNotNull(completionPending.LastHandoff?.AcceptedAt);

        var completed = await appStore.UpdateAsync(
            project.Id,
            state => AgentProjectCoordinator.CompleteProject(state, AgentProvider.ClaudeCode),
            timeout.Token);
        Assert.AreEqual(AgentProjectStatus.Completed, completed.Status);
        Assert.IsNull(completed.Lease);
    }

    [TestMethod]
    public async Task StdioBlockedAndCompletionReportsCannotImpersonateTheLeaseOwner()
    {
        var project = ActiveState();
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(project);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var codex = await CreateClientAsync(project.Id, "codex", timeout.Token);
        await using var claude = await CreateClientAsync(project.Id, "claude", timeout.Token);

        var impersonatedCompletion = await claude.CallToolAsync(
            "filekin_report_completed",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);
        Assert.AreEqual(true, impersonatedCompletion.IsError);

        await AssertSuccessfulCallAsync(
            codex,
            "filekin_report_blocked",
            new Dictionary<string, object?> { ["reason"] = "Waiting for explicit user input." },
            timeout.Token);

        using var reader = new SqliteAgentProjectStore(_databasePath);
        var persisted = await reader.LoadAsync(project.Id, timeout.Token);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(AgentProjectStatus.NeedsAttention, persisted.Status);
        Assert.AreEqual(AgentProvider.Codex, persisted.Lease?.Owner);
        Assert.AreEqual(AgentTurnState.Blocked, persisted.Participant(AgentProvider.Codex).TurnState);
        Assert.AreEqual(AgentTurnState.Waiting, persisted.Participant(AgentProvider.ClaudeCode).TurnState);
    }

    private static async Task SendMessagesAsync(
        McpClient client,
        string prefix,
        int count,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < count; index++)
        {
            await AssertSuccessfulCallAsync(
                client,
                "filekin_send_message",
                new Dictionary<string, object?> { ["text"] = $"{prefix}-{index}" },
                cancellationToken);
        }
    }

    private static async Task AssertSuccessfulCallAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken);
        Assert.AreNotEqual(true, result.IsError, $"MCP tool '{toolName}' returned an error.");
        Assert.IsNotNull(result.StructuredContent, $"MCP tool '{toolName}' returned no structured state.");
    }

    private static AgentProjectState ActiveState()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            "codex-session",
            Usage(AgentProvider.Codex, 10));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            "claude-session",
            Usage(AgentProvider.ClaudeCode, 20));
        return Coordinator().SelectInitialAgent(state, Now);
    }

    private static AgentUsageSnapshot Usage(AgentProvider provider, double usedPercent) =>
        new(
            provider,
            Now,
            [new AgentUsageWindow("primary", usedPercent, TimeSpan.FromHours(5), Now.AddHours(1))]);

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(5, TimeSpan.FromMinutes(5)));

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
