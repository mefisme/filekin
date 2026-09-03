using Filekin.Core.Agents;

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
    private const int ConversationIdReadAttempts = 5;

    private readonly ClaudeCliClient _client;
    private readonly TimeSpan _conversationIdRetryDelay;

    public ClaudeBackgroundSessionAdapter(string executable = "claude")
        : this(new ClaudeCliClient(executable))
    {
    }

    internal ClaudeBackgroundSessionAdapter(
        ClaudeCliClient client,
        TimeSpan? conversationIdRetryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _conversationIdRetryDelay = conversationIdRetryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    /// <summary>
    /// The background agents Claude reports for one folder, with both of the identities a background
    /// session has: the conversation Filekin stores, and the short handle <c>attach</c> takes.
    /// </summary>
    public Task<IReadOnlyList<ClaudeBackgroundAgent>> ListBackgroundAgentsAsync(
        string projectFolderPath,
        CancellationToken cancellationToken = default) =>
        _client.ListBackgroundAgentsAsync(projectFolderPath, cancellationToken);

    public static ClaudeBackgroundLaunchPlan CreateLaunchPlan(
        string projectFolderPath,
        string displayName,
        string prompt,
        AgentMcpLaunchConfiguration mcpServer) =>
        ClaudeBackgroundLaunchPlan.Create(projectFolderPath, displayName, prompt, mcpServer);

    /// <param name="workMode">
    /// How the owner said an agent may work in this folder. See
    /// <see cref="ClaudeCliClient.StartBackgroundSessionAsync"/>: it is never a permission bypass.
    /// </param>
    public async Task<ClaudeBackgroundSessionSnapshot> LaunchAsync(
        ApprovedClaudeBackgroundLaunch approvedLaunch,
        AgentWorkMode workMode = AgentWorkMode.UseMyOwnSettings,
        string? model = null,
        string? effort = null,
        string? resumeSessionId = null,
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
                workMode,
                model,
                effort,
                resumeSessionId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var session = await ReadLaunchedSessionAsync(plan.ProjectFolderPath, nativeId, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(session.Kind, "background", StringComparison.OrdinalIgnoreCase) ||
                !ClaudeBackgroundLaunchPlan.PathsEqual(plan.ProjectFolderPath, session.WorkingDirectory))
            {
                throw new InvalidOperationException(
                    "Claude Code did not bind the new background session to Filekin's shared project checkout.");
            }

            // A background session has two identities and only one of them is worth storing. Claude
            // lists the short handle as soon as the session exists but fills the conversation id in a
            // moment later, so a listing read this early can carry no conversation at all. Filekin
            // stores the conversation, because that is what a handoff resumes and what `--resume`
            // takes. Storing the handle in its place makes attach refuse, makes resume fail, and drops
            // Filekin into starting a brand new conversation instead — this agent's memory thrown away
            // without anybody choosing it. So wait for the real one, and never substitute the handle.
            for (var attempt = 0;
                string.IsNullOrWhiteSpace(session.SessionId) && attempt < ConversationIdReadAttempts;
                attempt++)
            {
                await Task.Delay(_conversationIdRetryDelay, cancellationToken).ConfigureAwait(false);
                session = await ReadLaunchedSessionAsync(plan.ProjectFolderPath, nativeId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(session.SessionId))
            {
                throw new InvalidOperationException(
                    "Claude Code started a background session but never reported the conversation id "
                    + "Filekin resumes it by, so Filekin could not record which conversation this is.");
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
    /// Stops the named conversations, and only those, returning the ones actually asked to stop.
    /// </summary>
    /// <remarks>
    /// It takes conversation ids because that is the identity Filekin records when it opens a
    /// session, and it stops nothing it was not given. A person may well be running Claude Code in
    /// the same folder for their own reasons, and an earlier version of this stopped every live
    /// session the folder had, so ending one agent project's work could end work that had nothing to
    /// do with Filekin.
    ///
    /// The mapping is read from Claude's own listing and never guessed. A session has two ids —
    /// <see cref="ClaudeBackgroundSession.SessionId"/> is the conversation Filekin stores, and
    /// <see cref="ClaudeBackgroundSession.Id"/> is the short handle <c>stop</c> takes — and one
    /// cannot be derived from the other.
    /// </remarks>
    public async Task<IReadOnlyList<string>> StopConversationsAsync(
        string projectFolderPath,
        IReadOnlyCollection<string> conversationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        ArgumentNullException.ThrowIfNull(conversationIds);
        if (conversationIds.Count == 0)
        {
            return [];
        }

        var fullPath = Path.GetFullPath(projectFolderPath);
        var sessions = await _client.ReadBackgroundSessionsAsync(fullPath, includeCompleted: false, cancellationToken)
            .ConfigureAwait(false);
        var stopped = new List<string>();
        foreach (var session in sessions.Where(candidate =>
            string.Equals(
                Path.GetFullPath(candidate.WorkingDirectory),
                fullPath,
                StringComparison.OrdinalIgnoreCase) &&
            candidate.SessionId is { Length: > 0 } conversation &&
            conversationIds.Contains(conversation, StringComparer.Ordinal)))
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

    private async Task<ClaudeBackgroundSession> ReadLaunchedSessionAsync(
        string projectFolderPath,
        string nativeId,
        CancellationToken cancellationToken)
    {
        var sessions = await _client.ReadBackgroundSessionsAsync(
                projectFolderPath,
                includeCompleted: true,
                cancellationToken)
            .ConfigureAwait(false);
        return sessions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, nativeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Claude Code started a background session but did not report it back to Filekin.");
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

        // Claude's "done" describes the turn, not the background process. A done row with a pid is
        // still an attachable session holding its Filekin helper open, so drive it through the idle
        // stop path instead of releasing the lease and pretending it disappeared.
        if (processId is > 0 && normalizedState is "completed" or "done")
        {
            return ClaudeBackgroundLifecycle.Idle;
        }

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

        // The two fields disagree at the end of a turn: the coarse state can still read "working"
        // while the row's own status has already gone idle, which is exactly what a finished
        // background turn looks like from outside. Believing state alone leaves Filekin waiting for a
        // turn that ended, holding the writer lease, and a submitted handoff that can never move —
        // the same stall the blocked case below was already corrected for. Nothing is waited on, so
        // there is no question outstanding and no work in flight to interrupt.
        if (normalizedStatus == "idle" && string.IsNullOrWhiteSpace(waitingFor))
        {
            return ClaudeBackgroundLifecycle.Idle;
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
