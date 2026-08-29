using Filekin.Core.Agents;
using Filekin.Core.Operations;
using Filekin.Infrastructure.Windows.Agents;
using Filekin.Infrastructure.Windows.Operations;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Tests.Operations;

[TestClass]
public sealed class SqliteOperationJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);
    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-operation-journal-{Guid.NewGuid():N}");
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
    public async Task EntriesPersistNewestFirstAcrossStoreInstances()
    {
        var first = Entry(1, OperationUndoState.Undoable);
        var second = Entry(2, OperationUndoState.NotUndoable);
        var third = Entry(3, OperationUndoState.Undoable);
        using (var writer = new SqliteOperationJournal(_databasePath))
        {
            await writer.RecordAsync(first);
            await writer.RecordAsync(second);
            await writer.RecordAsync(third);
        }

        using var reader = new SqliteOperationJournal(_databasePath);
        var recent = await reader.RecentAsync(2);

        CollectionAssert.AreEqual(new[] { third, second }, recent.ToArray());
        Assert.AreEqual(third, await reader.MostRecentUndoCandidateAsync());
        Assert.AreEqual(first, await reader.FindAsync(first.Id));
        Assert.AreEqual(second, await reader.FindAsync(second.Id));
        Assert.IsNull(await reader.FindAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task RecordingPrunesTransactionallyToTheNewestFiftyEntries()
    {
        var entries = Enumerable.Range(0, OperationJournalPolicy.RetainedOperations + 3)
            .Select(index => Entry(index, OperationUndoState.Undoable))
            .ToArray();
        using (var writer = new SqliteOperationJournal(_databasePath))
        {
            foreach (var entry in entries)
            {
                await writer.RecordAsync(entry);
            }
        }

        using var reader = new SqliteOperationJournal(_databasePath);
        var recent = await reader.RecentAsync();

        Assert.HasCount(OperationJournalPolicy.RetainedOperations, recent);
        Assert.AreEqual(entries[^1], recent[0]);
        Assert.AreEqual(entries[3], recent[^1]);
    }

    [TestMethod]
    public async Task RestartDemotesEveryCandidateAndPreservesOutcomeDetails()
    {
        var available = Entry(1, OperationUndoState.Undoable);
        var failed = Entry(2, OperationUndoState.UndoFailed) with
        {
            UndoStatusDetail = "The destination was locked.",
        };
        var partial = Entry(3, OperationUndoState.PartiallyUndone) with
        {
            UndoStatusDetail = "Restored 2 of 3 files; one was skipped.",
        };
        var informational = Entry(4, OperationUndoState.NotUndoable);
        using (var writer = new SqliteOperationJournal(_databasePath))
        {
            await writer.RecordAsync(available);
            await writer.RecordAsync(failed);
            await writer.RecordAsync(partial);
            await writer.RecordAsync(informational);
        }

        using (var restarting = new SqliteOperationJournal(_databasePath))
        {
            await restarting.ReconcileAfterRestartAsync();
        }

        using var reader = new SqliteOperationJournal(_databasePath);
        var recent = await reader.RecentAsync();
        Assert.IsNull(await reader.MostRecentUndoCandidateAsync());
        Assert.AreEqual(OperationUndoState.NotUndoable, recent[0].UndoState);
        Assert.AreEqual(OperationUndoState.Unavailable, recent[1].UndoState);
        Assert.AreEqual("Restored 2 of 3 files; one was skipped.", recent[1].UndoStatusDetail);
        Assert.AreEqual(OperationUndoState.Unavailable, recent[2].UndoState);
        Assert.AreEqual("The destination was locked.", recent[2].UndoStatusDetail);
        Assert.AreEqual(OperationUndoState.Unavailable, recent[3].UndoState);
        Assert.AreEqual(
            OperationJournalPolicy.PreviousSessionUndoUnavailableDetail,
            recent[3].UndoStatusDetail);
    }

    [TestMethod]
    public async Task UndoTransitionsPersistAndFailClosed()
    {
        var entry = Entry(1, OperationUndoState.Undoable);
        using (var writer = new SqliteOperationJournal(_databasePath))
        {
            await writer.RecordAsync(entry);
            await writer.TransitionUndoAsync(
                entry.Id,
                OperationUndoState.UndoFailed,
                "The destination was locked.");
        }

        using (var retry = new SqliteOperationJournal(_databasePath))
        {
            Assert.AreEqual(
                OperationUndoState.UndoFailed,
                (await retry.MostRecentUndoCandidateAsync())?.UndoState);
            await retry.TransitionUndoAsync(
                entry.Id,
                OperationUndoState.Undone,
                "Restored the original location.");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => retry.TransitionUndoAsync(
                    entry.Id,
                    OperationUndoState.UndoFailed,
                    "A terminal entry cannot be retried."));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => retry.TransitionUndoAsync(Guid.NewGuid(), OperationUndoState.Undone));
        }

        using var reader = new SqliteOperationJournal(_databasePath);
        var persisted = (await reader.RecentAsync()).Single();
        Assert.AreEqual(OperationUndoState.Undone, persisted.UndoState);
        Assert.AreEqual("Restored the original location.", persisted.UndoStatusDetail);
    }

    [TestMethod]
    public async Task ConcurrentStoreInstancesSerializeEveryWrite()
    {
        using (var initializer = new SqliteOperationJournal(_databasePath))
        {
            _ = await initializer.RecentAsync();
        }

        var entries = Enumerable.Range(0, 12)
            .Select(index => Entry(index, OperationUndoState.NotUndoable))
            .ToArray();
        await Task.WhenAll(entries.Select(async entry =>
        {
            using var store = new SqliteOperationJournal(_databasePath);
            await store.RecordAsync(entry);
        }));

        using var reader = new SqliteOperationJournal(_databasePath);
        var persisted = await reader.RecentAsync();
        Assert.HasCount(entries.Length, persisted);
        CollectionAssert.AreEquivalent(
            entries.Select(entry => entry.Id).ToArray(),
            persisted.Select(entry => entry.Id).ToArray());
    }

    [TestMethod]
    public async Task AgentStoreCanInitializeBeforeOperationHistory()
    {
        var project = AgentProjectCoordinator.Create(_directory);
        using (var agentStore = new SqliteAgentProjectStore(_databasePath))
        {
            await agentStore.SaveAsync(project);
        }

        var entry = Entry(1, OperationUndoState.NotUndoable);
        using (var journal = new SqliteOperationJournal(_databasePath))
        {
            await journal.RecordAsync(entry);
        }

        using var agentReader = new SqliteAgentProjectStore(_databasePath);
        Assert.IsNotNull(await agentReader.LoadAsync(project.Id));
    }

    [TestMethod]
    public async Task OperationHistoryCanInitializeBeforeAgentStore()
    {
        var entry = Entry(1, OperationUndoState.NotUndoable);
        using (var journal = new SqliteOperationJournal(_databasePath))
        {
            await journal.RecordAsync(entry);
        }

        var project = AgentProjectCoordinator.Create(_directory);
        using (var agentStore = new SqliteAgentProjectStore(_databasePath))
        {
            await agentStore.SaveAsync(project);
        }

        using var historyReader = new SqliteOperationJournal(_databasePath);
        Assert.AreEqual(entry, (await historyReader.RecentAsync()).Single());
    }

    [TestMethod]
    public async Task NewerSharedSchemaFailsWithoutAddingTheHistoryTable()
    {
        Directory.CreateDirectory(_directory);
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            await command.ExecuteNonQueryAsync();
        }

        using var store = new SqliteOperationJournal(_databasePath);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RecentAsync());
        StringAssert.Contains(exception.Message, "newer than this Filekin build");

        await using var verification = new SqliteConnection($"Data Source={_databasePath}");
        await verification.OpenAsync();
        await using var table = verification.CreateCommand();
        table.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'operation_journal';";
        Assert.AreEqual(0L, await table.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task CorruptDatabaseFailsWithoutReplacingIt()
    {
        Directory.CreateDirectory(_directory);
        const string corruptContent = "This is not a SQLite database.";
        await File.WriteAllTextAsync(_databasePath, corruptContent);

        using (var store = new SqliteOperationJournal(_databasePath))
        {
            _ = await Assert.ThrowsAsync<SqliteException>(() => store.RecentAsync());
        }

        SqliteConnection.ClearAllPools();
        Assert.AreEqual(corruptContent, await File.ReadAllTextAsync(_databasePath));
    }

    [TestMethod]
    public async Task UnavailableDatabaseDirectoryFailsHonestly()
    {
        Directory.CreateDirectory(_directory);
        var blockingFile = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, string.Empty);
        var unavailablePath = Path.Combine(blockingFile, "state.db");

        using var store = new SqliteOperationJournal(unavailablePath);
        _ = await Assert.ThrowsAsync<IOException>(() => store.RecentAsync());
    }

    [TestMethod]
    public void DefaultPathMatchesTheSharedFilekinStateDatabase()
    {
        Assert.AreEqual(SqliteAgentProjectStore.DefaultDatabasePath, SqliteOperationJournal.DefaultDatabasePath);
    }

    private static JournalEntry Entry(int index, OperationUndoState undoState) =>
        new(
            Guid.NewGuid(),
            Now.AddMinutes(index),
            $"kind-{index}",
            $"Operation {index}",
            $"{{\"index\":{index}}}",
            undoState);
}
