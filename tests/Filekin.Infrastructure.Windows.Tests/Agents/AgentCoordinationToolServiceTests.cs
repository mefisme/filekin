using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class AgentCoordinationToolServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 19, 0, 0, TimeSpan.Zero);
    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-agent-tools-{Guid.NewGuid():N}");
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
    public async Task ClockInAndMessagesUseTheProcessIdentityRatherThanCallerArguments()
    {
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(project);
        var codex = Service(store, project.Id, AgentProvider.Codex);
        var claude = Service(store, project.Id, AgentProvider.ClaudeCode);

        await codex.ClockInAsync("codex-session");
        await claude.ClockInAsync("claude-session");
        var afterMessage = await codex.SendMessageAsync("Review the SQLite boundary on your turn.");

        Assert.AreEqual(AgentProvider.Codex, afterMessage.Caller);
        Assert.HasCount(1, afterMessage.Messages);
        Assert.AreEqual(AgentProvider.Codex, afterMessage.Messages[0].From);
        Assert.AreEqual(AgentProvider.ClaudeCode, afterMessage.Messages[0].To);
        Assert.AreEqual(AgentProjectStatus.Ready, afterMessage.Status);
        Assert.IsFalse(
            typeof(AgentToolParticipantState).GetProperties()
                .Any(property => property.Name.Contains("Session", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SubmitAndAcceptHandoffFollowThePersistedLeaseTransitions()
    {
        var project = ActiveState();
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(project);
        var codex = Service(store, project.Id, AgentProvider.Codex);
        var claude = Service(store, project.Id, AgentProvider.ClaudeCode);

        await store.UpdateAsync(
            project.Id,
            state => AgentProjectCoordinator.RequestHandoff(
                state,
                AgentProvider.Codex,
                AgentHandoffReason.UsageThreshold));
        var submitted = await codex.SubmitHandoffAsync(
            AgentHandoffReason.UsageThreshold,
            "Persistence is complete.",
            "Added state.db tables.",
            "Build the MCP host.",
            "Focused tests pass.",
            string.Empty);

        Assert.AreEqual(AgentProvider.Codex, submitted.ActiveAgent);
        Assert.IsNotNull(submitted.PendingHandoff);
        await Assert.ThrowsAsync<InvalidOperationException>(() => claude.AcceptHandoffAsync());

        await store.UpdateAsync(
            project.Id,
            state => Coordinator().CompleteActiveTurn(
                state,
                AgentProvider.Codex,
                Now.AddMinutes(1)));
        var accepted = await claude.AcceptHandoffAsync();

        Assert.AreEqual(AgentProvider.ClaudeCode, accepted.ActiveAgent);
        Assert.IsNotNull(accepted.LastHandoff?.AcceptedAt);
    }

    [TestMethod]
    public async Task BlockedAndCompletedReportsRetainTheWriterLease()
    {
        var blockedProject = ActiveState();
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(blockedProject);
        var codex = Service(store, blockedProject.Id, AgentProvider.Codex);

        var blocked = await codex.ReportBlockedAsync("Waiting for a user approval.");

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, blocked.Status);
        Assert.AreEqual(AgentProvider.Codex, blocked.ActiveAgent);

        var completionProject = ActiveState(Path.Combine(Path.GetFullPath("."), "second-project"));
        await store.SaveAsync(completionProject);
        var completingCodex = Service(store, completionProject.Id, AgentProvider.Codex);
        var completion = await completingCodex.ReportCompletedAsync();

        Assert.AreEqual(AgentProjectStatus.CompletionPending, completion.Status);
        Assert.AreEqual(AgentProvider.Codex, completion.ActiveAgent);
    }

    [TestMethod]
    public async Task UsageLimitHookCanReportBeforeTheModelClocksIn()
    {
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(project);
        var claude = Service(store, project.Id, AgentProvider.ClaudeCode);

        var limited = await claude.ReportUsageLimitAsync("claude-session");

        Assert.AreEqual(AgentProjectStatus.Paused, limited.Status);
        StringAssert.Contains(limited.AttentionReason, "Claude Code");
        var participant = limited.Participants.Single(
            candidate => candidate.Provider == AgentProvider.ClaudeCode);
        Assert.AreEqual(AgentConnectionState.Unavailable, participant.ConnectionState);
        Assert.IsFalse(
            typeof(AgentToolParticipantState).GetProperties()
                .Any(property => property.Name.Contains("Session", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task OversizedMessageIsRejectedBeforeStateMutation()
    {
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(project);
        var codex = Service(store, project.Id, AgentProvider.Codex);

        await Assert.ThrowsAsync<ArgumentException>(
            () => codex.SendMessageAsync(new string('x', (32 * 1024) + 1)));

        var state = await store.LoadAsync(project.Id);
        Assert.IsNotNull(state);
        Assert.IsEmpty(state.Messages);
    }

    private static AgentCoordinationToolService Service(
        IAgentProjectStore store,
        Guid projectId,
        AgentProvider provider) =>
        new(store, new AgentToolIdentity(projectId, provider), new FixedTimeProvider(Now));

    private static AgentProjectState ActiveState(string? folderPath = null)
    {
        var state = AgentProjectCoordinator.Create(folderPath ?? Path.GetFullPath("."));
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
        new(new AgentCoordinationPolicy(5, 25, TimeSpan.FromMinutes(5)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
