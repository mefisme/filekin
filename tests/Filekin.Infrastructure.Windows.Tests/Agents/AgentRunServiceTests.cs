using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentRunServiceTests
{
    private const string Approval = "Let Filekin agents work in this folder itself.";
    private string _databasePath = null!;
    private string _directory = null!;
    private string _mcpExecutablePath = null!;
    private string _projectFolder = null!;

    [TestInitialize]
    public void CreateDisposableState()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-run-{Guid.NewGuid():N}");
        _projectFolder = Path.Combine(_directory, "project");
        Directory.CreateDirectory(_projectFolder);
        _databasePath = Path.Combine(_directory, "state.db");
        _mcpExecutablePath = Path.Combine(_directory, "Filekin.Mcp.exe");
        File.WriteAllText(_mcpExecutablePath, string.Empty);
    }

    [TestCleanup]
    public void RemoveDisposableState()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task NoAgentStartsUntilTheOwnerApprovesWorkingInTheFolderItself()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = AgentProjectCoordinator.Create(_projectFolder, "Tidy the build.");
        await store.SaveAsync(project);
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.StartAsync(project.Id));

        StringAssert.Contains(refusal.Message, "approves");
        Assert.AreEqual(0, launcher.Launches, "A refusal must not reach a provider.");
    }

    [TestMethod]
    public async Task StartingTheChosenAgentWaitsForItToReportBackBeforeGivingItTheTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var started = await service.StartAsync(project.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProvider.ClaudeCode, started.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, started.Status);
        var request = launcher.LastRequest!;
        Assert.AreEqual(AgentProvider.ClaudeCode, request.Provider);
        Assert.AreEqual(Approval, request.Consent.ApprovalDescription);
        Assert.AreEqual(project.Id, request.McpServer.ProjectId);
        StringAssert.Contains(request.Prompt, "Tidy the build.", "The user's own objective is passed through.");
        StringAssert.Contains(request.Prompt, "filekin_clock_in");
        CollectionAssert.AreEqual(
            new[] { AgentProvider.ClaudeCode },
            service.RunningAgents(project.Id).ToArray());
    }

    [TestMethod]
    public async Task WithoutAChoiceFilekinStartsTheAgentWithMoreAllowanceLeft()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store, codexUsedPercent: 70, claudeUsedPercent: 25);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var started = await service.StartAsync(project.Id);

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            started.ActiveAgent,
            "Claude Code has 75 percent left against Codex's 30.");
    }

    [TestMethod]
    public async Task AnAgentThatNeverReportsBackIsAskedToStopAndKeepsNoTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => service.StartAsync(project.Id, AgentProvider.Codex));

        Assert.IsTrue(launcher.LastHandle!.StopRequested, "A session Filekin gave up on is asked to stop.");
        var reloaded = await store.LoadAsync(project.Id);
        Assert.IsNull(reloaded!.Lease, "Nobody may hold the turn after a failed start.");
        Assert.IsEmpty(service.RunningAgents(project.Id));
    }

    [TestMethod]
    public async Task StoppingIsRequestedCooperativelyAndTheTurnIsReleasedOnlyWhenTheSessionEnds()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var stopping = await service.RequestStopAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.StopPending, stopping.Status);
        Assert.AreEqual(AgentProvider.Codex, stopping.ActiveAgent, "Asking is not proof the session ended.");
        Assert.IsTrue(launcher.LastHandle!.StopRequested);

        launcher.LastHandle.ReportStopped();
        var paused = await WaitForAsync(store, project.Id, state => state.Status == AgentProjectStatus.Paused);

        Assert.IsNull(paused.Lease);
        Assert.IsNull(service.StopFault);
        Assert.IsEmpty(service.RunningAgents(project.Id));
    }

    [TestMethod]
    public async Task PassingTheTurnAsksTheWorkingAgentToHandOverEarly()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var passing = await service.PassTheTurnAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.HandoffPending, passing.Status);
        Assert.AreEqual(AgentHandoffReason.UserRequested, passing.RequestedHandoffReason);
        Assert.AreEqual(AgentProvider.Codex, passing.ActiveAgent, "A request must not release the turn.");
        Assert.IsFalse(launcher.LastHandle!.StopRequested, "Passing the turn is not stopping.");
    }

    private static async Task<AgentProjectState> WaitForAsync(
        SqliteAgentProjectStore store,
        Guid projectId,
        Func<AgentProjectState, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var state = await store.LoadAsync(projectId);
            if (state is not null && condition(state))
            {
                return state;
            }

            Assert.IsLessThan(deadline, DateTimeOffset.UtcNow, "The project never reached the expected state.");
            await Task.Delay(20);
        }
    }

    private async Task<AgentProjectState> ApprovedProjectAsync(SqliteAgentProjectStore store, string objective)
    {
        var project = AgentProjectCoordinator.Create(_projectFolder, objective);
        await store.SaveAsync(project);
        return await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                current,
                DateTimeOffset.UtcNow,
                Approval));
    }

    private AgentCoordinationRuntime Runtime(
        IAgentProjectStore store,
        double codexUsedPercent = 20,
        double claudeUsedPercent = 20) =>
        new(
            store,
            Coordinator(),
            new FreshUsageSourceFactory(codexUsedPercent, claudeUsedPercent),
            new AgentMcpLaunchConfigurationFactory(_mcpExecutablePath, _databasePath),
            TimeProvider.System,
            TimeSpan.FromMinutes(1));

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));

    private static AgentRunService Service(
        AgentCoordinationRuntime runtime,
        IAgentProjectStore store,
        IAgentSessionLauncher launcher) =>
        new(
            runtime,
            store,
            Coordinator(),
            launcher,
            TimeProvider.System,
            clockInTimeout: TimeSpan.FromMilliseconds(200),
            clockInPollInterval: TimeSpan.FromMilliseconds(10));

    /// <summary>Reports allowance observed right now, so freshness never depends on test timing.</summary>
    private sealed class FreshUsageSourceFactory(double codexUsedPercent, double claudeUsedPercent)
        : IAgentUsageSourceFactory
    {
        public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath) =>
            new FreshUsageSource(
                provider,
                provider == AgentProvider.Codex ? codexUsedPercent : claudeUsedPercent);

        private sealed class FreshUsageSource(AgentProvider provider, double usedPercent) : IAgentUsageSource
        {
            public AgentProvider Provider => provider;

            public Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new AgentUsageSnapshot(
                    provider,
                    DateTimeOffset.UtcNow,
                    [new AgentUsageWindow("primary", usedPercent, TimeSpan.FromHours(5), null)]));

            public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return await ReadAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Stands in for a provider. It never starts a process, and it clocks the agent in through the
    /// same store the real MCP server writes to, which is exactly what Filekin waits for.
    /// </summary>
    private sealed class FakeLauncher(IAgentProjectStore store) : IAgentSessionLauncher
    {
        public bool ClockInOnLaunch { get; init; }

        public int Launches { get; private set; }

        public AgentSessionLaunchRequest? LastRequest { get; private set; }

        public FakeHandle? LastHandle { get; private set; }

        public async Task<IAgentSessionHandle> LaunchAsync(
            AgentSessionLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            Launches++;
            LastRequest = request;
            if (ClockInOnLaunch)
            {
                await store.UpdateAsync(
                    request.ProjectId,
                    current => AgentProjectCoordinator.ClockIn(
                        current,
                        request.Provider,
                        $"native-{request.Provider}",
                        usage: null),
                    cancellationToken);
            }

            LastHandle = new FakeHandle(request.Provider);
            return LastHandle;
        }
    }

    private sealed class FakeHandle(AgentProvider provider) : IAgentSessionHandle
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentProvider Provider => provider;

        public string NativeSessionId => $"native-{provider}";

        public Task Stopped => _stopped.Task;

        public bool StopRequested { get; private set; }

        public void ReportStopped() => _stopped.TrySetResult();

        public Task RequestStopAsync(CancellationToken cancellationToken = default)
        {
            StopRequested = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
