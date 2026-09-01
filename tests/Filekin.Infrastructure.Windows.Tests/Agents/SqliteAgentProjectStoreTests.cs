using System.Globalization;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows;
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
    public async Task UpgradingFromPerProjectUsageKeepsTheNewestReadingPerProvider()
    {
        Directory.CreateDirectory(_directory);
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        // Build a schema 7 database by hand: two projects, each holding its own copy of the same
        // account fact, which is exactly the shape this migration exists to collapse.
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 PRAGMA user_version = 7;
                 CREATE TABLE agent_projects (
                     project_id TEXT PRIMARY KEY,
                     folder_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                     objective TEXT NOT NULL DEFAULT '',
                     shared_checkout_consent_at TEXT NULL,
                     shared_checkout_consent_text TEXT NULL,
                     shared_checkout_trust INTEGER NOT NULL DEFAULT 0,
                     work_on_low_allowance INTEGER NOT NULL DEFAULT 0,
                     status INTEGER NOT NULL,
                     requested_handoff_reason INTEGER NULL,
                     attention_reason TEXT NULL,
                     updated_at TEXT NOT NULL);
                 CREATE TABLE agent_usage_observations (
                     project_id TEXT NOT NULL,
                     provider INTEGER NOT NULL,
                     observed_at TEXT NOT NULL,
                     PRIMARY KEY (project_id, provider));
                 CREATE TABLE agent_usage_observation_windows (
                     project_id TEXT NOT NULL,
                     provider INTEGER NOT NULL,
                     name TEXT NOT NULL,
                     used_percent REAL NOT NULL,
                     duration_ticks INTEGER NULL,
                     resets_at TEXT NULL,
                     PRIMARY KEY (project_id, provider, name));

                 INSERT INTO agent_projects VALUES
                     ('{older:D}', 'D:\one', '', NULL, NULL, 0, 0, 0, NULL, NULL, '2026-08-31T00:00:00.0000000+00:00'),
                     ('{newer:D}', 'D:\two', '', NULL, NULL, 0, 0, 0, NULL, NULL, '2026-08-31T00:00:00.0000000+00:00');

                 INSERT INTO agent_usage_observations VALUES
                     ('{older:D}', 1, '2026-08-31T10:00:00.0000000+00:00'),
                     ('{newer:D}', 1, '2026-08-31T12:00:00.0000000+00:00');

                 INSERT INTO agent_usage_observation_windows VALUES
                     ('{older:D}', 1, 'claude:five_hour', 10, NULL, NULL),
                     ('{newer:D}', 1, 'claude:five_hour', 65, NULL, NULL);
                 """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();

        using var store = new SqliteAgentProjectStore(_databasePath);
        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);

        Assert.IsNotNull(stored, "The reading survived the upgrade.");
        Assert.AreEqual(
            35,
            stored.MinimumRemainingPercent,
            "The newest of the two duplicate readings is the one kept.");
        Assert.AreEqual(
            DateTimeOffset.Parse("2026-08-31T12:00:00+00:00", CultureInfo.InvariantCulture),
            stored.ObservedAt);
    }

    [TestMethod]
    public async Task OneProjectsUsageReadingIsReadBackWithoutNamingAProject()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var reporting = ActiveState();
        await store.SaveAsync(reporting);

        // A five-hour window is spent by every session on the machine, so what one project's session
        // measured is what every other project must already know before it starts anything.
        Assert.IsTrue(await store.RecordUsageObservationAsync(
            reporting.Id,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), Now.AddHours(1))])));

        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);

        Assert.IsNotNull(stored);
        Assert.AreEqual(Now, stored.ObservedAt);
        Assert.AreEqual(70, stored.MinimumRemainingPercent);
    }

    [TestMethod]
    public async Task TwoProjectsShareOneReadingAndTheNewestWins()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var first = ActiveState();
        await store.SaveAsync(first);
        var secondFolder = Path.Combine(_directory, "second");
        Directory.CreateDirectory(secondFolder);
        var second = AgentProjectCoordinator.Create(secondFolder, "Other work.");
        await store.SaveAsync(second);

        Assert.IsTrue(await store.RecordUsageObservationAsync(
            first.Id,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), null)])));
        Assert.IsTrue(await store.RecordUsageObservationAsync(
            second.Id,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now.AddMinutes(1),
                [new AgentUsageWindow("claude:five_hour", 80, TimeSpan.FromHours(5), null)])));

        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);

        Assert.AreEqual(
            20,
            stored!.MinimumRemainingPercent,
            "There is one account, so there is one reading, and it is the newest one.");
    }

    [TestMethod]
    public async Task AnOlderReadingNeverReplacesANewerOne()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = ActiveState();
        await store.SaveAsync(project);

        Assert.IsTrue(await store.RecordUsageObservationAsync(
            project.Id,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), null)])));
        Assert.IsFalse(
            await store.RecordUsageObservationAsync(
                project.Id,
                new AgentUsageSnapshot(
                    AgentProvider.ClaudeCode,
                    Now.AddMinutes(-1),
                    [new AgentUsageWindow("claude:five_hour", 90, TimeSpan.FromHours(5), null)])),
            "An out-of-order helper must not overwrite a fresher reading.");

        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.AreEqual(70, stored!.MinimumRemainingPercent);
    }

    [TestMethod]
    public async Task EachProviderKeepsItsOwnAccountReading()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = ActiveState();
        await store.SaveAsync(project);

        await store.RecordUsageObservationAsync(
            project.Id,
            new AgentUsageSnapshot(
                AgentProvider.ClaudeCode,
                Now,
                [new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), null)]));
        await store.RecordUsageObservationAsync(
            project.Id,
            new AgentUsageSnapshot(
                AgentProvider.Codex,
                Now,
                [new AgentUsageWindow("codex:primary", 90, TimeSpan.FromHours(5), null)]));

        Assert.AreEqual(
            70,
            (await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode))!.MinimumRemainingPercent);
        Assert.AreEqual(
            10,
            (await store.ReadUsageObservationAsync(AgentProvider.Codex))!.MinimumRemainingPercent);
    }

    [TestMethod]
    public async Task ARecordedReadingNeedsAProjectThatReallyReportedIt()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            store.RecordUsageObservationAsync(
                Guid.NewGuid(),
                new AgentUsageSnapshot(
                    AgentProvider.ClaudeCode,
                    Now,
                    [new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), null)])));
    }

    [TestMethod]
    public async Task ProjectExistsFindsASavedProjectWithoutWritingToTheDatabase()
    {
        var state = ActiveState();
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(state);
        }

        SqliteConnection.ClearAllPools();
        var before = File.GetLastWriteTimeUtc(_databasePath);

        Assert.IsTrue(await SqliteAgentProjectStore.ProjectExistsAsync(_databasePath, state.Id));
        Assert.AreEqual(
            before,
            File.GetLastWriteTimeUtc(_databasePath),
            "The check must never write to a database it is only asking about.");
    }

    [TestMethod]
    public async Task ProjectExistsRefusesAProjectThatIsNotInTheDatabase()
    {
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            await store.SaveAsync(ActiveState());
        }

        SqliteConnection.ClearAllPools();

        Assert.IsFalse(await SqliteAgentProjectStore.ProjectExistsAsync(_databasePath, Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ProjectExistsRefusesAMissingDatabaseAndDoesNotCreateOne()
    {
        Assert.IsFalse(await SqliteAgentProjectStore.ProjectExistsAsync(_databasePath, Guid.NewGuid()));
        Assert.IsFalse(File.Exists(_databasePath), "Asking a question must not create the database.");
    }

    [TestMethod]
    public async Task ProjectExistsRefusesADatabaseWithNoCoordinationSchema()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_databasePath, string.Empty);

        Assert.IsFalse(await SqliteAgentProjectStore.ProjectExistsAsync(_databasePath, Guid.NewGuid()));
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

    [TestMethod]
    public async Task UsageObservationsRoundTripAndOnlyMoveForward()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        await store.SaveAsync(project);

        var first = new AgentUsageSnapshot(
            AgentProvider.ClaudeCode,
            Now,
            [new AgentUsageWindow("claude:five_hour", 23.5, TimeSpan.FromHours(5), Now.AddHours(2))]);
        Assert.IsTrue(await store.RecordUsageObservationAsync(project.Id, first));

        var stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.AreEqual(Now, stored.ObservedAt);
        Assert.HasCount(1, stored.Windows);
        Assert.AreEqual("claude:five_hour", stored.Windows[0].Name);
        Assert.AreEqual(23.5, stored.Windows[0].UsedPercent);
        Assert.AreEqual(TimeSpan.FromHours(5), stored.Windows[0].WindowDuration);
        Assert.AreEqual(Now.AddHours(2), stored.Windows[0].ResetsAt);
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.Codex));

        Assert.IsFalse(await store.RecordUsageObservationAsync(
            project.Id,
            first with { ObservedAt = Now.AddSeconds(-1) }));
        Assert.IsFalse(await store.RecordUsageObservationAsync(project.Id, first));

        var later = new AgentUsageSnapshot(
            AgentProvider.ClaudeCode,
            Now.AddMinutes(5),
            [
                new AgentUsageWindow("claude:five_hour", 30, TimeSpan.FromHours(5), null),
                new AgentUsageWindow("claude:seven_day", 44, TimeSpan.FromDays(7), null),
            ]);
        Assert.IsTrue(await store.RecordUsageObservationAsync(project.Id, later));
        stored = await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode);
        Assert.IsNotNull(stored);
        Assert.HasCount(2, stored.Windows);
        Assert.AreEqual(30, stored.Windows[0].UsedPercent);
    }

    [TestMethod]
    public async Task UsageObservationsRequireAKnownProjectAndValidWindows()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        await store.SaveAsync(project);
        var observation = new AgentUsageSnapshot(
            AgentProvider.ClaudeCode,
            Now,
            [new AgentUsageWindow("claude:five_hour", 10, TimeSpan.FromHours(5), null)]);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => store.RecordUsageObservationAsync(Guid.NewGuid(), observation));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.RecordUsageObservationAsync(
                project.Id,
                new AgentUsageSnapshot(AgentProvider.ClaudeCode, Now, [])));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.RecordUsageObservationAsync(
                project.Id,
                observation with
                {
                    Windows = [new AgentUsageWindow("claude:five_hour", 101, null, null)],
                }));
        Assert.IsNull(await store.ReadUsageObservationAsync(AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task AnEarlierSchemaDatabaseGainsNewTablesAndColumnsWithoutLosingState()
    {
        AgentProjectState project;
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            project = ActiveState();
            await store.SaveAsync(project);
        }

        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TABLE agent_usage_observation_windows;
                DROP TABLE agent_usage_observations;
                ALTER TABLE agent_projects DROP COLUMN objective;
                ALTER TABLE agent_projects DROP COLUMN shared_checkout_consent_at;
                ALTER TABLE agent_projects DROP COLUMN shared_checkout_consent_text;
                ALTER TABLE agent_projects DROP COLUMN shared_checkout_trust;
                ALTER TABLE agent_projects DROP COLUMN work_on_low_allowance;
                ALTER TABLE agent_participants DROP COLUMN preferred_model;
                ALTER TABLE agent_participants DROP COLUMN preferred_effort;
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        using (var migrated = new SqliteAgentProjectStore(_databasePath))
        {
            var loaded = await migrated.LoadAsync(project.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(project.Status, loaded.Status);
            Assert.IsNotNull(loaded.Lease);
            Assert.AreEqual(
                string.Empty,
                loaded.Objective,
                "A project stored before objectives existed has none, not a broken read.");
            Assert.IsNull(
                loaded.SharedCheckoutConsent,
                "A project stored before consent existed has not approved anything.");
            var described = await migrated.UpdateAsync(
                project.Id,
                current => AgentProjectCoordinator.SetObjective(current, "Finish the migration."));
            Assert.AreEqual("Finish the migration.", described.Objective);
            var approved = await migrated.UpdateAsync(
                project.Id,
                current => AgentProjectCoordinator.GrantSharedCheckoutConsent(current, Now, "Share this folder."));
            Assert.AreEqual(Now, approved.SharedCheckoutConsent?.GrantedAt);
            Assert.AreEqual("Share this folder.", approved.SharedCheckoutConsent?.ApprovalDescription);
            Assert.AreEqual(
                SharedFolderTrust.UseMyOwnSettings,
                approved.SharedCheckoutConsent?.Trust,
                "An approval recorded before Filekin asked how far it goes means the narrow answer.");
            Assert.IsFalse(
                loaded.WorkOnLowAllowance,
                "Waiving the safety limit is something the owner says, not something a migration decides.");
            Assert.IsNull(loaded.Participant(AgentProvider.Codex).PreferredModel);
            Assert.IsNull(loaded.Participant(AgentProvider.ClaudeCode).PreferredEffort);
            var carryingOn = await migrated.UpdateAsync(
                project.Id,
                current => AgentProjectCoordinator.SetWorkOnLowAllowance(current, allowed: true));
            Assert.IsTrue(carryingOn.WorkOnLowAllowance);
            Assert.IsTrue(await migrated.RecordUsageObservationAsync(
                project.Id,
                new AgentUsageSnapshot(
                    AgentProvider.ClaudeCode,
                    Now,
                    [new AgentUsageWindow("claude:five_hour", 12, TimeSpan.FromHours(5), null)])));
        }

        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";

            // The migration stamps the shared version only after every additive step succeeds.
            Assert.AreEqual(
                (long)StateDatabase.SchemaVersion,
                Convert.ToInt64(await command.ExecuteScalarAsync(), null));
        }
    }

    [TestMethod]
    public async Task TheModelChosenForEachAgentSurvivesARestart()
    {
        AgentProjectState project;
        using (var store = new SqliteAgentProjectStore(_databasePath))
        {
            project = ReadyState();
            await store.SaveAsync(project);
            await store.UpdateAsync(
                project.Id,
                current => AgentProjectCoordinator.ChooseModel(
                    AgentProjectCoordinator.ChooseModel(current, AgentProvider.ClaudeCode, "opus", "high"),
                    AgentProvider.Codex,
                    "gpt-5.6-sol"));
        }

        SqliteConnection.ClearAllPools();
        using var reopened = new SqliteAgentProjectStore(_databasePath);
        var loaded = await reopened.LoadAsync(project.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("opus", loaded.Participant(AgentProvider.ClaudeCode).PreferredModel);
        Assert.AreEqual("high", loaded.Participant(AgentProvider.ClaudeCode).PreferredEffort);
        Assert.AreEqual("gpt-5.6-sol", loaded.Participant(AgentProvider.Codex).PreferredModel);
        Assert.IsNull(loaded.Participant(AgentProvider.Codex).PreferredEffort);
    }

    private static AgentProjectState ActiveState() =>
        Coordinator().SelectInitialAgent(ReadyState(), Now);

    private static AgentProjectState ReadyState()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, ("five-hour", 10), ("weekly", 20)));
        return AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
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
        new(new AgentCoordinationPolicy(5, 25, TimeSpan.FromMinutes(5)));
}
