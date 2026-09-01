namespace Filekin.Infrastructure.Windows.Agents;

public enum ClaudeBackgroundLifecycle
{
    Unknown,
    Working,
    Idle,
    NeedsInput,
    Completed,
    Stopped,
    Failed,
}

public sealed record ClaudeBackgroundSessionSnapshot(
    string NativeId,
    string? ConversationSessionId,
    string ProjectFolderPath,
    ClaudeBackgroundLifecycle Lifecycle,
    string RawState,
    string? RawStatus,
    string? WaitingFor,
    int? ProcessId,
    DateTimeOffset StartedAt)
{
    public bool RequiresOwnerAttention => Lifecycle is
        ClaudeBackgroundLifecycle.Unknown or
        ClaudeBackgroundLifecycle.NeedsInput or
        ClaudeBackgroundLifecycle.Failed;
}

/// <summary>
/// Opt-in native Claude Code Agent View adapter. It uses the user's installed CLI and subscription,
/// preserves Claude permissions, fixes the session to Filekin's MCP server, and refuses to launch
/// until the shared-checkout settings preview has been explicitly approved.
/// </summary>
public sealed class ClaudeBackgroundSessionAdapter
{
    private readonly ClaudeCliClient _client;

    public ClaudeBackgroundSessionAdapter(string executable = "claude")
        : this(new ClaudeCliClient(executable))
    {
    }

    internal ClaudeBackgroundSessionAdapter(ClaudeCliClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public static ClaudeBackgroundLaunchPlan CreateLaunchPlan(
        string projectFolderPath,
        string displayName,
        string prompt,
        AgentMcpLaunchConfiguration mcpServer) =>
        ClaudeBackgroundLaunchPlan.Create(projectFolderPath, displayName, prompt, mcpServer);

    /// <param name="trustFolder">
    /// Set only when the owner has said this project folder is safe to work in. See
    /// <see cref="ClaudeCliClient.StartBackgroundSessionAsync"/>: it is never a permission bypass.
    /// </param>
    public async Task<ClaudeBackgroundSessionSnapshot> LaunchAsync(
        ApprovedClaudeBackgroundLaunch approvedLaunch,
        bool trustFolder = false,
        string? model = null,
        string? effort = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedLaunch);
        var plan = approvedLaunch.Plan;
        var account = await _client.ReadAccountAsync(plan.ProjectFolderPath, cancellationToken)
            .ConfigureAwait(false);
        if (!account.UsesClaudeSubscription)
        {
            throw new InvalidOperationException(
                "Filekin refused to start Claude Code because the CLI did not prove first-party Claude.ai subscription mode.");
        }

        var nativeId = await _client.StartBackgroundSessionAsync(
                plan.ProjectFolderPath,
                plan.DisplayName,
                plan.Prompt,
                plan.McpConfigurationJson,
                plan.SettingsPreviewJson,
                trustFolder,
                model,
                effort,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var sessions = await _client.ReadBackgroundSessionsAsync(
                    plan.ProjectFolderPath,
                    includeCompleted: true,
                    cancellationToken)
                .ConfigureAwait(false);
            var session = sessions.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, nativeId, StringComparison.Ordinal));
            if (session is null)
            {
                throw new InvalidOperationException(
                    "Claude Code started a background session but did not report it back to Filekin.");
            }

            if (!string.Equals(session.Kind, "background", StringComparison.OrdinalIgnoreCase) ||
                !ClaudeBackgroundLaunchPlan.PathsEqual(plan.ProjectFolderPath, session.WorkingDirectory))
            {
                throw new InvalidOperationException(
                    "Claude Code did not bind the new background session to Filekin's shared project checkout.");
            }

            return ToSnapshot(session);
        }
        catch (Exception validationException)
        {
            var stopException = await TryStopInvalidLaunchAsync(plan.ProjectFolderPath, nativeId)
                .ConfigureAwait(false);
            if (stopException is not null)
            {
                throw new InvalidOperationException(
                    $"Claude background session '{nativeId}' failed Filekin validation and could not be stopped automatically. Review it in Claude Agent View.",
                    new AggregateException(validationException, stopException));
            }

            throw;
        }
    }

    public async Task<ClaudeBackgroundSessionSnapshot?> ReadAsync(
        string projectFolderPath,
        string nativeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeId);
        var sessions = await _client.ReadBackgroundSessionsAsync(
                projectFolderPath,
                includeCompleted: true,
                cancellationToken)
            .ConfigureAwait(false);
        var session = sessions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, nativeId, StringComparison.Ordinal));
        return session is null ? null : ToSnapshot(session);
    }

    public async Task<ClaudeBackgroundSessionSnapshot?> StopAsync(
        string projectFolderPath,
        string nativeId,
        CancellationToken cancellationToken = default)
    {
        await _client.StopBackgroundSessionAsync(projectFolderPath, nativeId, cancellationToken)
            .ConfigureAwait(false);
        return await ReadAsync(projectFolderPath, nativeId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The background sessions Claude still has open in this folder. A session that has finished its
    /// turn is still open, so this is the only honest answer to "is anything still running here?".
    /// </summary>
    public async Task<IReadOnlyList<ClaudeBackgroundSessionSnapshot>> ReadLiveSessionsAsync(
        string projectFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        var fullPath = Path.GetFullPath(projectFolderPath);
        var sessions = await _client
            .ReadBackgroundSessionsAsync(fullPath, includeCompleted: false, cancellationToken)
            .ConfigureAwait(false);
        return sessions
            .Where(candidate => string.Equals(
                Path.GetFullPath(candidate.WorkingDirectory),
                fullPath,
                StringComparison.OrdinalIgnoreCase))
            .Select(ToSnapshot)
            .ToArray();
    }

    /// <summary>
    /// Stops every background session Claude still lists for this folder and returns their ids. Only
    /// sessions whose own working directory is this folder are touched.
    /// </summary>
    public async Task<IReadOnlyList<string>> StopAllAsync(
        string projectFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        var fullPath = Path.GetFullPath(projectFolderPath);
        var sessions = await _client.ReadBackgroundSessionsAsync(fullPath, includeCompleted: false, cancellationToken)
            .ConfigureAwait(false);
        var stopped = new List<string>();
        foreach (var session in sessions.Where(candidate => string.Equals(
            Path.GetFullPath(candidate.WorkingDirectory),
            fullPath,
            StringComparison.OrdinalIgnoreCase)))
        {
            await _client.StopBackgroundSessionAsync(fullPath, session.Id, cancellationToken)
                .ConfigureAwait(false);
            stopped.Add(session.Id);
        }

        return stopped;
    }

    public Task<string?> ReadRecentOutputAsync(
        string projectFolderPath,
        string nativeId,
        CancellationToken cancellationToken = default) =>
        _client.ReadBackgroundSessionLogsAsync(projectFolderPath, nativeId, cancellationToken);

    private async Task<Exception?> TryStopInvalidLaunchAsync(string projectFolderPath, string nativeId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _client.StopBackgroundSessionAsync(projectFolderPath, nativeId, timeout.Token)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static ClaudeBackgroundSessionSnapshot ToSnapshot(ClaudeBackgroundSession session) =>
        new(
            session.Id,
            session.SessionId,
            Path.GetFullPath(session.WorkingDirectory),
            MapLifecycle(session.State, session.Status, session.WaitingFor, session.ProcessId),
            session.State,
            session.Status,
            session.WaitingFor,
            session.ProcessId,
            session.StartedAt);

    private static ClaudeBackgroundLifecycle MapLifecycle(
        string state,
        string? status,
        string? waitingFor,
        int? processId)
    {
        var normalizedState = state.Trim().Replace('-', '_').ToLowerInvariant();
        var normalizedStatus = status?.Trim().Replace('-', '_').ToLowerInvariant();

        var terminal = normalizedState switch
        {
            "completed" or "done" => ClaudeBackgroundLifecycle.Completed,
            "stopped" or "cancelled" or "canceled" => ClaudeBackgroundLifecycle.Stopped,
            "failed" or "error" => ClaudeBackgroundLifecycle.Failed,
            _ => (ClaudeBackgroundLifecycle?)null,
        };
        if (terminal is { } completed)
        {
            return completed;
        }

        // Agent View keeps resumable rows after their process exits. The JSON then has no pid even
        // when the row still remembers that its last conversational state was blocked/idle. There is
        // no live provider process holding Filekin's turn in that case.
        if (processId is null)
        {
            return ClaudeBackgroundLifecycle.Stopped;
        }

        return normalizedState switch
        {
            "running" or "working" => ClaudeBackgroundLifecycle.Working,
            "blocked" when normalizedStatus == "idle" && string.IsNullOrWhiteSpace(waitingFor) =>
                ClaudeBackgroundLifecycle.Idle,
            "blocked" or "waiting" or "needs_input" => ClaudeBackgroundLifecycle.NeedsInput,
            _ => normalizedStatus switch
            {
                "running" or "working" => ClaudeBackgroundLifecycle.Working,
                "idle" when string.IsNullOrWhiteSpace(waitingFor) => ClaudeBackgroundLifecycle.Idle,
                "blocked" or "waiting" or "needs_input" => ClaudeBackgroundLifecycle.NeedsInput,
                "completed" or "done" => ClaudeBackgroundLifecycle.Completed,
                "stopped" or "cancelled" or "canceled" => ClaudeBackgroundLifecycle.Stopped,
                "failed" or "error" => ClaudeBackgroundLifecycle.Failed,
                _ => ClaudeBackgroundLifecycle.Unknown,
            },
        };
    }
}
