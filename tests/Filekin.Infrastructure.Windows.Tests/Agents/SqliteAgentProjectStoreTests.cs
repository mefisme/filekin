using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class SqliteAgentProjectStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 18, 0, 0, TimeSpan.Zero);
    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-agent-store-{Guid.NewGuid():N}");
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
    public async Task SaveAndLoadRoundTripsCompleteCoordinationState()
    {
        var state = ActiveState();
        state = AgentProjectCoordinator.QueueMessage(
            state,
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            "Check the persistence boundary when your turn starts.",
            Now.AddSeconds(1));
        state = AgentProjectCoordinator.RequestHandoff(
            state,
            AgentProvider.Codex,
            AgentHandoffReason.UserRequested);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentHandoffReason.UserRequested));
        state = Coordinator().CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(3));
        state = AgentProjectCoordinator.AcceptHandoff(
            state,
            AgentProvider.ClaudeCode,
            Now.AddSeconds(4));

        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(state);
        var loaded = await store.LoadAsync(state.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(state.Id, loaded.Id);
        Assert.AreEqual(state.FolderPath, loaded.FolderPath);
        Assert.AreEqual(AgentProvider.ClaudeCode, loaded.ActiveAgent);
        Assert.AreEqual(state.Lease?.Id, loaded.Lease?.Id);
        Assert.HasCount(1, loaded.Messages);
        Assert.AreEqual("Check the persistence boundary when your turn starts.", loaded.Messages[0].Text);
        Assert.AreEqual(Now.AddSeconds(4), loaded.LastHandoff?.AcceptedAt);
        var codexUsage = loaded.Participant(AgentProvider.Codex).Usage;
        Assert.IsNotNull(codexUsage);
        Assert.HasCount(2, codexUsage.Windows);
        Assert.AreEqual("weekly", codexUsage.Windows[1].Name);
    }

    [TestMethod]
    public async Task LoadByFolderUsesWindowsCaseInsensitiveIdentity()
    {
        var state = ReadyState();
        using var store = new SqliteAgentProjectStore(_databasePath);
        await store.SaveAsync(state);

        var loaded = await store.LoadByFolderAsync(state.FolderPath.ToUpperInvariant());

        Assert.IsNotNull(loaded);
        Assert.AreEqual(state.Id, loaded.Id);
    }

    [TestMethod]
    public async Task RestartReconciliationClearsAndPersistsAnUnverifiedLease()
    {
        var state = ActiveState();
        using (var writer = new SqliteAgentProjectStore(_databasePath))
        {
            await writer.SaveAsync(state);
        }

        using (var restarting = new SqliteAgentProjectStore(_databasePath))
        {
            var reconciled = await restarting.ReconcileAfterRestartAsync();
            Assert.HasCount(1, reconciled);
            Assert.IsNull(reconciled[0].Lease);
            Assert.AreEqual(AgentProjectStatus.NeedsAttention, reconciled[0].Status);
        }

        using var reader = new SqliteAgentProjectStore(_databasePath);
        var persisted = await reader.LoadAsync(state.Id);
        Assert.IsNotNull(persisted);
        Assert.IsNull(persisted.Lease);
        Assert.AreEqual(
            AgentTurnState.NeedsAttention,
            persisted.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public async Task ConcurrentStoreInstancesDoNotLoseMessages()
    {
        var state = ReadyState();
        using (var creator = new SqliteAgentProjectStore(_databasePath))
        {
            await creator.SaveAsync(state);
        }

        var updates = Enumerable.Range(0, 8).Select(async index =>
        {
            using var store = new SqliteAgentProjectStore(_databasePath);
            await store.UpdateAsync(
                state.Id,
                current => AgentProjectCoordinator.QueueMessage(
                    current,
                    AgentProvider.Codex,
                    AgentProvider.ClaudeCode,
                    $"message-{index}",
                    Now.AddSeconds(index)));
        });
        await Task.WhenAll(updates);

        using var reader = new SqliteAgentProjectStore(_databasePath);
        var loaded = await reader.LoadAsync(state.Id);
        Assert.IsNotNull(loaded);
        Assert.HasCount(8, loaded.Messages);
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 8).Select(index => $"message-{index}").ToArray(),
            loaded.Messages.Select(message => message.Text).ToArray());
    }

    [TestMethod]
    public async Task NewerSchemaFailsWithoutChangingTheDatabase()
    {
        Directory.CreateDirectory(_directory);
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            await command.ExecuteNonQueryAsync();
        }

        using var store = new SqliteAgentProjectStore(_databasePath);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LoadAllAsync());

        StringAssert.Contains(exception.Message, "newer than this Filekin build");
    }

    [TestMethod]
    public void DefaultPathUsesTheConfirmedProductDirectory()
    {
        Assert.AreEqual(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Filekin",
                "state.db"),
            SqliteAgentProjectStore.DefaultDatabasePath);
    }

    private static AgentProjectState ActiveState() =>
        Coordinator().SelectInitialAgent(ReadyState(), Now);

    private static AgentProjectState ReadyState()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            "codex-session",
            Usage(AgentProvider.Codex, ("five-hour", 10), ("weekly", 20)));
        return AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            "claude-session",
            Usage(AgentProvider.ClaudeCode, ("five-hour", 20), ("weekly", 30)));
    }

    private static AgentUsageSnapshot Usage(
        AgentProvider provider,
        params (string Name, double UsedPercent)[] windows) =>
        new(
            provider,
            Now,
            windows.Select(window => new AgentUsageWindow(
                window.Name,
                window.UsedPercent,
                TimeSpan.FromHours(5),
                Now.AddHours(1))).ToArray());

    private static AgentHandoff Handoff(AgentHandoffReason reason) =>
        new(
            Guid.NewGuid(),
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            Now.AddSeconds(2),
            reason,
            "Persistence is ready for review.",
            "Implemented the SQLite state store.",
            "Expose the narrow MCP tools.",
            "Focused tests pass.",
            string.Empty);

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(5, TimeSpan.FromMinutes(5)));
}
