using System.Runtime.CompilerServices;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class AgentCoordinationRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private string _databasePath = null!;
    private string _directory = null!;
    private string _mcpExecutablePath = null!;
    private string _projectFolder = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-agent-runtime-{Guid.NewGuid():N}");
        _databasePath = Path.Combine(_directory, "state.db");
        _mcpExecutablePath = Path.Combine(_directory, "Filekin.Mcp.exe");
        _projectFolder = Path.Combine(_directory, "project");
        Directory.CreateDirectory(_projectFolder);
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
    public async Task ProjectOperationsAreRefusedUntilStartupReconciliationCompletes()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        await using var runtime = Runtime(store, sources);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.PrepareProjectAsync(state.Id));

        StringAssert.Contains(exception.Message, "startup reconciliation");
        Assert.AreEqual(0, sources.TotalReads);
    }

    [TestMethod]
    public async Task StartupPersistsLeaseInvalidationBeforePreparingMcpIdentities()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = Coordinator().SelectInitialAgent(ReadyState(), Now);
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        await using var runtime = Runtime(store, sources);

        var reconciled = await runtime.StartAsync();
        var prepared = await runtime.PrepareProjectAsync(state.Id);

        Assert.HasCount(1, reconciled);
        Assert.IsNull(prepared.Project.Lease);
        Assert.AreEqual(AgentProjectStatus.NeedsAttention, prepared.Project.Status);
        Assert.AreEqual(2, sources.TotalReads);
        Assert.HasCount(2, prepared.McpServers);
        AssertMcpIdentity(prepared.McpServers[0], state.Id, AgentProvider.Codex, "codex");
        AssertMcpIdentity(prepared.McpServers[1], state.Id, AgentProvider.ClaudeCode, "claude");
    }

    [TestMethod]
    public async Task InitialSelectionRefreshesProviderFactsBeforeGrantingOneLease()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = new FakeUsageSourceFactory(
            Usage(AgentProvider.Codex, 40),
            Usage(AgentProvider.ClaudeCode, 20));
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();

        var selected = await runtime.SelectInitialAgentAsync(state.Id);

        Assert.AreEqual(AgentProvider.ClaudeCode, selected.Project.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, selected.Project.Status);
        Assert.AreEqual(2, sources.TotalReads);
        Assert.AreEqual(
            AgentTurnState.Waiting,
            selected.Project.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public async Task APrepareRefreshAsksTheActiveOwnerToHandOffOnceItsUsageCrossesTheThreshold()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = new FakeUsageSourceFactory(
            Usage(AgentProvider.Codex, 75),
            Usage(AgentProvider.ClaudeCode, 85));
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();

        var selected = await runtime.SelectInitialAgentAsync(state.Id);
        Assert.AreEqual(
            AgentProvider.Codex,
            selected.Project.ActiveAgent,
            "Codex has more remaining headroom (25 vs 15) at the moment the lease is first granted.");
        Assert.AreEqual(AgentProjectStatus.Working, selected.Project.Status);

        var prepared = await runtime.PrepareProjectAsync(state.Id);

        Assert.AreEqual(AgentProjectStatus.HandoffPending, prepared.Project.Status);
        Assert.AreEqual(AgentHandoffReason.UsageThreshold, prepared.Project.RequestedHandoffReason);
        Assert.AreEqual(AgentProvider.Codex, prepared.Project.ActiveAgent, "A request must not release the lease.");
        Assert.AreEqual(
            AgentTurnState.HandoffRequested,
            prepared.Project.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public async Task AFolderIsOnlyAnAgentProjectAfterItExplicitlyOptsIn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var sources = SuccessfulSources();
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();

        Assert.IsNull(
            await runtime.FindProjectAsync(_projectFolder),
            "Looking at a folder must not opt it in.");
        Assert.AreEqual(0, sources.TotalReads, "Looking must not probe a provider.");

        var created = await runtime.CreateProjectAsync(_projectFolder, "Add the /agents surface.");

        Assert.AreEqual(_projectFolder, created.FolderPath);
        Assert.AreEqual("Add the /agents surface.", created.Objective);
        Assert.IsNull(created.Lease, "Opting in grants no turn.");
        Assert.IsTrue(created.Participants.Values.All(
            participant => participant.ConnectionState == AgentConnectionState.Offline));
        Assert.AreEqual(0, sources.TotalReads, "Opting in must not start or probe a provider.");

        var found = await runtime.FindProjectAsync(_projectFolder);
        Assert.IsNotNull(found);
        Assert.AreEqual(created.Id, found.Id);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.CreateProjectAsync(_projectFolder, "A second project."));
        StringAssert.Contains(again.Message, "already an agent project");
    }

    [TestMethod]
    public async Task TheObjectiveCanBeSuppliedAfterTheProjectExists()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        await using var runtime = Runtime(store, SuccessfulSources());
        await runtime.StartAsync();
        var created = await runtime.CreateProjectAsync(_projectFolder, string.Empty);
        Assert.AreEqual(string.Empty, created.Objective);

        var described = await runtime.SetObjectiveAsync(created.Id, "  Ship the control room.  ");

        Assert.AreEqual("Ship the control room.", described.Objective);
        var reloaded = await store.LoadAsync(created.Id);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual("Ship the control room.", reloaded.Objective);
    }

    [TestMethod]
    public async Task ALongRunningTurnIsRefreshedPeriodicallyUntilItsUsageCrossesTheThreshold()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = new FakeUsageSourceFactory(
            Usage(AgentProvider.Codex, 10),
            Usage(AgentProvider.ClaudeCode, 20));
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(store, sources, timeProvider);
        await runtime.StartAsync();

        var selected = await runtime.SelectInitialAgentAsync(state.Id);
        Assert.AreEqual(AgentProvider.Codex, selected.Project.ActiveAgent);
        var timer = timeProvider.ActiveTimers.Single();
        Assert.AreEqual(RefreshInterval, timer.DueTime);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, timer.Period, "Each tick rearms itself so refreshes cannot overlap.");

        // Nothing else asks the runtime anything: the turn simply keeps running while Codex spends its
        // allowance, and only the periodic refresh notices.
        timer.Fire();
        await runtime.InTurnRefreshActivity;

        Assert.AreEqual(4, sources.TotalReads);
        var working = await store.LoadAsync(state.Id);
        Assert.IsNotNull(working);
        Assert.AreEqual(AgentProjectStatus.Working, working.Status, "Full allowance must not request a handoff.");
        Assert.AreEqual(RefreshInterval, timer.DueTime);

        sources.Set(AgentProvider.Codex, Usage(AgentProvider.Codex, 75));
        timer.Fire();
        await runtime.InTurnRefreshActivity;

        var requested = await store.LoadAsync(state.Id);
        Assert.IsNotNull(requested);
        Assert.AreEqual(AgentProjectStatus.HandoffPending, requested.Status);
        Assert.AreEqual(AgentHandoffReason.UsageThreshold, requested.RequestedHandoffReason);
        Assert.AreEqual(AgentProvider.Codex, requested.ActiveAgent, "A request must not release the lease.");
        Assert.IsEmpty(timeProvider.ActiveTimers, "The request stands, so the project stops re-asking.");
        Assert.IsNull(runtime.InTurnRefreshFault(state.Id));
    }

    [TestMethod]
    public async Task NoTurnMeansNoPeriodicRefresh()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(store, sources, timeProvider);
        await runtime.StartAsync();

        var prepared = await runtime.PrepareProjectAsync(state.Id);

        Assert.IsNull(prepared.Project.Lease);
        Assert.IsEmpty(timeProvider.ActiveTimers);
    }

    [TestMethod]
    public async Task DisposalStopsThePeriodicRefresh()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        var runtime = Runtime(store, sources, timeProvider);
        await runtime.StartAsync();
        await runtime.SelectInitialAgentAsync(state.Id);
        Assert.HasCount(1, timeProvider.ActiveTimers);

        await runtime.DisposeAsync();

        Assert.IsEmpty(timeProvider.ActiveTimers);
    }

    [TestMethod]
    public async Task AnUnexpectedPeriodicRefreshFailureStopsItInsteadOfRetryingSilently()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var failing = new FailingLoadStore(store);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(failing, sources, timeProvider);
        await runtime.StartAsync();
        await runtime.SelectInitialAgentAsync(state.Id);
        var timer = timeProvider.ActiveTimers.Single();

        failing.FailingProjectId = state.Id;
        timer.Fire();
        await runtime.InTurnRefreshActivity;

        Assert.IsInstanceOfType<IOException>(runtime.InTurnRefreshFault(state.Id));
        Assert.IsEmpty(timeProvider.ActiveTimers);

        failing.FailingProjectId = null;
        var resumed = await runtime.PrepareProjectAsync(state.Id);

        Assert.AreEqual(AgentProjectStatus.Working, resumed.Project.Status);
        Assert.IsNull(
            runtime.InTurnRefreshFault(state.Id),
            "An explicit operation restarts the periodic refresh.");
        Assert.HasCount(1, timeProvider.ActiveTimers);
    }

    [TestMethod]
    public async Task OneProjectsHealthyRefreshLeavesAnotherProjectsStoppedWatcherReported()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var stopped = ReadyState();
        var healthy = ReadyState(Path.Combine(_directory, "second-project"));
        await store.SaveAsync(stopped);
        await store.SaveAsync(healthy);
        var failing = new FailingLoadStore(store);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(failing, sources, timeProvider);
        await runtime.StartAsync();
        await runtime.SelectInitialAgentAsync(stopped.Id);
        await runtime.SelectInitialAgentAsync(healthy.Id);
        Assert.HasCount(2, timeProvider.ActiveTimers);

        failing.FailingProjectId = stopped.Id;
        Timer(timeProvider, stopped.Id).Fire();
        await runtime.InTurnRefreshActivity;
        Timer(timeProvider, healthy.Id).Fire();
        await runtime.InTurnRefreshActivity;

        Assert.IsInstanceOfType<IOException>(
            runtime.InTurnRefreshFault(stopped.Id),
            "The healthy project must not clear another project's fault.");
        Assert.IsNull(runtime.InTurnRefreshFault(healthy.Id));
        Assert.AreEqual(
            healthy.Id,
            timeProvider.ActiveTimers.Single().State,
            "Only the failed project's watcher stops.");
    }

    [TestMethod]
    public async Task FailedProviderRefreshesAreRecordedWithoutGrantingALease()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = FakeUsageSourceFactory.FailingBoth();
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();

        var selected = await runtime.SelectInitialAgentAsync(state.Id);

        Assert.IsNull(selected.Project.Lease);
        Assert.AreEqual(AgentProjectStatus.Paused, selected.Project.Status);
        Assert.IsTrue(selected.ProviderRefreshes.All(
            result => result.Status == AgentProviderRefreshStatus.Unavailable));
        Assert.IsTrue(selected.Project.Participants.Values.All(
            participant => participant.ConnectionState == AgentConnectionState.Unavailable));
    }

    [TestMethod]
    public async Task ConfirmedStopRefreshesRecipientAndPausesFailedHandoffSafely()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = new FakeUsageSourceFactory(
            Usage(AgentProvider.Codex, 10),
            new InvalidOperationException("Claude unavailable"));
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();
        await store.UpdateAsync(
            state.Id,
            current =>
            {
                current = Coordinator().SelectInitialAgent(current, Now);
                current = AgentProjectCoordinator.RequestHandoff(
                    current,
                    AgentProvider.Codex,
                    AgentHandoffReason.UserRequested);
                return AgentProjectCoordinator.SubmitHandoff(
                    current,
                    Handoff(AgentHandoffReason.UserRequested));
            });

        var stopped = await runtime.ConfirmProviderStoppedAsync(state.Id, AgentProvider.Codex);

        Assert.IsNull(stopped.Lease);
        Assert.AreEqual(AgentProjectStatus.Paused, stopped.Status);
        Assert.IsNotNull(stopped.LastHandoff);
        Assert.AreEqual(
            AgentConnectionState.Unavailable,
            stopped.Participant(AgentProvider.ClaudeCode).ConnectionState);
        Assert.AreEqual(1, sources.Reads(AgentProvider.ClaudeCode));
        Assert.AreEqual(0, sources.Reads(AgentProvider.Codex));
    }

    [TestMethod]
    public async Task CompletionRequiresProviderConfirmedStopButDoesNotDispatchAnotherAgent()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();
        await store.UpdateAsync(
            state.Id,
            current =>
            {
                current = Coordinator().SelectInitialAgent(current, Now);
                return AgentProjectCoordinator.ReportCompleted(current, AgentProvider.Codex);
            });

        var completed = await runtime.ConfirmProviderStoppedAsync(state.Id, AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.Completed, completed.Status);
        Assert.IsNull(completed.Lease);
        Assert.AreEqual(0, sources.TotalReads);
    }

    [TestMethod]
    public async Task TheUserCanChooseTheAgentThatStartsEvenWhenTheOtherHasMoreLeft()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = new FakeUsageSourceFactory(
            Usage(AgentProvider.Codex, 20),
            Usage(AgentProvider.ClaudeCode, 40));
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();

        var selected = await runtime.SelectInitialAgentAsync(state.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            selected.Project.ActiveAgent,
            "Codex has more allowance left, but the user chose Claude Code.");
        Assert.AreEqual(AgentProjectStatus.Working, selected.Project.Status);
    }

    [TestMethod]
    public async Task AStopTheUserAskedForKeepsTheProjectAndStopsWatchingTheTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(store, sources, timeProvider);
        await runtime.StartAsync();
        var selected = await runtime.SelectInitialAgentAsync(state.Id);
        var owner = selected.Project.ActiveAgent!.Value;
        Assert.HasCount(1, timeProvider.ActiveTimers);

        var stopping = await runtime.RequestStopAsync(state.Id, owner);
        Assert.AreEqual(AgentProjectStatus.StopPending, stopping.Status);
        Assert.AreEqual(owner, stopping.ActiveAgent, "A stop request must not release the lease.");

        var stopped = await runtime.ConfirmProviderStoppedAsync(state.Id, owner);

        Assert.AreEqual(AgentProjectStatus.Paused, stopped.Status);
        Assert.IsNull(stopped.Lease);
        Assert.IsEmpty(timeProvider.ActiveTimers, "Nobody holds the turn, so nothing is watched.");

        var reloaded = await store.LoadAsync(state.Id);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(AgentProjectStatus.Paused, reloaded!.Status, "The project is kept, not thrown away.");

        var resumed = await runtime.ResumeAsync(state.Id);

        Assert.AreEqual(AgentProjectStatus.Ready, resumed.Status);
        Assert.IsNull(resumed.AttentionReason);
    }

    [TestMethod]
    public async Task ANewJobPersistsAReadyProjectAndStartsNoProvider()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        var sources = SuccessfulSources();
        var timeProvider = new ManualTimeProvider(Now);
        await using var runtime = Runtime(store, sources, timeProvider);
        await runtime.StartAsync();
        var selected = await runtime.SelectInitialAgentAsync(state.Id);
        var owner = selected.Project.ActiveAgent!.Value;
        var completed = await store.UpdateAsync(
            state.Id,
            current => AgentProjectCoordinator.CompleteProject(current, owner));
        Assert.AreEqual(AgentProjectStatus.Completed, completed.Status);
        var readsBefore = sources.TotalReads;

        var reopened = await runtime.StartNewObjectiveAsync(state.Id, "Write the release notes.");

        Assert.AreEqual(AgentProjectStatus.Ready, reopened.Status);
        Assert.AreEqual("Write the release notes.", reopened.Objective);
        Assert.IsNull(reopened.Lease, "A new job grants no turn; Start work still does that.");
        Assert.IsEmpty(timeProvider.ActiveTimers, "Nobody holds the turn, so nothing is watched.");
        Assert.AreEqual(readsBefore, sources.TotalReads, "Opening a new job contacts no provider.");

        var reloaded = await store.LoadAsync(state.Id);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(AgentProjectStatus.Ready, reloaded!.Status);
        Assert.AreEqual("Write the release notes.", reloaded.Objective);
        Assert.IsNull(reloaded.Participant(AgentProvider.Codex).NativeSessionId);
        Assert.IsNull(reloaded.Participant(AgentProvider.ClaudeCode).NativeSessionId);
    }

    [TestMethod]
    public async Task TheRecordedNativeSessionIsFilekinsOwnAndGrantsNoTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var state = ReadyState();
        await store.SaveAsync(state);
        await using var runtime = Runtime(store, SuccessfulSources());
        await runtime.StartAsync();

        var recorded = await runtime.RecordNativeSessionAsync(
            state.Id,
            AgentProvider.Codex,
            "codex-session-filekin-opened");

        Assert.AreEqual("codex-session-filekin-opened", recorded.Participant(AgentProvider.Codex).NativeSessionId);
        Assert.IsNull(recorded.Lease);

        var reloaded = await store.LoadAsync(state.Id);
        Assert.AreEqual(
            "codex-session-filekin-opened",
            reloaded!.Participant(AgentProvider.Codex).NativeSessionId);
        await Assert.ThrowsAsync<ArgumentException>(
            () => runtime.RecordNativeSessionAsync(state.Id, AgentProvider.Codex, "  "));
    }

    private AgentCoordinationRuntime Runtime(
        IAgentProjectStore store,
        FakeUsageSourceFactory sources,
        TimeProvider? timeProvider = null) =>
        new(
            store,
            Coordinator(),
            sources,
            new AgentMcpLaunchConfigurationFactory(_mcpExecutablePath, _databasePath),
            timeProvider ?? new FixedTimeProvider(Now),
            RefreshInterval);

    private static ManualTimer Timer(ManualTimeProvider timeProvider, Guid projectId) =>
        timeProvider.ActiveTimers.Single(timer => Equals(timer.State, projectId));

    private AgentProjectState ReadyState() => ReadyState(_projectFolder);

    private static AgentProjectState ReadyState(string projectFolder)
    {
        Directory.CreateDirectory(projectFolder);
        var state = AgentProjectCoordinator.Create(projectFolder);
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 10));
        return AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, 20));
    }

    private void AssertMcpIdentity(
        AgentMcpLaunchConfiguration configuration,
        Guid projectId,
        AgentProvider provider,
        string providerArgument)
    {
        Assert.AreEqual(provider, configuration.Provider);
        Assert.AreEqual(projectId, configuration.ProjectId);
        Assert.AreEqual(_mcpExecutablePath, configuration.ExecutablePath);
        Assert.AreEqual(_projectFolder, configuration.WorkingDirectory);
        CollectionAssert.AreEqual(
            new[]
            {
                "--project",
                projectId.ToString("D"),
                "--provider",
                providerArgument,
                "--state-db",
                _databasePath,
            },
            configuration.Arguments.ToArray());
    }

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));

    private static FakeUsageSourceFactory SuccessfulSources() =>
        new(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20));

    private static AgentUsageSnapshot Usage(AgentProvider provider, double usedPercent) =>
        new(
            provider,
            Now,
            [new AgentUsageWindow("primary", usedPercent, TimeSpan.FromHours(5), Now.AddHours(1))]);

    private static AgentHandoff Handoff(AgentHandoffReason reason) =>
        new(
            Guid.NewGuid(),
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            Now,
            reason,
            "Runtime handoff is ready.",
            "Implemented the runtime boundary.",
            "Verify the recipient transition.",
            "Focused tests pass.",
            string.Empty);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A fixed clock whose timers only ever fire when a test fires them.</summary>
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public IReadOnlyList<ManualTimer> ActiveTimers =>
            _timers.Where(timer => !timer.IsDisposed).ToArray();

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;

        public TimeSpan Period { get; private set; } = period;

        public object? State => state;

        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            if (IsDisposed)
            {
                return false;
            }

            DueTime = newDueTime;
            Period = newPeriod;
            return true;
        }

        public void Fire()
        {
            Assert.IsFalse(IsDisposed, "A stopped timer cannot fire.");
            Assert.AreNotEqual(Timeout.InfiniteTimeSpan, DueTime, "A disarmed timer cannot fire.");
            callback(state);
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Turns one project's reads into an unexpected runtime failure on demand.</summary>
    private sealed class FailingLoadStore(IAgentProjectStore inner) : IAgentProjectStore
    {
        public Guid? FailingProjectId { get; set; }

        public Task SaveAsync(AgentProjectState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public Task<AgentProjectState?> LoadAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            FailingProjectId == projectId
                ? Task.FromException<AgentProjectState?>(new IOException("The state database is unreadable."))
                : inner.LoadAsync(projectId, cancellationToken);

        public Task<AgentProjectState?> LoadByFolderAsync(
            string folderPath,
            CancellationToken cancellationToken = default) =>
            inner.LoadByFolderAsync(folderPath, cancellationToken);

        public Task<IReadOnlyList<AgentProjectState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            inner.LoadAllAsync(cancellationToken);

        public Task<AgentProjectState> UpdateAsync(
            Guid projectId,
            Func<AgentProjectState, AgentProjectState> transition,
            CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(projectId, transition, cancellationToken);

        public Task<IReadOnlyList<AgentProjectState>> ReconcileAfterRestartAsync(
            CancellationToken cancellationToken = default) =>
            inner.ReconcileAfterRestartAsync(cancellationToken);
    }

    private sealed class FakeUsageSourceFactory : IAgentUsageSourceFactory
    {
        private readonly Dictionary<AgentProvider, FakeUsageSource> _sources;

        public FakeUsageSourceFactory(object codexResult, object claudeResult)
        {
            _sources = new Dictionary<AgentProvider, FakeUsageSource>
            {
                [AgentProvider.Codex] = new(AgentProvider.Codex, codexResult),
                [AgentProvider.ClaudeCode] = new(AgentProvider.ClaudeCode, claudeResult),
            };
        }

        public int TotalReads => _sources.Values.Sum(source => source.ReadCount);

        public void Set(AgentProvider provider, AgentUsageSnapshot usage) =>
            _sources[provider].Result = usage;

        public static FakeUsageSourceFactory FailingBoth() =>
            new(
                new InvalidOperationException("Codex unavailable"),
                new InvalidOperationException("Claude unavailable"));

        public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(projectFolderPath));
            return _sources[provider];
        }

        public int Reads(AgentProvider provider) => _sources[provider].ReadCount;
    }

    private sealed class FakeUsageSource(AgentProvider provider, object result) : IAgentUsageSource
    {
        public AgentProvider Provider => provider;

        public int ReadCount { get; private set; }

        public object Result { get; set; } = result;

        public Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Result switch
            {
                AgentUsageSnapshot snapshot => Task.FromResult(snapshot),
                Exception exception => Task.FromException<AgentUsageSnapshot>(exception),
                _ => throw new InvalidOperationException("Unsupported fake result."),
            };
        }

        public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }
}
