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

    private AgentCoordinationRuntime Runtime(
        SqliteAgentProjectStore store,
        FakeUsageSourceFactory sources) =>
        new(
            store,
            Coordinator(),
            sources,
            new AgentMcpLaunchConfigurationFactory(_mcpExecutablePath, _databasePath),
            new FixedTimeProvider(Now));

    private AgentProjectState ReadyState()
    {
        var state = AgentProjectCoordinator.Create(_projectFolder);
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            "codex-session",
            Usage(AgentProvider.Codex, 10));
        return AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            "claude-session",
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

        public Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return result switch
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
