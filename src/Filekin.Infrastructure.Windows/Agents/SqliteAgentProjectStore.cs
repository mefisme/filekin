using System.Globalization;
using Filekin.Core;
using Filekin.Core.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>Transactional <c>state.db</c> storage for cooperative agent projects.</summary>
public sealed class SqliteAgentProjectStore : IAgentProjectStore, IDisposable
{
    private const int SchemaVersion = 1;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteAgentProjectStore()
        : this(DefaultDatabasePath)
    {
    }

    public SqliteAgentProjectStore(string databasePath)
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

    public async Task SaveAsync(
        AgentProjectState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await SaveAsync(connection, transaction, state, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<AgentProjectState?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return await LoadAsync(connection, transaction: null, projectId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<AgentProjectState?> LoadByFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(
                connection,
                transaction: null,
                "SELECT project_id FROM agent_projects WHERE folder_path = $folder COLLATE NOCASE;");
            command.Parameters.AddWithValue("$folder", fullPath);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string id
                ? await LoadAsync(connection, transaction: null, ParseGuid(id, "project id"), cancellationToken)
                    .ConfigureAwait(false)
                : null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentProjectState>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return await LoadAllAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<AgentProjectState> UpdateAsync(
        Guid projectId,
        Func<AgentProjectState, AgentProjectState> transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            // Take SQLite's writer reservation before reading. Without this, two MCP processes could
            // both read the same snapshot and the later save would silently discard the first change.
            await using (var lockCommand = CreateCommand(
                             connection,
                             transaction,
                             "UPDATE agent_projects SET updated_at = updated_at WHERE project_id = $id;"))
            {
                lockCommand.Parameters.AddWithValue("$id", projectId.ToString("D"));
                if (await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                {
                    throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
                }
            }

            var current = await LoadAsync(connection, transaction, projectId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("The locked agent project disappeared.");
            var updated = transition(current)
                ?? throw new InvalidOperationException("An agent project transition returned no state.");
            if (updated.Id != current.Id)
            {
                throw new InvalidOperationException("An agent project transition cannot change project identity.");
            }

            await SaveAsync(connection, transaction, updated, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentProjectState>> ReconcileAfterRestartAsync(
        CancellationToken cancellationToken = default)
    {
        var states = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var reconciled = new List<AgentProjectState>(states.Count);
        foreach (var state in states)
        {
            var updated = await UpdateAsync(
                    state.Id,
                    AgentProjectCoordinator.ReconcileAfterRestart,
                    cancellationToken)
                .ConfigureAwait(false);
            reconciled.Add(updated);
        }

        return reconciled;
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

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var versionValue = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var version = Convert.ToInt32(versionValue, CultureInfo.InvariantCulture);
            if (version > SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"state.db schema {version} is newer than this Filekin build supports.");
            }

            if (version == 0)
            {
                await using var schemaCommand = connection.CreateCommand();
                schemaCommand.CommandText = SchemaSql;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

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

    private static async Task SaveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentProjectState state,
        CancellationToken cancellationToken)
    {
        await UpsertProjectAsync(connection, transaction, state, cancellationToken).ConfigureAwait(false);
        await DeleteChildrenAsync(connection, transaction, state.Id, cancellationToken).ConfigureAwait(false);

        foreach (var participant in state.Participants.Values.OrderBy(value => value.Provider))
        {
            await InsertParticipantAsync(connection, transaction, state.Id, participant, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.Lease is not null)
        {
            await InsertLeaseAsync(connection, transaction, state.Id, state.Lease, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var message in state.Messages)
        {
            await InsertMessageAsync(connection, transaction, state.Id, message, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.PendingHandoff is not null)
        {
            await InsertHandoffAsync(
                    connection,
                    transaction,
                    state.Id,
                    "pending",
                    state.PendingHandoff,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.LastHandoff is not null)
        {
            await InsertHandoffAsync(
                    connection,
                    transaction,
                    state.Id,
                    "last",
                    state.LastHandoff,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task UpsertProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentProjectState state,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO agent_projects (
                project_id, folder_path, status, requested_handoff_reason, attention_reason, updated_at)
            VALUES ($id, $folder, $status, $reason, $attention, $updated)
            ON CONFLICT(project_id) DO UPDATE SET
                folder_path = excluded.folder_path,
                status = excluded.status,
                requested_handoff_reason = excluded.requested_handoff_reason,
                attention_reason = excluded.attention_reason,
                updated_at = excluded.updated_at;
            """);
        command.Parameters.AddWithValue("$id", state.Id.ToString("D"));
        command.Parameters.AddWithValue("$folder", state.FolderPath);
        command.Parameters.AddWithValue("$status", (int)state.Status);
        command.Parameters.AddWithValue(
            "$reason",
            state.RequestedHandoffReason is { } reason ? (int)reason : DBNull.Value);
        command.Parameters.AddWithValue("$attention", (object?)state.AttentionReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", FormatDateTime(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            DELETE FROM agent_usage_windows WHERE project_id = $id;
            DELETE FROM agent_participants WHERE project_id = $id;
            DELETE FROM agent_leases WHERE project_id = $id;
            DELETE FROM agent_messages WHERE project_id = $id;
            DELETE FROM agent_handoffs WHERE project_id = $id;
            """);
        command.Parameters.AddWithValue("$id", projectId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertParticipantAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        AgentParticipant participant,
        CancellationToken cancellationToken)
    {
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         INSERT INTO agent_participants (
                             project_id, provider, native_session_id, connection_state, turn_state,
                             usage_observed_at)
                         VALUES ($project, $provider, $session, $connection, $turn, $observed);
                         """))
        {
            command.Parameters.AddWithValue("$project", projectId.ToString("D"));
            command.Parameters.AddWithValue("$provider", (int)participant.Provider);
            command.Parameters.AddWithValue("$session", (object?)participant.NativeSessionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$connection", (int)participant.ConnectionState);
            command.Parameters.AddWithValue("$turn", (int)participant.TurnState);
            command.Parameters.AddWithValue(
                "$observed",
                participant.Usage is { } usage ? FormatDateTime(usage.ObservedAt) : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (participant.Usage is null)
        {
            return;
        }

        foreach (var window in participant.Usage.Windows)
        {
            await using var command = CreateCommand(
                connection,
                transaction,
                """
                INSERT INTO agent_usage_windows (
                    project_id, provider, name, used_percent, duration_ticks, resets_at)
                VALUES ($project, $provider, $name, $used, $duration, $reset);
                """);
            command.Parameters.AddWithValue("$project", projectId.ToString("D"));
            command.Parameters.AddWithValue("$provider", (int)participant.Provider);
            command.Parameters.AddWithValue("$name", window.Name);
            command.Parameters.AddWithValue("$used", window.UsedPercent);
            command.Parameters.AddWithValue(
                "$duration",
                window.WindowDuration is { } duration ? duration.Ticks : DBNull.Value);
            command.Parameters.AddWithValue(
                "$reset",
                window.ResetsAt is { } reset ? FormatDateTime(reset) : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        WorkingTreeLease lease,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO agent_leases (project_id, lease_id, owner, acquired_at)
            VALUES ($project, $lease, $owner, $acquired);
            """);
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        command.Parameters.AddWithValue("$lease", lease.Id.ToString("D"));
        command.Parameters.AddWithValue("$owner", (int)lease.Owner);
        command.Parameters.AddWithValue("$acquired", FormatDateTime(lease.AcquiredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        AgentMessage message,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO agent_messages (project_id, message_id, from_provider, to_provider, sent_at, text)
            VALUES ($project, $message, $from, $to, $sent, $text);
            """);
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        command.Parameters.AddWithValue("$message", message.Id.ToString("D"));
        command.Parameters.AddWithValue("$from", (int)message.From);
        command.Parameters.AddWithValue("$to", (int)message.To);
        command.Parameters.AddWithValue("$sent", FormatDateTime(message.SentAt));
        command.Parameters.AddWithValue("$text", message.Text);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertHandoffAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string slot,
        AgentHandoff handoff,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO agent_handoffs (
                project_id, slot, handoff_id, from_provider, to_provider, created_at, reason,
                summary, completed_work, remaining_work, verification, blockers, accepted_at)
            VALUES (
                $project, $slot, $handoff, $from, $to, $created, $reason,
                $summary, $completed, $remaining, $verification, $blockers, $accepted);
            """);
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        command.Parameters.AddWithValue("$slot", slot);
        command.Parameters.AddWithValue("$handoff", handoff.Id.ToString("D"));
        command.Parameters.AddWithValue("$from", (int)handoff.From);
        command.Parameters.AddWithValue("$to", (int)handoff.To);
        command.Parameters.AddWithValue("$created", FormatDateTime(handoff.CreatedAt));
        command.Parameters.AddWithValue("$reason", (int)handoff.Reason);
        command.Parameters.AddWithValue("$summary", handoff.Summary);
        command.Parameters.AddWithValue("$completed", handoff.CompletedWork);
        command.Parameters.AddWithValue("$remaining", handoff.RemainingWork);
        command.Parameters.AddWithValue("$verification", handoff.Verification);
        command.Parameters.AddWithValue("$blockers", handoff.Blockers);
        command.Parameters.AddWithValue(
            "$accepted",
            handoff.AcceptedAt is { } accepted ? FormatDateTime(accepted) : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<AgentProjectState>> LoadAllAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         "SELECT project_id FROM agent_projects ORDER BY folder_path COLLATE NOCASE;"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(ParseGuid(reader.GetString(0), "project id"));
            }
        }

        var states = new List<AgentProjectState>(ids.Count);
        foreach (var id in ids)
        {
            states.Add(await LoadAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("An enumerated agent project disappeared."));
        }

        return states;
    }

    private static async Task<AgentProjectState?> LoadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        string folderPath;
        AgentProjectStatus status;
        AgentHandoffReason? requestedReason;
        string? attentionReason;
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT folder_path, status, requested_handoff_reason, attention_reason
                         FROM agent_projects WHERE project_id = $id;
                         """))
        {
            command.Parameters.AddWithValue("$id", projectId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            folderPath = reader.GetString(0);
            status = ReadEnum<AgentProjectStatus>(reader.GetInt32(1), "project status");
            requestedReason = reader.IsDBNull(2)
                ? null
                : ReadEnum<AgentHandoffReason>(reader.GetInt32(2), "handoff reason");
            attentionReason = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        var participants = await LoadParticipantsAsync(connection, transaction, projectId, cancellationToken)
            .ConfigureAwait(false);
        var lease = await LoadLeaseAsync(connection, transaction, projectId, cancellationToken)
            .ConfigureAwait(false);
        var messages = await LoadMessagesAsync(connection, transaction, projectId, cancellationToken)
            .ConfigureAwait(false);
        var handoffs = await LoadHandoffsAsync(connection, transaction, projectId, cancellationToken)
            .ConfigureAwait(false);

        EnsureCompleteParticipantSet(participants);
        return new AgentProjectState(
            projectId,
            folderPath,
            status,
            participants,
            lease,
            requestedReason,
            handoffs.Pending,
            handoffs.Last,
            messages,
            attentionReason);
    }

    private static async Task<Dictionary<AgentProvider, AgentParticipant>> LoadParticipantsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(
            AgentProvider Provider,
            string? NativeSessionId,
            AgentConnectionState ConnectionState,
            AgentTurnState TurnState,
            DateTimeOffset? UsageObservedAt)>();
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT provider, native_session_id, connection_state, turn_state, usage_observed_at
                         FROM agent_participants WHERE project_id = $id ORDER BY provider;
                         """))
        {
            command.Parameters.AddWithValue("$id", projectId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((
                    ReadEnum<AgentProvider>(reader.GetInt32(0), "agent provider"),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    ReadEnum<AgentConnectionState>(reader.GetInt32(2), "connection state"),
                    ReadEnum<AgentTurnState>(reader.GetInt32(3), "turn state"),
                    reader.IsDBNull(4)
                        ? null
                        : ParseDateTime(reader.GetString(4), "usage observation")));
            }
        }

        var participants = new Dictionary<AgentProvider, AgentParticipant>();
        foreach (var row in rows)
        {
            var usage = row.UsageObservedAt is { } observedAt
                ? new AgentUsageSnapshot(
                    row.Provider,
                    observedAt,
                    await LoadUsageWindowsAsync(
                            connection,
                            transaction,
                            projectId,
                            row.Provider,
                            cancellationToken)
                        .ConfigureAwait(false))
                : null;
            participants.Add(
                row.Provider,
                new AgentParticipant(
                    row.Provider,
                    row.NativeSessionId,
                    row.ConnectionState,
                    row.TurnState,
                    usage));
        }

        return participants;
    }

    private static async Task<IReadOnlyList<AgentUsageWindow>> LoadUsageWindowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken)
    {
        var windows = new List<AgentUsageWindow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT name, used_percent, duration_ticks, resets_at
            FROM agent_usage_windows
            WHERE project_id = $project AND provider = $provider
            ORDER BY name;
            """);
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        command.Parameters.AddWithValue("$provider", (int)provider);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var usedPercent = reader.GetDouble(1);
            if (string.IsNullOrWhiteSpace(reader.GetString(0)) || usedPercent is < 0 or > 100)
            {
                throw new InvalidOperationException("state.db contains an invalid usage window.");
            }

            windows.Add(new AgentUsageWindow(
                reader.GetString(0),
                usedPercent,
                reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : ParseDateTime(reader.GetString(3), "usage reset")));
        }

        return windows;
    }

    private static async Task<WorkingTreeLease?> LoadLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            "SELECT lease_id, owner, acquired_at FROM agent_leases WHERE project_id = $id;");
        command.Parameters.AddWithValue("$id", projectId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new WorkingTreeLease(
                ParseGuid(reader.GetString(0), "lease id"),
                ReadEnum<AgentProvider>(reader.GetInt32(1), "lease owner"),
                ParseDateTime(reader.GetString(2), "lease acquisition"))
            : null;
    }

    private static async Task<IReadOnlyList<AgentMessage>> LoadMessagesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var messages = new List<AgentMessage>();
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT message_id, from_provider, to_provider, sent_at, text
            FROM agent_messages WHERE project_id = $id ORDER BY sent_at, message_id;
            """);
        command.Parameters.AddWithValue("$id", projectId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(new AgentMessage(
                ParseGuid(reader.GetString(0), "message id"),
                ReadEnum<AgentProvider>(reader.GetInt32(1), "message sender"),
                ReadEnum<AgentProvider>(reader.GetInt32(2), "message recipient"),
                ParseDateTime(reader.GetString(3), "message time"),
                reader.GetString(4)));
        }

        return messages;
    }

    private static async Task<(AgentHandoff? Pending, AgentHandoff? Last)> LoadHandoffsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        AgentHandoff? pending = null;
        AgentHandoff? last = null;
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT slot, handoff_id, from_provider, to_provider, created_at, reason, summary,
                   completed_work, remaining_work, verification, blockers, accepted_at
            FROM agent_handoffs WHERE project_id = $id;
            """);
        command.Parameters.AddWithValue("$id", projectId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var handoff = new AgentHandoff(
                ParseGuid(reader.GetString(1), "handoff id"),
                ReadEnum<AgentProvider>(reader.GetInt32(2), "handoff sender"),
                ReadEnum<AgentProvider>(reader.GetInt32(3), "handoff recipient"),
                ParseDateTime(reader.GetString(4), "handoff time"),
                ReadEnum<AgentHandoffReason>(reader.GetInt32(5), "handoff reason"),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11), "handoff acceptance"));
            switch (reader.GetString(0))
            {
                case "pending":
                    pending = handoff;
                    break;
                case "last":
                    last = handoff;
                    break;
                default:
                    throw new InvalidOperationException("state.db contains an unknown handoff slot.");
            }
        }

        return (pending, last);
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void EnsureCompleteParticipantSet(
        Dictionary<AgentProvider, AgentParticipant> participants)
    {
        if (participants.Count != 2 ||
            !participants.ContainsKey(AgentProvider.Codex) ||
            !participants.ContainsKey(AgentProvider.ClaudeCode))
        {
            throw new InvalidOperationException(
                "state.db does not contain the complete supported participant set.");
        }
    }

    private static TEnum ReadEnum<TEnum>(int value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new InvalidOperationException($"state.db contains an unknown {fieldName} value.");
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDateTime(string value, string fieldName) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new InvalidOperationException($"state.db contains an invalid {fieldName}.");

    private static Guid ParseGuid(string value, string fieldName) =>
        Guid.TryParseExact(value, "D", out var parsed)
            ? parsed
            : throw new InvalidOperationException($"state.db contains an invalid {fieldName}.");

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS agent_projects (
            project_id TEXT PRIMARY KEY,
            folder_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
            status INTEGER NOT NULL,
            requested_handoff_reason INTEGER NULL,
            attention_reason TEXT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS agent_participants (
            project_id TEXT NOT NULL REFERENCES agent_projects(project_id) ON DELETE CASCADE,
            provider INTEGER NOT NULL,
            native_session_id TEXT NULL,
            connection_state INTEGER NOT NULL,
            turn_state INTEGER NOT NULL,
            usage_observed_at TEXT NULL,
            PRIMARY KEY (project_id, provider)
        );

        CREATE TABLE IF NOT EXISTS agent_usage_windows (
            project_id TEXT NOT NULL,
            provider INTEGER NOT NULL,
            name TEXT NOT NULL,
            used_percent REAL NOT NULL,
            duration_ticks INTEGER NULL,
            resets_at TEXT NULL,
            PRIMARY KEY (project_id, provider, name),
            FOREIGN KEY (project_id, provider)
                REFERENCES agent_participants(project_id, provider) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS agent_leases (
            project_id TEXT PRIMARY KEY REFERENCES agent_projects(project_id) ON DELETE CASCADE,
            lease_id TEXT NOT NULL UNIQUE,
            owner INTEGER NOT NULL,
            acquired_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS agent_messages (
            project_id TEXT NOT NULL REFERENCES agent_projects(project_id) ON DELETE CASCADE,
            message_id TEXT NOT NULL,
            from_provider INTEGER NOT NULL,
            to_provider INTEGER NOT NULL,
            sent_at TEXT NOT NULL,
            text TEXT NOT NULL,
            PRIMARY KEY (project_id, message_id)
        );

        CREATE TABLE IF NOT EXISTS agent_handoffs (
            project_id TEXT NOT NULL REFERENCES agent_projects(project_id) ON DELETE CASCADE,
            slot TEXT NOT NULL CHECK (slot IN ('pending', 'last')),
            handoff_id TEXT NOT NULL,
            from_provider INTEGER NOT NULL,
            to_provider INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            reason INTEGER NOT NULL,
            summary TEXT NOT NULL,
            completed_work TEXT NOT NULL,
            remaining_work TEXT NOT NULL,
            verification TEXT NOT NULL,
            blockers TEXT NOT NULL,
            accepted_at TEXT NULL,
            PRIMARY KEY (project_id, slot)
        );

        PRAGMA user_version = 1;
        """;
}
