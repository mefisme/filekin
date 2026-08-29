namespace Filekin.Infrastructure.Windows.Agents;

public enum ClaudeBackgroundLifecycle
{
    Unknown,
    Working,
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

    public async Task<ClaudeBackgroundSessionSnapshot> LaunchAsync(
        ApprovedClaudeBackgroundLaunch approvedLaunch,
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
            MapLifecycle(session.State, session.Status),
            session.State,
            session.Status,
            session.WaitingFor,
            session.ProcessId,
            session.StartedAt);

    private static ClaudeBackgroundLifecycle MapLifecycle(string state, string? status)
    {
        var normalizedState = state.Trim().Replace('-', '_').ToLowerInvariant();
        var normalizedStatus = status?.Trim().Replace('-', '_').ToLowerInvariant();
        return normalizedState switch
        {
            "running" or "working" => ClaudeBackgroundLifecycle.Working,
            "blocked" or "waiting" or "needs_input" => ClaudeBackgroundLifecycle.NeedsInput,
            "completed" or "done" => ClaudeBackgroundLifecycle.Completed,
            "stopped" or "cancelled" or "canceled" => ClaudeBackgroundLifecycle.Stopped,
            "failed" or "error" => ClaudeBackgroundLifecycle.Failed,
            _ => normalizedStatus switch
            {
                "running" or "working" => ClaudeBackgroundLifecycle.Working,
                "blocked" or "waiting" or "needs_input" => ClaudeBackgroundLifecycle.NeedsInput,
                "completed" or "done" => ClaudeBackgroundLifecycle.Completed,
                "stopped" or "cancelled" or "canceled" => ClaudeBackgroundLifecycle.Stopped,
                "failed" or "error" => ClaudeBackgroundLifecycle.Failed,
                _ => ClaudeBackgroundLifecycle.Unknown,
            },
        };
    }
}
