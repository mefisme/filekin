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
    public async Task SessionObservationUsesTheExactNativeSessionAndSurvivesItsStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var live = service.Session(project.Id, AgentProvider.Codex);
        Assert.IsNotNull(live);
        Assert.AreEqual("native-Codex", live.NativeSessionId);
        Assert.AreSame(launcher.LastHandle!.Events, live.Events);
        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(
            "native-Codex",
            persisted!.Participant(AgentProvider.Codex).NativeSessionId,
            "The recorded identity is the session Filekin opened, not anything the agent reported.");

        await service.RequestStopAsync(project.Id);
        launcher.LastHandle.ReportStopped();
        _ = await WaitForAsync(store, project.Id, state => state.Status == AgentProjectStatus.Paused);

        Assert.AreSame(live, service.Session(project.Id, AgentProvider.Codex));
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
        var paused = await WaitForAsync(
            store,
            project.Id,
            state => state.Status == AgentProjectStatus.Paused &&
                service.RunningAgents(project.Id).Count == 0);

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

    [TestMethod]
    public async Task AnAgentWaitingForAPersonSaysSoInsteadOfLookingBusy()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        launcher.LastHandle!.ReportNeedsPerson("Codex is waiting for permission.");
        var blocked = await WaitForAsync(
            store,
            project.Id,
            state => state.Status == AgentProjectStatus.NeedsAttention);

        Assert.AreEqual(
            AgentProvider.Codex,
            blocked.ActiveAgent,
            "A question is not proof that the session stopped, so the turn is kept.");
        StringAssert.Contains(blocked.AttentionReason, "waiting");
        Assert.AreEqual("Codex is waiting for permission.", service.LastReport(project.Id, AgentProvider.Codex));
    }

    [TestMethod]
    public async Task TheOtherAgentIsStartedOnlyWhenThereIsSomethingToHandOver()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);
        Assert.AreEqual(1, launcher.Launches, "Filekin does not keep a second agent idling.");

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                AgentProjectCoordinator.RequestHandoff(
                    current,
                    AgentProvider.Codex,
                    AgentHandoffReason.UserRequested),
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The partner is started at the moment it is needed.");
        StringAssert.Contains(
            launcher.LastRequest!.Prompt,
            "handed this work over",
            "The agent picking up a handoff is told that is what it is doing.");
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task AnAgentThatHandsOverByItselfBringsThePartnerIn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Nobody asked. Codex decided its own part was done, which is the only way a relay can run
        // without a person pressing a button for every leg.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The partner is started at the moment it is needed.");
        Assert.AreEqual(AgentHandoffReason.WorkCompleted, transferred.LastHandoff?.Reason);
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task AnIdleSessionCanBeEndedEvenThoughItHoldsNoTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 2 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Claude clocked in behind the turn: here, waiting, and holding nothing.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(current, AgentProvider.ClaudeCode, usage: null));

        var stopped = await service.StopSessionsAsync(project.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(2, stopped);
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.ClaudeCode);
        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            persisted!.Participant(AgentProvider.ClaudeCode).ConnectionState,
            "An agent whose session ended is no longer here.");
        Assert.AreEqual(
            AgentProvider.Codex,
            persisted.ActiveAgent,
            "Ending somebody else's session never touches the turn.");
        Assert.AreEqual(AgentProjectStatus.Working, persisted.Status);
    }

    [TestMethod]
    public async Task ClosingSeesEverySessionThisWindowHasOpen()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        Assert.AreEqual(0, service.LiveSessions().Count, "Nothing is running before anything starts.");

        await service.StartAsync(project.Id, AgentProvider.Codex);

        CollectionAssert.AreEqual(
            new[] { new AgentLiveSession(project.Id, AgentProvider.Codex) },
            service.LiveSessions().ToArray());
    }

    [TestMethod]
    public async Task AClosingWindowAsksEverySessionToStopAndReportsThatItDid()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.IsNull(
            await service.StopAllSessionsAsync(),
            "Every session was asked to stop, so the window is leaving nothing behind.");
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.Codex);
        Assert.IsTrue(launcher.LastHandle!.StopRequested);
    }

    [TestMethod]
    public async Task AClosingWindowSaysWhenAnAgentCouldNotBeEnded()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store)
        {
            ClockInOnLaunch = true,
            StopSessionsFault = new InvalidOperationException("Claude Code did not answer."),
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var failure = await service.StopAllSessionsAsync();

        Assert.IsNotNull(failure, "A close that left a session running must never report success.");
        StringAssert.Contains(failure, "Claude Code did not answer.");
        Assert.IsNotNull(service.StopFault);
    }

    [TestMethod]
    public async Task OneAgentThatWillNotStopDoesNotSpareTheOthers()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var first = await ApprovedProjectAsync(store, "Tidy the build.");
        var secondFolder = Path.Combine(_projectFolder, "second");
        Directory.CreateDirectory(secondFolder);
        var second = await ApprovedProjectAsync(store, "Tidy the other build.", secondFolder);
        var launcher = new FakeLauncher(store)
        {
            ClockInOnLaunch = true,
            StoppableSessions = 1,
            StopSessionsFault = new InvalidOperationException("Codex did not answer."),
            StopSessionsFaultFor = AgentProvider.Codex,
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(first.Id, AgentProvider.Codex);
        await service.StartAsync(second.Id, AgentProvider.ClaudeCode);

        var failure = await service.StopAllSessionsAsync();

        Assert.IsNotNull(failure);
        CollectionAssert.Contains(
            launcher.StopSessionsCalls,
            AgentProvider.ClaudeCode,
            "The agent that could stop must still have been asked.");
    }

    [TestMethod]
    public async Task EndingTheWorkingAgentsSessionStaysTheCooperativeStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        await service.StopSessionsAsync(project.Id, AgentProvider.Codex);

        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(AgentProjectStatus.StopPending, persisted!.Status);
        Assert.AreEqual(
            AgentProvider.Codex,
            persisted.ActiveAgent,
            "The turn is released only when that provider reports its session ended.");
        Assert.IsTrue(launcher.LastHandle!.StopRequested);
    }

    [TestMethod]
    public async Task AProviderWithoutACooperativeStopSaysSoInsteadOfPretending()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = null };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.IsNull(await service.StopSessionsAsync(project.Id, AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task ASessionThatEndsWithoutTheTurnIsNotTreatedAsALeaseOwnersStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);
        var codex = launcher.LastHandle!;

        // The turn is released first, and only then does the session end.
        var coordinator = new AgentProjectCoordinator(new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));
        await store.UpdateAsync(
            project.Id,
            current => coordinator.CompleteActiveTurn(current, AgentProvider.Codex, DateTimeOffset.UtcNow));
        codex.ReportStopped();

        var persisted = await WaitForAsync(
            store,
            project.Id,
            state => state.Participant(AgentProvider.Codex).ConnectionState == AgentConnectionState.Offline);

        Assert.IsNull(service.StopFault, "A session ending without a turn is not a fault.");
        Assert.IsNull(persisted.Lease);
    }

    [TestMethod]
    public async Task ThePartnerIsStartedEvenWhenTheProjectStillRemembersItAsConnected()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Claude clocked in during an earlier run and its session has long since ended. The project
        // still says it is here, and that is exactly the state that used to leave work sitting still.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(current, AgentProvider.ClaudeCode, usage: null));
        Assert.AreNotEqual(
            AgentConnectionState.Offline,
            (await store.LoadAsync(project.Id))!.Participant(AgentProvider.ClaudeCode).ConnectionState);
        CollectionAssert.DoesNotContain(
            service.RunningAgents(project.Id).ToArray(),
            AgentProvider.ClaudeCode);

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The turn never moves to an agent nobody is running.");
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task StoppingAnAgentNobodyIsWatchingReleasesTheTurnInsteadOfWaitingForever()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 0 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // The session Filekin was watching is gone, as it would be after a restart, while the turn
        // is still recorded against that agent. Nothing will ever report a stop for it.
        await service.DisposeAsync();
        await using var reopened = Service(runtime, store, launcher);
        Assert.IsEmpty(reopened.RunningAgents(project.Id));

        var stopped = await reopened.RequestStopAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.Paused, stopped.Status);
        Assert.IsNull(stopped.Lease, "A turn nobody is running must not hold the project.");
        StringAssert.Contains(stopped.AttentionReason, "resumed");
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.Codex);
    }

    private static AgentHandoff Handoff(AgentProvider from, AgentProvider to) =>
        new(
            Guid.NewGuid(),
            from,
            to,
            DateTimeOffset.UtcNow,
            AgentHandoffReason.UserRequested,
            "First leg done.",
            "Read the state and wrote the marker.",
            "Carry on from here.",
            "Tests pass.",
            string.Empty);

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

    private async Task<AgentProjectState> ApprovedProjectAsync(
        SqliteAgentProjectStore store,
        string objective,
        string? folderPath = null)
    {
        var project = AgentProjectCoordinator.Create(folderPath ?? _projectFolder, objective);
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

        public int? StoppableSessions { get; set; }

        public Exception? StopSessionsFault { get; init; }

        /// <summary>Refuse only this agent, so a close can be proved to ask the rest anyway.</summary>
        public AgentProvider? StopSessionsFaultFor { get; init; }

        public List<AgentProvider> StopSessionsCalls { get; } = [];

        public Task<int?> StopSessionsAsync(
            AgentProvider provider,
            string projectFolderPath,
            CancellationToken cancellationToken = default)
        {
            StopSessionsCalls.Add(provider);
            if (StopSessionsFault is { } fault &&
                (StopSessionsFaultFor is null || StopSessionsFaultFor == provider))
            {
                throw fault;
            }

            return Task.FromResult(StoppableSessions);
        }

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
                    current => AgentProjectCoordinator.ClockIn(current, request.Provider, usage: null),
                    cancellationToken);
            }

            LastHandle = new FakeHandle(request.Provider);
            return LastHandle;
        }
    }

    private sealed class FakeHandle(AgentProvider provider) : IAgentSessionHandle
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentProvider Provider => provider;

        public string NativeSessionId => $"native-{provider}";

        public Task Stopped => _stopped.Task;

        public Task<string> NeedsPerson => _needsPerson.Task;

        public string? LastReport { get; private set; }

        public AgentSessionEventFeed Events { get; } = new();

        public bool StopRequested { get; private set; }

        public void ReportNeedsPerson(string reason)
        {
            LastReport = reason;
            _needsPerson.TrySetResult(reason);
        }

        public void ReportStopped() => _stopped.TrySetResult();

        public Task RequestStopAsync(CancellationToken cancellationToken = default)
        {
            StopRequested = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
