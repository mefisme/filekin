using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class LiveClaudeRelayTests
{
    private const string RunVariable = "FILEKIN_RUN_LIVE_CLAUDE_RELAY";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresLiveProvider")]
    [TestCategory("ConsumesSubscriptionUsage")]
    public async Task DepletedClaudeReportsStructuredUsageLimit()
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

        var probeRoot = Path.Combine(Path.GetTempPath(), $"Filekin-live-Claude-relay-{Guid.NewGuid():N}");
        var projectFolder = Path.Combine(probeRoot, "project");
        var stateDatabasePath = Path.Combine(probeRoot, "state.db");
        Directory.CreateDirectory(projectFolder);
        TestContext.WriteLine($"Disposable probe: {probeRoot}");

        using var store = new SqliteAgentProjectStore(stateDatabasePath);
        var project = AgentProjectCoordinator.Create(projectFolder);
        await store.SaveAsync(project);

        await using var runtime = new AgentCoordinationRuntime(
            store,
            new AgentCoordinationPolicy(10, TimeSpan.FromMinutes(10)),
            mcpExecutablePath);
        await runtime.StartAsync();
        var prepared = await runtime.PrepareProjectAsync(project.Id);
        var claudeMcp = prepared.McpServers.Single(server => server.Provider == AgentProvider.ClaudeCode);

        var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            projectFolder,
            "Filekin Claude allowance probe",
            "This is a Filekin relay availability probe. Do not inspect, create, edit, or delete files. Call filekin_read_state once, then end.",
            claudeMcp);
        var adapter = new ClaudeBackgroundSessionAdapter();

        using var launchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var session = await adapter.LaunchAsync(plan.ApproveSharedCheckout(), launchTimeout.Token);
        TestContext.WriteLine($"Claude session {session.NativeId}: {session.Lifecycle} ({session.RawState}/{session.RawStatus})");

        AgentProjectState? observedState = null;
        ClaudeBackgroundSessionSnapshot? observedSession = session;
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!observationTimeout.IsCancellationRequested)
        {
            observedState = await store.LoadAsync(project.Id, observationTimeout.Token);
            Assert.IsNotNull(observedState);
            var participant = observedState.Participant(AgentProvider.ClaudeCode);
            if (participant.ConnectionState == AgentConnectionState.Unavailable &&
                participant.NativeSessionId is not null &&
                observedState.AttentionReason?.Contains("usage limit", StringComparison.OrdinalIgnoreCase) == true)
            {
                TestContext.WriteLine(
                    $"Structured callback recorded: project={observedState.Status}, provider={participant.ConnectionState}, turn={participant.TurnState}.");
                Assert.IsNull(observedState.Lease, "A pre-turn usage-limit callback must not create a writer lease.");
                var stopped = await adapter.StopAsync(projectFolder, session.NativeId);
                Assert.IsNotNull(stopped, "Claude no longer reported the disposable session after Filekin requested its stop.");
                Assert.AreEqual(ClaudeBackgroundLifecycle.Stopped, stopped.Lifecycle);
                TestContext.WriteLine("Provider-confirmed stop recorded for the disposable session.");
                return;
            }

            observedSession = await adapter.ReadAsync(projectFolder, session.NativeId, observationTimeout.Token);
            if (observedSession is null || observedSession.Lifecycle is
                ClaudeBackgroundLifecycle.Completed or
                ClaudeBackgroundLifecycle.Stopped or
                ClaudeBackgroundLifecycle.Failed)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), observationTimeout.Token);
        }

        if (observedSession is not null && observedSession.Lifecycle is not (
            ClaudeBackgroundLifecycle.Completed or
            ClaudeBackgroundLifecycle.Stopped or
            ClaudeBackgroundLifecycle.Failed))
        {
            await adapter.StopAsync(projectFolder, session.NativeId);
        }

        observedState ??= await store.LoadAsync(project.Id);
        Assert.Fail(
            "Claude did not deliver the structured usage-limit callback. " +
            $"Last lifecycle: {observedSession?.Lifecycle.ToString() ?? "missing"}; " +
            $"project state: {observedState?.Status.ToString() ?? "missing"}. " +
            "This can mean Claude allowance was available or the native hook did not run.");
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
