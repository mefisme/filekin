using System.Text.Json;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class LiveCodexRelayTests
{
    private const string ExpectedMessage =
        "Codex reached Filekin through the project-bound MCP server.";
    private const string RunVariable = "FILEKIN_RUN_LIVE_CODEX_RELAY";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task CodexUsesProjectMcpAndObservesFailClosedLifecycleActions()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {RunVariable}=1 to run this explicit subscription-backed probe.");
        }

        var repositoryRoot = FindRepositoryRoot();
        var mcpExecutablePath = Path.Combine(
            repositoryRoot,
            "src",
            "Filekin.Mcp",
            "bin",
            "Release",
            "net10.0-windows",
            "Filekin.Mcp.exe");
        Assert.IsTrue(File.Exists(mcpExecutablePath), $"Build the Release MCP executable first: {mcpExecutablePath}");

        var probeRoot = Path.Combine(Path.GetTempPath(), $"Filekin-live-Codex-relay-{Guid.NewGuid():N}");
        var projectFolder = Path.Combine(probeRoot, "project");
        var stateDatabasePath = Path.Combine(probeRoot, "state.db");
        Directory.CreateDirectory(projectFolder);
        TestContext.WriteLine($"Disposable probe: {probeRoot}");

        try
        {
            using var store = new SqliteAgentProjectStore(stateDatabasePath);
            var project = AgentProjectCoordinator.Create(projectFolder);
            await store.SaveAsync(project);

            await using var runtime = new AgentCoordinationRuntime(
                store,
                new AgentCoordinationPolicy(10, TimeSpan.FromMinutes(10)),
                mcpExecutablePath);
            await runtime.StartAsync();
            var prepared = await runtime.PrepareProjectAsync(project.Id);
            var codexMcp = prepared.McpServers.Single(server => server.Provider == AgentProvider.Codex);

            await using var client = new CodexAppServerClient(codexMcp);
            CodexThreadSession? thread = null;
            CodexTurnHandle? turn = null;
            var turnCompleted = false;
            try
            {
                using var launchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                var account = await client.ReadAccountAsync(launchTimeout.Token);
                Assert.IsTrue(
                    account.UsesChatGptSubscription,
                    "The live relay must use ChatGPT subscription authentication, never API-key billing.");
                var rateLimits = CodexAppServerProtocol.ParseRateLimits(
                    await client.ReadRateLimitsAsync(launchTimeout.Token),
                    DateTimeOffset.UtcNow);
                TestContext.WriteLine(
                    $"Codex account plan={account.PlanType ?? "unknown"}; minimum remaining=" +
                    $"{rateLimits.MinimumRemainingPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}%.");

                thread = await client.StartThreadAsync(projectFolder, launchTimeout.Token);
                var prompt =
                    "This is an explicit disposable Filekin MCP coordination contract probe. " +
                    "Do not inspect project files, execute commands, create or edit files, or use non-Filekin tools. " +
                    "First call filekin_read_state exactly once. " +
                    "Then intentionally call filekin_accept_handoff exactly once; its error is expected because no lease or handoff exists, so continue. " +
                    "Then intentionally call filekin_report_completed exactly once; its error is expected because this agent has no lease, so continue. " +
                    $"Call filekin_clock_in exactly once with nativeSessionId '{thread.SessionId}'. " +
                    $"Then call filekin_send_message exactly once with text '{ExpectedMessage}'. " +
                    "After the final two Filekin calls succeed, reply briefly and end. Do not call any other tools.";
                turn = await client.StartTurnAsync(thread.ThreadId, projectFolder, prompt, launchTimeout.Token);
                TestContext.WriteLine($"Codex thread={thread.ThreadId}; session={thread.SessionId}; turn={turn.TurnId}.");

                using var observationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var itemTypes = new List<string>();
                var mcpTools = new List<string>();
                var completionTask = WaitForTurnCompletionAsync(
                    client,
                    turn,
                    itemTypes,
                    mcpTools,
                    observationTimeout.Token);
                var requestTask = WaitForServerRequestAsync(client, observationTimeout.Token);
                var first = await Task.WhenAny(completionTask, requestTask);
                if (first == requestTask)
                {
                    var request = await requestTask;
                    await client.InterruptTurnAsync(thread.ThreadId, turn.TurnId, CancellationToken.None);
                    Assert.Fail(
                        $"Codex requested '{request.Method}'. Filekin correctly did not auto-approve the disposable relay.");
                }

                var completion = await completionTask;
                turnCompleted = true;
                observationTimeout.Cancel();
                await IgnoreCancellationAsync(requestTask);

                Assert.AreEqual("completed", completion.Status, ignoreCase: true);
                Assert.IsTrue(
                    CalledExactlyOnce(mcpTools, "filekin_read_state"),
                    "The native event stream did not report exactly one Filekin state-read call.");
                Assert.IsTrue(
                    CalledExactlyOnce(mcpTools, "filekin_accept_handoff"),
                    "The native event stream did not report exactly one expected failed handoff acceptance.");
                Assert.IsTrue(
                    CalledExactlyOnce(mcpTools, "filekin_report_completed"),
                    "The native event stream did not report exactly one expected failed completion report.");
                Assert.IsTrue(
                    CalledExactlyOnce(mcpTools, "filekin_clock_in"),
                    "The native event stream did not report exactly one Filekin clock-in call.");
                Assert.IsTrue(
                    CalledExactlyOnce(mcpTools, "filekin_send_message"),
                    "The native event stream did not report exactly one Filekin message call.");
                Assert.IsFalse(
                    itemTypes.Any(type => type is "commandExecution" or "fileChange"),
                    "The disposable relay unexpectedly executed a command or proposed a file change.");

                var persisted = await store.LoadAsync(project.Id);
                Assert.IsNotNull(persisted);
                Assert.AreEqual(AgentProjectStatus.ClockingIn, persisted.Status);
                Assert.AreEqual(thread.SessionId, persisted.Participant(AgentProvider.Codex).NativeSessionId);
                Assert.AreEqual(
                    AgentConnectionState.UsagePending,
                    persisted.Participant(AgentProvider.Codex).ConnectionState);
                Assert.AreEqual(AgentTurnState.Waiting, persisted.Participant(AgentProvider.Codex).TurnState);
                Assert.IsNull(persisted.Lease);
                Assert.IsNull(persisted.PendingHandoff);
                Assert.IsNull(persisted.LastHandoff);
                var message = Assert.ContainsSingle(persisted.Messages);
                Assert.AreEqual(AgentProvider.Codex, message.From);
                Assert.AreEqual(AgentProvider.ClaudeCode, message.To);
                Assert.AreEqual(ExpectedMessage, message.Text);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(projectFolder));
                TestContext.WriteLine(
                    $"Native turn completed; MCP tools={string.Join(", ", mcpTools)}; message persisted for Claude Code.");
            }
            finally
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (thread is not null && turn is not null && !turnCompleted)
                {
                    await IgnoreFailureAsync(
                        () => client.InterruptTurnAsync(thread.ThreadId, turn.TurnId, cleanupTimeout.Token));
                }

                if (thread is not null)
                {
                    await client.DeleteThreadAsync(thread.ThreadId, cleanupTimeout.Token);
                    TestContext.WriteLine("Deleted the disposable Codex thread.");
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDisposableProbe(probeRoot);
        }
    }

    private static async Task<CodexTurnCompletion> WaitForTurnCompletionAsync(
        CodexAppServerClient client,
        CodexTurnHandle turn,
        ICollection<string> itemTypes,
        ICollection<string> mcpTools,
        CancellationToken cancellationToken)
    {
        await foreach (var notification in client.ReadNotificationsAsync(cancellationToken))
        {
            if (string.Equals(notification.Method, "item/completed", StringComparison.Ordinal) &&
                notification.Parameters.TryGetProperty("item", out var item))
            {
                AddString(item, "type", itemTypes);
                if (ReadString(item, "type") == "mcpToolCall")
                {
                    AddString(item, "tool", mcpTools);
                }
            }

            if (CodexAppServerProtocol.TryParseTurnCompletion(notification, out var completion) &&
                completion?.TurnId == turn.TurnId)
            {
                return completion;
            }
        }

        throw new EndOfStreamException("The Codex App Server event stream ended before the turn completed.");
    }

    private static async Task<CodexAppServerRequest> WaitForServerRequestAsync(
        CodexAppServerClient client,
        CancellationToken cancellationToken)
    {
        await foreach (var request in client.ReadServerRequestsAsync(cancellationToken))
        {
            return request;
        }

        throw new EndOfStreamException("The Codex App Server request stream ended.");
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task IgnoreFailureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool CalledExactlyOnce(IEnumerable<string> tools, string expectedTool) =>
        tools.Count(tool => tool.EndsWith(expectedTool, StringComparison.Ordinal)) == 1;

    private static void AddString(
        JsonElement element,
        string propertyName,
        ICollection<string> values)
    {
        if (ReadString(element, propertyName) is { } value)
        {
            values.Add(value);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static void DeleteDisposableProbe(string probeRoot)
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(probeRoot);
        if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("Filekin-live-Codex-relay-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing unexpected live-probe cleanup target: {resolved}");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
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
