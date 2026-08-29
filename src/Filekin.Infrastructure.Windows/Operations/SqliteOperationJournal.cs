using System.Globalization;
using Filekin.Core;
using Filekin.Core.Operations;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Operations;

/// <summary>Transactional persistent operation history in Filekin's shared <c>state.db</c>.</summary>
public sealed class SqliteOperationJournal : IOperationJournal, IDisposable
{
    private const int CompatibleStateDatabaseVersion = 1;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteOperationJournal()
        : this(DefaultDatabasePath)
    {
    }

    public SqliteOperationJournal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException("The state database path must be fully qualified.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 10,
        }.ToString();
    }

    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductIdentity.Name,
        "state.db");

    public string DatabasePath { get; }

    private string ConnectionString { get; }

    public async Task RecordAsync(
        JournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (var insert = CreateCommand(
                             connection,
                             transaction,
                             """
                             INSERT INTO operation_journal (
                                 operation_id,
                                 performed_at,
                                 kind,
                                 summary,
                                 payload_json,
                                 undo_state,
                                 undo_status_detail)
                             VALUES ($id, $performedAt, $kind, $summary, $payload, $undoState, $undoDetail);
                             """))
            {
                AddEntryParameters(insert, entry);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var prune = CreateCommand(
                             connection,
                             transaction,
                             """
                             DELETE FROM operation_journal
                             WHERE sequence NOT IN (
                                 SELECT sequence
                                 FROM operation_journal
                                 ORDER BY sequence DESC
                                 LIMIT $retained);
                             """))
            {
                prune.Parameters.AddWithValue("$retained", OperationJournalPolicy.RetainedOperations);
                await prune.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<JournalEntry?> MostRecentUndoCandidateAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                {SelectColumns}
                WHERE undo_state IN ($undoable, $failed, $partial)
                ORDER BY sequence DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$undoable", (int)OperationUndoState.Undoable);
            command.Parameters.AddWithValue("$failed", (int)OperationUndoState.UndoFailed);
            command.Parameters.AddWithValue("$partial", (int)OperationUndoState.PartiallyUndone);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadEntry(reader)
                : null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task TransitionUndoAsync(
        Guid id,
        OperationUndoState state,
        string? statusDetail = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (var reserveWriter = CreateCommand(
                             connection,
                             transaction,
                             """
                             UPDATE operation_journal
                             SET operation_id = operation_id
                             WHERE operation_id = $id;
                             """))
            {
                reserveWriter.Parameters.AddWithValue("$id", id.ToString("D"));
                if (await reserveWriter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                {
                    throw new KeyNotFoundException($"Operation journal entry '{id:D}' does not exist.");
                }
            }

            var current = await LoadAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The locked operation journal entry disappeared.");
            var updated = current.TransitionUndo(state, statusDetail);

            await using (var update = CreateCommand(
                             connection,
                             transaction,
                             """
                             UPDATE operation_journal
                             SET undo_state = $undoState,
                                 undo_status_detail = $undoDetail
                             WHERE operation_id = $id;
                             """))
            {
                update.Parameters.AddWithValue("$undoState", (int)updated.UndoState);
                update.Parameters.AddWithValue(
                    "$undoDetail",
                    (object?)updated.UndoStatusDetail ?? DBNull.Value);
                update.Parameters.AddWithValue("$id", id.ToString("D"));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new InvalidOperationException("The operation journal transition was not persisted.");
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<JournalEntry>> RecentAsync(
        int count = OperationJournalPolicy.RetainedOperations,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                {SelectColumns}
                ORDER BY sequence DESC
                LIMIT $count;
                """;
            command.Parameters.AddWithValue("$count", count);

            var entries = new List<JournalEntry>(Math.Min(count, OperationJournalPolicy.RetainedOperations));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReconcileAfterRestartAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = CreateCommand(
                connection,
                transaction,
                """
                UPDATE operation_journal
                SET undo_state = $unavailable,
                    undo_status_detail = CASE
                        WHEN undo_status_detail IS NULL OR trim(undo_status_detail) = '' THEN $reason
                        ELSE undo_status_detail
                    END
                WHERE undo_state IN ($undoable, $failed, $partial);
                """);
            command.Parameters.AddWithValue("$unavailable", (int)OperationUndoState.Unavailable);
            command.Parameters.AddWithValue(
                "$reason",
                OperationJournalPolicy.PreviousSessionUndoUnavailableDetail);
            command.Parameters.AddWithValue("$undoable", (int)OperationUndoState.Undoable);
            command.Parameters.AddWithValue("$failed", (int)OperationUndoState.UndoFailed);
            command.Parameters.AddWithValue("$partial", (int)OperationUndoState.PartiallyUndone);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializationGate.Dispose();
        _operationGate.Dispose();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);

            await using (var version = connection.CreateCommand())
            {
                version.CommandText = "PRAGMA user_version;";
                var value = await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (number > CompatibleStateDatabaseVersion)
                {
                    throw new InvalidOperationException(
                        $"state.db schema {number} is newer than this Filekin build supports.");
                }
            }

            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var schema = CreateCommand(connection, transaction, SchemaSql);
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000; PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JournalEntry?> LoadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            $"""
            {SelectColumns}
            WHERE operation_id = $id;
            """);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEntry(reader)
            : null;
    }

    private static JournalEntry ReadEntry(SqliteDataReader reader)
    {
        var idText = reader.GetString(0);
        if (!Guid.TryParseExact(idText, "D", out var id))
        {
            throw new InvalidOperationException("state.db contains an invalid operation id.");
        }

        var performedAtText = reader.GetString(1);
        if (!DateTimeOffset.TryParseExact(
                performedAtText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var performedAt))
        {
            throw new InvalidOperationException("state.db contains an invalid operation timestamp.");
        }

        var undoStateValue = reader.GetInt32(5);
        if (!Enum.IsDefined((OperationUndoState)undoStateValue))
        {
            throw new InvalidOperationException("state.db contains an invalid operation Undo state.");
        }

        return new JournalEntry(
            id,
            performedAt,
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            (OperationUndoState)undoStateValue,
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void AddEntryParameters(SqliteCommand command, JournalEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$performedAt",
            entry.PerformedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$summary", entry.Summary);
        command.Parameters.AddWithValue("$payload", entry.PayloadJson);
        command.Parameters.AddWithValue("$undoState", (int)entry.UndoState);
        command.Parameters.AddWithValue("$undoDetail", (object?)entry.UndoStatusDetail ?? DBNull.Value);
    }

    private const string SelectColumns =
        """
        SELECT operation_id,
               performed_at,
               kind,
               summary,
               payload_json,
               undo_state,
               undo_status_detail
        FROM operation_journal
        """;

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS operation_journal (
            sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            operation_id TEXT NOT NULL UNIQUE,
            performed_at TEXT NOT NULL,
            kind TEXT NOT NULL,
            summary TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            undo_state INTEGER NOT NULL CHECK (undo_state BETWEEN 0 AND 5),
            undo_status_detail TEXT NULL
        );
        """;
}
