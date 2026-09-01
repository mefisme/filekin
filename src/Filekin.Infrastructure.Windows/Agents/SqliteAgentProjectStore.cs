using System.Globalization;
using Filekin.Core;
using Filekin.Core.Agents;
using Microsoft.Data.Sqlite;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>Transactional <c>state.db</c> storage for cooperative agent projects.</summary>
public sealed class SqliteAgentProjectStore : IAgentProjectStore, IAgentUsageObservationStore, IDisposable
{
    private const int SchemaVersion = StateDatabase.SchemaVersion;
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

    /// <summary>
    /// Answers whether one project is present in a state database, without creating the file,
    /// running a migration, or writing anything at all.
    /// </summary>
    /// <remarks>
    /// A companion process is pinned to one project for its whole life, and it can outlive that
    /// project: an agent session that is still running after Filekin removed or reset the project
    /// keeps relaunching its companion against whatever <c>state.db</c> is there now. Opening that
    /// database read-write would make a stale writer out of it, which is exactly the risk this check
    /// exists to remove, so the check itself is read-only and fails closed.
    /// </remarks>
    public static async Task<bool> ProjectExistsAsync(
        string databasePath,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(databasePath))
        {
            return false;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 10,
        }.ToString();

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM agent_projects WHERE project_id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", projectId.ToString("D"));
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (SqliteException)
        {
            // No coordination schema yet, or a database this process cannot read. Neither is proof
            // that the project is here, and guessing would attach the very writer this prevents.
            return false;
        }
    }

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

    public async Task<bool> RecordUsageObservationAsync(
        Guid reportingProjectId,
        AgentUsageSnapshot observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Windows.Count == 0)
        {
            throw new ArgumentException(
                "An unknown quota observation is never stored; missing data must stay unknown.",
                nameof(observation));
        }

        foreach (var window in observation.Windows)
        {
            if (string.IsNullOrWhiteSpace(window.Name) || window.UsedPercent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observation),
                    "Usage windows must be named and between 0 and 100 percent.");
            }
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            // Take the writer reservation before reading, for the same reason UpdateAsync does: two
            // helper processes must not both read the stored observation and then both write. The
            // reporting project is also proved to exist here, so a companion that outlived its
            // project cannot write a reading nobody asked for.
            await using (var lockCommand = CreateCommand(
                             connection,
                             transaction,
                             "UPDATE agent_projects SET updated_at = updated_at WHERE project_id = $id;"))
            {
                lockCommand.Parameters.AddWithValue("$id", reportingProjectId.ToString("D"));
                if (await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                {
                    throw new KeyNotFoundException($"Agent project '{reportingProjectId:D}' does not exist.");
                }
            }

            var stored = await LoadUsageObservationAsync(
                    connection,
                    transaction,
                    observation.Provider,
                    cancellationToken)
                .ConfigureAwait(false);
            if (stored is not null && stored.ObservedAt >= observation.ObservedAt)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using (var replaceCommand = CreateCommand(
                             connection,
                             transaction,
                             """
                             DELETE FROM agent_usage_observation_windows WHERE provider = $provider;
                             DELETE FROM agent_usage_observations WHERE provider = $provider;
                             INSERT INTO agent_usage_observations (
                                 provider, observed_at, reported_by_project_id)
                             VALUES ($provider, $observed, $project);
                             """))
            {
                replaceCommand.Parameters.AddWithValue("$project", reportingProjectId.ToString("D"));
                replaceCommand.Parameters.AddWithValue("$provider", (int)observation.Provider);
                replaceCommand.Parameters.AddWithValue("$observed", FormatDateTime(observation.ObservedAt));
                await replaceCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var window in observation.Windows)
            {
                await using var windowCommand = CreateCommand(
                    connection,
                    transaction,
                    """
                    INSERT INTO agent_usage_observation_windows (
                        provider, name, used_percent, duration_ticks, resets_at)
                    VALUES ($provider, $name, $used, $duration, $reset);
                    """);
                windowCommand.Parameters.AddWithValue("$provider", (int)observation.Provider);
                windowCommand.Parameters.AddWithValue("$name", window.Name);
                windowCommand.Parameters.AddWithValue("$used", window.UsedPercent);
                windowCommand.Parameters.AddWithValue(
                    "$duration",
                    window.WindowDuration is { } duration ? duration.Ticks : DBNull.Value);
                windowCommand.Parameters.AddWithValue(
                    "$reset",
                    window.ResetsAt is { } reset ? FormatDateTime(reset) : DBNull.Value);
                await windowCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<AgentUsageSnapshot?> ReadUsageObservationAsync(
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return await LoadUsageObservationAsync(
                    connection,
                    transaction: null,
                    provider,
                    cancellationToken)
                .ConfigureAwait(false);
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

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var versionValue = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var version = Convert.ToInt32(versionValue, CultureInfo.InvariantCulture);
            if (version > SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"state.db schema {version} is newer than this Filekin build supports.");
            }

            // The CREATE ... IF NOT EXISTS script both creates a new database and adds tables an older
            // one lacks. It cannot add a column to a table that already exists, so every change to an
            // existing table needs its own explicit step after it, written to be safe on a database
            // that the script just created with the column already present.
            if (version < SchemaVersion)
            {
                await using (var schemaCommand = connection.CreateCommand())
                {
                    schemaCommand.CommandText = SchemaSql;
                    await schemaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await AddMissingColumnAsync(
                        connection,
                        "agent_projects",
                        "objective",
                        "TEXT NOT NULL DEFAULT ''",
                        cancellationToken)
                    .ConfigureAwait(false);
                await AddMissingColumnAsync(
                        connection,
                        "agent_projects",
                        "shared_checkout_consent_at",
                        "TEXT NULL",
                        cancellationToken)
                    .ConfigureAwait(false);
                await AddMissingColumnAsync(
                        connection,
                        "agent_projects",
                        "shared_checkout_consent_text",
                        "TEXT NULL",
                        cancellationToken)
                    .ConfigureAwait(false);

                // An approval recorded before Filekin asked how far it goes means the narrow answer:
                // use the owner's own tool settings. Widening it needs them to say so.
                await AddMissingColumnAsync(
                        connection,
                        "agent_projects",
                        "shared_checkout_trust",
                        "INTEGER NOT NULL DEFAULT 0",
                        cancellationToken)
                    .ConfigureAwait(false);

                // A project recorded before this choice existed keeps the safety threshold. Waiving it
                // is something the owner says, never something a migration decides for them.
                await AddMissingColumnAsync(
                        connection,
                        "agent_projects",
                        "work_on_low_allowance",
                        "INTEGER NOT NULL DEFAULT 0",
                        cancellationToken)
                    .ConfigureAwait(false);

                // No model recorded means the tool's own choice, which is what every project had
                // before a person could pick one.
                await AddMissingColumnAsync(
                        connection,
                        "agent_participants",
                        "preferred_model",
                        "TEXT NULL",
                        cancellationToken)
                    .ConfigureAwait(false);
                await AddMissingColumnAsync(
                        connection,
                        "agent_participants",
                        "preferred_effort",
                        "TEXT NULL",
                        cancellationToken)
                    .ConfigureAwait(false);

                // The CREATE script cannot reshape a table that already exists, so moving usage from
                // per project to per account needs its own rebuild. It is safe to run on a database
                // the script just created, because it does nothing unless the old shape is there.
                await MigrateUsageObservationsToAccountScopeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);

                // Stamp the version last. If a process exits between additive migration steps, the
                // older version remains and the next opener safely retries instead of trusting an
                // incomplete schema merely because the CREATE script ran first.
                await using var stampVersion = connection.CreateCommand();
                stampVersion.CommandText = $"PRAGMA user_version = {SchemaVersion};";
                await stampVersion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    /// <summary>
    /// Adds one column to an existing table when it is missing. A database the schema script just
    /// created already has it, so this is a no-op there rather than an error.
    /// </summary>
    /// <summary>
    /// Moves usage observations from one row per project to one row per provider.
    /// </summary>
    /// <remarks>
    /// The old shape kept a separate copy of the same account fact for every folder, so a new project
    /// started blind about an account measured minutes earlier, and two projects could disagree about
    /// one account. The newest reading per provider is kept; the older duplicates described the same
    /// account and are dropped. Foreign keys are off for the rebuild, which is the documented way to
    /// exchange a table, and the whole exchange is one transaction.
    /// </remarks>
    private static async Task MigrateUsageObservationsToAccountScopeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasColumnAsync(connection, "agent_usage_observations", "project_id", cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await using (var pragmaOff = connection.CreateCommand())
        {
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF;";
            await pragmaOff.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var rebuild = connection.CreateCommand())
            {
                rebuild.Transaction = transaction;
                rebuild.CommandText =
                    """
                    CREATE TABLE agent_usage_observations_account (
                        provider INTEGER NOT NULL PRIMARY KEY,
                        observed_at TEXT NOT NULL,
                        reported_by_project_id TEXT NULL
                    );

                    CREATE TABLE agent_usage_observation_windows_account (
                        provider INTEGER NOT NULL,
                        name TEXT NOT NULL,
                        used_percent REAL NOT NULL,
                        duration_ticks INTEGER NULL,
                        resets_at TEXT NULL,
                        PRIMARY KEY (provider, name),
                        FOREIGN KEY (provider)
                            REFERENCES agent_usage_observations_account(provider) ON DELETE CASCADE
                    );

                    -- Timestamps are round-trip UTC, so the newest reading is also the largest text.
                    -- With MAX(), SQLite takes the remaining columns from that same winning row.
                    INSERT INTO agent_usage_observations_account (
                        provider, observed_at, reported_by_project_id)
                    SELECT provider, MAX(observed_at), project_id
                    FROM agent_usage_observations
                    GROUP BY provider;

                    INSERT OR IGNORE INTO agent_usage_observation_windows_account (
                        provider, name, used_percent, duration_ticks, resets_at)
                    SELECT w.provider, w.name, w.used_percent, w.duration_ticks, w.resets_at
                    FROM agent_usage_observation_windows w
                    JOIN agent_usage_observations_account a
                        ON a.provider = w.provider
                       AND a.reported_by_project_id = w.project_id;

                    DROP TABLE agent_usage_observation_windows;
                    DROP TABLE agent_usage_observations;
                    ALTER TABLE agent_usage_observations_account
                        RENAME TO agent_usage_observations;
                    ALTER TABLE agent_usage_observation_windows_account
                        RENAME TO agent_usage_observation_windows;
                    """;
                await rebuild.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await using var pragmaOn = connection.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON;";
            await pragmaOn.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task AddMissingColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
                project_id, folder_path, objective, shared_checkout_consent_at,
                shared_checkout_consent_text, shared_checkout_trust, work_on_low_allowance, status,
                requested_handoff_reason, attention_reason, updated_at)
            VALUES ($id, $folder, $objective, $consentAt, $consentText, $trust, $lowAllowance, $status,
                $reason, $attention, $updated)
            ON CONFLICT(project_id) DO UPDATE SET
                folder_path = excluded.folder_path,
                objective = excluded.objective,
                shared_checkout_consent_at = excluded.shared_checkout_consent_at,
                shared_checkout_consent_text = excluded.shared_checkout_consent_text,
                shared_checkout_trust = excluded.shared_checkout_trust,
                work_on_low_allowance = excluded.work_on_low_allowance,
                status = excluded.status,
                requested_handoff_reason = excluded.requested_handoff_reason,
                attention_reason = excluded.attention_reason,
                updated_at = excluded.updated_at;
            """);
        command.Parameters.AddWithValue("$id", state.Id.ToString("D"));
        command.Parameters.AddWithValue("$folder", state.FolderPath);
        command.Parameters.AddWithValue("$objective", state.Objective);
        command.Parameters.AddWithValue(
            "$consentAt",
            state.SharedCheckoutConsent is { } grantedConsent
                ? FormatDateTime(grantedConsent.GrantedAt)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$consentText",
            (object?)state.SharedCheckoutConsent?.ApprovalDescription ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$trust",
            (int)(state.SharedCheckoutConsent?.Trust ?? SharedFolderTrust.UseMyOwnSettings));
        command.Parameters.AddWithValue("$lowAllowance", state.WorkOnLowAllowance ? 1 : 0);
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
                             usage_observed_at, preferred_model, preferred_effort)
                         VALUES ($project, $provider, $session, $connection, $turn, $observed, $model,
                                 $effort);
                         """))
        {
            command.Parameters.AddWithValue("$project", projectId.ToString("D"));
            command.Parameters.AddWithValue("$provider", (int)participant.Provider);
            command.Parameters.AddWithValue("$session", (object?)participant.NativeSessionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$connection", (int)participant.ConnectionState);
            command.Parameters.AddWithValue("$turn", (int)participant.TurnState);
            command.Parameters.AddWithValue("$model", (object?)participant.PreferredModel ?? DBNull.Value);
            command.Parameters.AddWithValue("$effort", (object?)participant.PreferredEffort ?? DBNull.Value);
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
        string objective;
        SharedCheckoutConsent? sharedCheckoutConsent;
        bool workOnLowAllowance;
        AgentProjectStatus status;
        AgentHandoffReason? requestedReason;
        string? attentionReason;
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT folder_path, objective, shared_checkout_consent_at,
                                shared_checkout_consent_text, shared_checkout_trust,
                                work_on_low_allowance, status, requested_handoff_reason,
                                attention_reason
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
            objective = reader.GetString(1);

            // Both consent columns are written together, so a row holding only one of them is damaged
            // rather than merely old, and a half-recorded approval must never count as an approval.
            sharedCheckoutConsent = reader.IsDBNull(2) && reader.IsDBNull(3)
                ? null
                : reader.IsDBNull(2) || reader.IsDBNull(3)
                    ? throw new InvalidOperationException(
                        "state.db contains an incomplete shared checkout consent.")
                    : new SharedCheckoutConsent(
                        ParseDateTime(reader.GetString(2), "shared checkout consent time"),
                        reader.GetString(3),
                        ReadEnum<SharedFolderTrust>(reader.GetInt32(4), "shared folder trust"));
            workOnLowAllowance = reader.GetInt32(5) != 0;
            status = ReadEnum<AgentProjectStatus>(reader.GetInt32(6), "project status");
            requestedReason = reader.IsDBNull(7)
                ? null
                : ReadEnum<AgentHandoffReason>(reader.GetInt32(7), "handoff reason");
            attentionReason = reader.IsDBNull(8) ? null : reader.GetString(8);
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
            objective,
            sharedCheckoutConsent,
            workOnLowAllowance,
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
            DateTimeOffset? UsageObservedAt,
            string? PreferredModel,
            string? PreferredEffort)>();
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT provider, native_session_id, connection_state, turn_state,
                                usage_observed_at, preferred_model, preferred_effort
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
                        : ParseDateTime(reader.GetString(4), "usage observation"),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
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
                    usage,
                    row.PreferredModel,
                    row.PreferredEffort));
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

    private static async Task<AgentUsageSnapshot?> LoadUsageObservationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AgentProvider provider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt;
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT observed_at FROM agent_usage_observations WHERE provider = $provider;
                         """))
        {
            command.Parameters.AddWithValue("$provider", (int)provider);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not string text)
            {
                return null;
            }

            observedAt = ParseDateTime(text, "usage observation time");
        }

        var windows = new List<AgentUsageWindow>();
        await using (var command = CreateCommand(
                         connection,
                         transaction,
                         """
                         SELECT name, used_percent, duration_ticks, resets_at
                         FROM agent_usage_observation_windows
                         WHERE provider = $provider
                         ORDER BY name;
                         """))
        {
            command.Parameters.AddWithValue("$provider", (int)provider);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var usedPercent = reader.GetDouble(1);
                if (string.IsNullOrWhiteSpace(reader.GetString(0)) || usedPercent is < 0 or > 100)
                {
                    throw new InvalidOperationException("state.db contains an invalid usage observation window.");
                }

                windows.Add(new AgentUsageWindow(
                    reader.GetString(0),
                    usedPercent,
                    reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)),
                    reader.IsDBNull(3) ? null : ParseDateTime(reader.GetString(3), "usage observation reset")));
            }
        }

        return windows.Count == 0
            ? throw new InvalidOperationException("state.db contains a usage observation without windows.")
            : new AgentUsageSnapshot(provider, observedAt, windows);
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
            objective TEXT NOT NULL DEFAULT '',
            shared_checkout_consent_at TEXT NULL,
            shared_checkout_consent_text TEXT NULL,
            shared_checkout_trust INTEGER NOT NULL DEFAULT 0,
            work_on_low_allowance INTEGER NOT NULL DEFAULT 0,
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
            preferred_model TEXT NULL,
            preferred_effort TEXT NULL,
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

        -- Usage is an account fact. A five-hour window is spent by every session on the machine,
        -- so one reading per provider serves every project. reported_by_project_id is only where
        -- the reading came from; deleting that project does not delete what it told us.
        CREATE TABLE IF NOT EXISTS agent_usage_observations (
            provider INTEGER NOT NULL PRIMARY KEY,
            observed_at TEXT NOT NULL,
            reported_by_project_id TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS agent_usage_observation_windows (
            provider INTEGER NOT NULL,
            name TEXT NOT NULL,
            used_percent REAL NOT NULL,
            duration_ticks INTEGER NULL,
            resets_at TEXT NULL,
            PRIMARY KEY (provider, name),
            FOREIGN KEY (provider)
                REFERENCES agent_usage_observations(provider) ON DELETE CASCADE
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

        """;
}
