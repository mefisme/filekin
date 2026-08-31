using System.Text.Json;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class LiveCompleteRelayTests
{
    private const string CodexMessage = "Codex completed the first Filekin relay leg.";
    private const string ClaudeMessage = "Claude completed the return Filekin relay leg.";
    private const string RunVariable = "FILEKIN_RUN_LIVE_COMPLETE_RELAY";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task CodexClaudeCodexRoundTripTransfersOneWriterLease()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {RunVariable}=1 to run this explicit subscription-backed relay.");
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

        var probeRoot = Path.Combine(Path.GetTempPath(), $"Filekin-live-complete-relay-{Guid.NewGuid():N}");
        var projectFolder = Path.Combine(probeRoot, "project");
        var stateDatabasePath = Path.Combine(probeRoot, "state.db");
        Directory.CreateDirectory(projectFolder);
        TestContext.WriteLine($"Disposable complete relay: {probeRoot}");

        var project = AgentProjectCoordinator.Create(projectFolder);
        ClaudeBackgroundSessionSnapshot? claudeSession = null;
        CodexThreadSession? codexThread = null;
        CodexTurnHandle? activeCodexTurn = null;
        var activeCodexTurnCompleted = true;
        var claudeAdapter = new ClaudeBackgroundSessionAdapter();
        try
        {
            using var store = new SqliteAgentProjectStore(stateDatabasePath);
            await store.SaveAsync(project);

            var policy = new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(10));
            await using var runtime = new AgentCoordinationRuntime(
                store,
                new AgentProjectCoordinator(policy),
                new FixedUsageSourceFactory(),
                new AgentMcpLaunchConfigurationFactory(mcpExecutablePath, stateDatabasePath),
                TimeProvider.System,
                TimeSpan.FromMinutes(5));
            await runtime.StartAsync();

            var codexTools = new AgentCoordinationToolService(
                store,
                new AgentToolIdentity(project.Id, AgentProvider.Codex));
            var claudeTools = new AgentCoordinationToolService(
                store,
                new AgentToolIdentity(project.Id, AgentProvider.ClaudeCode));
            await codexTools.ClockInAsync("filekin-live-codex-relay");
            await claudeTools.ClockInAsync("filekin-live-claude-relay");

            var selected = await runtime.SelectInitialAgentAsync(project.Id);
            AssertSingleWriter(selected.Project, AgentProvider.Codex);
            var codexMcp = selected.McpServers.Single(server => server.Provider == AgentProvider.Codex);
            var claudeMcp = selected.McpServers.Single(server => server.Provider == AgentProvider.ClaudeCode);

            await using var codexClient = new CodexAppServerClient(codexMcp);
            using (var launchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
            {
                var account = await codexClient.ReadAccountAsync(launchTimeout.Token);
                Assert.IsTrue(
                    account.UsesChatGptSubscription,
                    "The relay must use ChatGPT subscription authentication, never API-key billing.");
                codexThread = await codexClient.StartThreadAsync(projectFolder, launchTimeout.Token);
            }

            await runtime.RequestHandoffAsync(
                project.Id,
                AgentProvider.Codex,
                AgentHandoffReason.UserRequested);
            var firstPrompt =
                "This is the first leg of an explicit disposable Filekin relay. " +
                "Do not inspect files, run commands, edit files, or call non-Filekin tools. " +
                "Call filekin_read_state once. " +
                $"Call filekin_send_message once with text '{CodexMessage}'. " +
                "Call filekin_submit_handoff once with reason 'user_requested', summary 'Codex relay leg complete.', " +
                "completedWork 'Read Filekin state and sent the relay marker.', " +
                "remainingWork 'Accept this handoff and return a handoff to Codex.', " +
                "verification 'Only Filekin MCP tools were called.', blockers ''. " +
                "Then reply briefly and end.";
            activeCodexTurnCompleted = false;
            activeCodexTurn = await StartCodexTurnAsync(
                codexClient,
                codexThread,
                projectFolder,
                firstPrompt);
            var firstTools = await WaitForCodexTurnAsync(codexClient, activeCodexTurn);
            activeCodexTurnCompleted = true;
            Assert.IsTrue(firstTools.Any(tool =>
                tool.EndsWith("filekin_submit_handoff", StringComparison.Ordinal)));

            var afterCodex = await store.LoadAsync(project.Id);
            Assert.IsNotNull(afterCodex);
            AssertSingleWriter(afterCodex, AgentProvider.Codex);
            Assert.AreEqual(AgentProvider.Codex, afterCodex.PendingHandoff?.From);
            Assert.AreEqual(AgentProvider.ClaudeCode, afterCodex.PendingHandoff?.To);
            Assert.AreEqual(CodexMessage, afterCodex.Messages.Single().Text);

            var transferredToClaude = await runtime.ConfirmProviderStoppedAsync(
                project.Id,
                AgentProvider.Codex);
            AssertSingleWriter(transferredToClaude, AgentProvider.ClaudeCode);
            Assert.IsNull(transferredToClaude.PendingHandoff);
            Assert.AreEqual(AgentProvider.Codex, transferredToClaude.LastHandoff?.From);

            await runtime.RequestHandoffAsync(
                project.Id,
                AgentProvider.ClaudeCode,
                AgentHandoffReason.UserRequested);
            var claudePrompt =
                "This is the middle leg of an explicit disposable Filekin relay. " +
                "Do not inspect files, run commands, edit files, or call non-Filekin tools. " +
                "Call filekin_read_state once. Call filekin_accept_handoff once. " +
                $"Call filekin_send_message once with text '{ClaudeMessage}'. " +
                "Call filekin_submit_handoff once with reason 'user_requested', summary 'Claude return leg complete.', " +
                "completedWork 'Accepted Codex handoff and sent the return marker.', " +
                "remainingWork 'Accept the return handoff and complete the disposable relay.', " +
                "verification 'Only Filekin MCP tools were called.', blockers ''. " +
                "Then reply briefly and end.";
            var claudePlan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
                projectFolder,
                "Filekin complete relay",
                claudePrompt,
                claudeMcp);
            using (var launchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
            {
                claudeSession = await claudeAdapter.LaunchAsync(
                    claudePlan.ApproveSharedCheckout(),
                    launchTimeout.Token);
            }

            claudeSession = await WaitForClaudeTurnAsync(
                claudeAdapter,
                projectFolder,
                claudeSession.NativeId);
            Assert.AreEqual(ClaudeBackgroundLifecycle.Completed, claudeSession.Lifecycle);

            var afterClaude = await store.LoadAsync(project.Id);
            Assert.IsNotNull(afterClaude);
            AssertSingleWriter(afterClaude, AgentProvider.ClaudeCode);
            Assert.IsNotNull(afterClaude.LastHandoff?.AcceptedAt);
            Assert.AreEqual(AgentProvider.ClaudeCode, afterClaude.PendingHandoff?.From);
            Assert.AreEqual(AgentProvider.Codex, afterClaude.PendingHandoff?.To);
            Assert.AreEqual(ClaudeMessage, afterClaude.Messages[^1].Text);

            var transferredToCodex = await runtime.ConfirmProviderStoppedAsync(
                project.Id,
                AgentProvider.ClaudeCode);
            AssertSingleWriter(transferredToCodex, AgentProvider.Codex);
            Assert.AreEqual(AgentProvider.ClaudeCode, transferredToCodex.LastHandoff?.From);

            var finalPrompt =
                "This is the final leg of an explicit disposable Filekin relay. " +
                "Do not inspect files, run commands, edit files, or call non-Filekin tools. " +
                "Call filekin_read_state once, call filekin_accept_handoff once, then call " +
                "filekin_report_completed once. Reply briefly and end.";
            activeCodexTurnCompleted = false;
            activeCodexTurn = await StartCodexTurnAsync(
                codexClient,
                codexThread,
                projectFolder,
                finalPrompt);
            var finalTools = await WaitForCodexTurnAsync(codexClient, activeCodexTurn);
            activeCodexTurnCompleted = true;
            Assert.IsTrue(finalTools.Any(tool =>
                tool.EndsWith("filekin_accept_handoff", StringComparison.Ordinal)));
            Assert.IsTrue(finalTools.Any(tool =>
                tool.EndsWith("filekin_report_completed", StringComparison.Ordinal)));

            var completed = await runtime.ConfirmProviderStoppedAsync(project.Id, AgentProvider.Codex);
            Assert.AreEqual(AgentProjectStatus.Completed, completed.Status);
            Assert.IsNull(completed.Lease);
            Assert.IsNull(completed.PendingHandoff);
            Assert.IsNotNull(completed.LastHandoff?.AcceptedAt);
            CollectionAssert.AreEqual(
                new[] { CodexMessage, ClaudeMessage },
                completed.Messages.Select(message => message.Text).ToArray());
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(projectFolder));
            TestContext.WriteLine("Complete Codex → Claude → Codex relay finished with no concurrent writer lease.");

            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await codexClient.DeleteThreadAsync(codexThread.ThreadId, cleanupTimeout.Token);
            codexThread = null;
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (claudeSession is not null && claudeSession.Lifecycle != ClaudeBackgroundLifecycle.Stopped)
            {
                await IgnoreFailureAsync(() => claudeAdapter.StopAsync(
                    projectFolder,
                    claudeSession.NativeId,
                    cleanupTimeout.Token));
            }

            if (codexThread is not null)
            {
                await using var cleanupClient = new CodexAppServerClient(new AgentMcpLaunchConfiguration(
                    AgentProvider.Codex,
                    project.Id,
                    mcpExecutablePath,
                    projectFolder,
                    [
                        "--project", project.Id.ToString("D"),
                        "--provider", "codex",
                        "--state-db", stateDatabasePath,
                    ]));
                if (activeCodexTurn is not null && !activeCodexTurnCompleted)
                {
                    await IgnoreFailureAsync(() => cleanupClient.InterruptTurnAsync(
                        codexThread.ThreadId,
                        activeCodexTurn.TurnId,
                        cleanupTimeout.Token));
                }

                await IgnoreFailureAsync(() => cleanupClient.DeleteThreadAsync(
                    codexThread.ThreadId,
                    cleanupTimeout.Token));
            }

            SqliteConnection.ClearAllPools();
            await DeleteDisposableProbeAsync(probeRoot);
        }
    }

    private static async Task<CodexTurnHandle> StartCodexTurnAsync(
        CodexAppServerClient client,
        CodexThreadSession thread,
        string projectFolder,
        string prompt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        return await client.StartTurnAsync(thread.ThreadId, projectFolder, prompt, timeout.Token);
    }

    private static async Task<IReadOnlyList<string>> WaitForCodexTurnAsync(
        CodexAppServerClient client,
        CodexTurnHandle turn)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var tools = new List<string>();
        var completionTask = ReadCodexCompletionAsync(client, turn, tools, timeout.Token);
        var requestTask = ReadCodexRequestAsync(client, timeout.Token);
        var first = await Task.WhenAny(completionTask, requestTask);
        if (first == requestTask)
        {
            var request = await requestTask;
            await client.InterruptTurnAsync(turn.ThreadId, turn.TurnId, CancellationToken.None);
            Assert.Fail($"Codex requested '{request.Method}'. Filekin did not auto-approve it.");
        }

        var completion = await completionTask;
        timeout.Cancel();
        await IgnoreCancellationAsync(requestTask);
        Assert.AreEqual("completed", completion.Status, ignoreCase: true);
        return tools;
    }

    private static async Task<CodexTurnCompletion> ReadCodexCompletionAsync(
        CodexAppServerClient client,
        CodexTurnHandle turn,
        List<string> tools,
        CancellationToken cancellationToken)
    {
        await foreach (var notification in client.ReadNotificationsAsync(cancellationToken))
        {
            if (string.Equals(notification.Method, "item/completed", StringComparison.Ordinal) &&
                notification.Parameters.TryGetProperty("item", out var item) &&
                ReadString(item, "type") == "mcpToolCall" &&
                ReadString(item, "tool") is { } tool)
            {
                tools.Add(tool);
            }

            if (CodexAppServerProtocol.TryParseTurnCompletion(notification, out var completion) &&
                completion?.TurnId == turn.TurnId)
            {
                return completion;
            }
        }

        throw new EndOfStreamException("The Codex event stream ended before the relay turn completed.");
    }

    private static async Task<CodexAppServerRequest> ReadCodexRequestAsync(
        CodexAppServerClient client,
        CancellationToken cancellationToken)
    {
        await foreach (var request in client.ReadServerRequestsAsync(cancellationToken))
        {
            return request;
        }

        throw new EndOfStreamException("The Codex request stream ended.");
    }

    private static async Task<ClaudeBackgroundSessionSnapshot> WaitForClaudeTurnAsync(
        ClaudeBackgroundSessionAdapter adapter,
        string projectFolder,
        string nativeId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (!timeout.IsCancellationRequested)
        {
            var session = await adapter.ReadAsync(projectFolder, nativeId, timeout.Token)
                ?? throw new InvalidOperationException("Claude no longer reported the disposable relay session.");
            if (session.Lifecycle is ClaudeBackgroundLifecycle.Completed or
                ClaudeBackgroundLifecycle.Stopped or ClaudeBackgroundLifecycle.Failed)
            {
                return session;
            }

            if (session.RequiresOwnerAttention)
            {
                Assert.Fail(
                    $"Claude relay needs owner attention: {session.RawState}/{session.RawStatus} " +
                    $"waiting={session.WaitingFor ?? "unknown"}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
        }

        throw new TimeoutException("Claude did not finish the disposable relay turn.");
    }

    private static void AssertSingleWriter(AgentProjectState state, AgentProvider expectedOwner)
    {
        Assert.IsNotNull(state.Lease);
        Assert.AreEqual(expectedOwner, state.Lease.Owner);
        Assert.AreEqual(expectedOwner, state.ActiveAgent);
        Assert.IsTrue(state.Participant(expectedOwner).TurnState is
            AgentTurnState.Active or AgentTurnState.HandoffRequested or AgentTurnState.CompletionReported);
        Assert.AreEqual(1, state.Participants.Values.Count(participant => participant.TurnState is
            AgentTurnState.Active or AgentTurnState.HandoffRequested or AgentTurnState.CompletionReported));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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

    private static async Task DeleteDisposableProbeAsync(string probeRoot)
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(probeRoot);
        if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("Filekin-live-complete-relay-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing unexpected live-probe cleanup target: {resolved}");
        }

        for (var attempt = 0; Directory.Exists(resolved); attempt++)
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
            }
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

    private sealed class FixedUsageSourceFactory : IAgentUsageSourceFactory
    {
        public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath) =>
            new FixedUsageSource(provider);
    }

    private sealed class FixedUsageSource(AgentProvider provider) : IAgentUsageSource
    {
        public AgentProvider Provider { get; } = provider;

        public Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var used = Provider == AgentProvider.Codex ? 10 : 20;
            return Task.FromResult(new AgentUsageSnapshot(
                Provider,
                DateTimeOffset.UtcNow,
                [new AgentUsageWindow("relay-proof", used, TimeSpan.FromHours(5), null)]));
        }

        public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return await ReadAsync(cancellationToken);
        }
    }
}
