using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Starts real Codex and Claude Code sessions through each provider's own supported interface. It
/// runs the tools the user installed and signed into, passes only Filekin's project MCP identity, and
/// never types into a terminal, reads a screen, or handles a credential.
/// </summary>
public sealed class NativeAgentSessionLauncher : IAgentSessionLauncher
{
    private readonly string _claudeExecutable;
    private readonly string _codexExecutable;
    private readonly TimeSpan _claudePollInterval;

    public NativeAgentSessionLauncher(
        string codexExecutable = "codex",
        string claudeExecutable = "claude",
        TimeSpan? claudePollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(claudeExecutable);
        _codexExecutable = codexExecutable;
        _claudeExecutable = claudeExecutable;
        _claudePollInterval = claudePollInterval ?? TimeSpan.FromSeconds(5);
        if (_claudePollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(claudePollInterval));
        }
    }

    public async Task<IAgentSessionHandle> LaunchAsync(
        AgentSessionLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.McpServer.Provider != request.Provider || request.McpServer.ProjectId != request.ProjectId)
        {
            throw new ArgumentException(
                "The MCP identity must belong to the project and agent being started.",
                nameof(request));
        }

        return request.Provider switch
        {
            AgentProvider.ClaudeCode => await LaunchClaudeAsync(request, cancellationToken).ConfigureAwait(false),
            AgentProvider.Codex => await LaunchCodexAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    private async Task<IAgentSessionHandle> LaunchClaudeAsync(
        AgentSessionLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var adapter = new ClaudeBackgroundSessionAdapter(_claudeExecutable);
        var plan = ClaudeBackgroundSessionAdapter.CreateLaunchPlan(
            request.ProjectFolderPath,
            request.DisplayName,
            request.Prompt,
            request.McpServer);

        // The owner's approval is the only thing that unlocks the launch, and it is carried in the
        // request rather than assumed here.
        var snapshot = await adapter.LaunchAsync(
                plan.ApproveSharedCheckout(),
                request.Consent.Trust == SharedFolderTrust.TrustThisFolder,
                request.Model,
                request.Effort,
                cancellationToken)
            .ConfigureAwait(false);
        return new ClaudeSessionHandle(adapter, request.ProjectFolderPath, snapshot, _claudePollInterval);
    }

    /// <summary>
    /// Ends this provider's own sessions in a project folder through its documented stop. Codex has no
    /// cooperative stop: an App Server turn ends when Codex ends it, and interrupting would be a kill,
    /// so Filekin says there is nothing it can stop rather than pretending.
    /// </summary>
    public async Task<int?> StopSessionsAsync(
        AgentProvider provider,
        string projectFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        if (provider != AgentProvider.ClaudeCode)
        {
            return null;
        }

        var stopped = await new ClaudeBackgroundSessionAdapter(_claudeExecutable)
            .StopAllAsync(projectFolderPath, cancellationToken)
            .ConfigureAwait(false);
        return stopped.Count;
    }

    private async Task<IAgentSessionHandle> LaunchCodexAsync(
        AgentSessionLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var client = new CodexAppServerClient(request.McpServer, _codexExecutable);
        try
        {
            var account = await client.ReadAccountAsync(cancellationToken).ConfigureAwait(false);
            if (!account.UsesChatGptSubscription)
            {
                throw new InvalidOperationException(
                    "Filekin refused to start Codex because it did not prove ChatGPT subscription mode.");
            }

            var thread = await client.StartThreadAsync(
                    request.ProjectFolderPath,
                    request.Model,
                    cancellationToken)
                .ConfigureAwait(false);
            var turn = await client.StartTurnAsync(
                    thread.ThreadId,
                    request.ProjectFolderPath,
                    request.Prompt,
                    effort: request.Effort,
                    trustFolder: request.Consent.Trust == SharedFolderTrust.TrustThisFolder,
                    cancellationToken)
                .ConfigureAwait(false);
            return new CodexSessionHandle(client, thread, turn);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// One Claude background session. Claude reports its own lifecycle, so Filekin asks for that
    /// report rather than watching a process, and treats a finished lifecycle as proof of the stop.
    /// </summary>
    private sealed class ClaudeSessionHandle : IAgentSessionHandle
    {
        private readonly ClaudeBackgroundSessionAdapter _adapter;
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly string _projectFolderPath;
        private readonly CancellationTokenSource _watching = new();
        private bool _disposed;
        private int _idleStopAttempts;
        private int _inactiveObservations;
        private string? _lastOutput;

        public ClaudeSessionHandle(
            ClaudeBackgroundSessionAdapter adapter,
            string projectFolderPath,
            ClaudeBackgroundSessionSnapshot snapshot,
            TimeSpan pollInterval)
        {
            _adapter = adapter;
            _projectFolderPath = projectFolderPath;
            NativeSessionId = snapshot.NativeId;
            PublishLifecycle(snapshot);
            Stopped = WatchAsync(pollInterval);
        }

        public AgentProvider Provider => AgentProvider.ClaudeCode;

        public string NativeSessionId { get; }

        public Task Stopped { get; }

        public Task<string> NeedsPerson => _needsPerson.Task;

        public string? LastReport { get; private set; }

        public AgentSessionEventFeed Events { get; } = new();

        public async Task RequestStopAsync(CancellationToken cancellationToken = default) =>
            await _adapter.StopAsync(_projectFolderPath, NativeSessionId, cancellationToken)
                .ConfigureAwait(false);

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _watching.CancelAsync().ConfigureAwait(false);
            _watching.Dispose();
        }

        private static string Describe(ClaudeBackgroundSessionSnapshot snapshot) =>
            snapshot.Lifecycle == ClaudeBackgroundLifecycle.Idle
                ? "Response finished; ending the background session."
                : snapshot.WaitingFor is { Length: > 0 } waiting
                ? $"{snapshot.RawState} ({waiting})"
                : snapshot.RawStatus is { Length: > 0 } status
                    ? $"{snapshot.RawState} ({status})"
                    : snapshot.RawState;

        private async Task WatchAsync(TimeSpan pollInterval)
        {
            while (true)
            {
                await Task.Delay(pollInterval, _watching.Token).ConfigureAwait(false);
                var snapshot = await _adapter
                    .ReadAsync(_projectFolderPath, NativeSessionId, _watching.Token)
                    .ConfigureAwait(false);

                // A session Claude no longer lists has ended as far as Filekin can tell, which is the
                // same thing it needs to know.
                if (snapshot is null)
                {
                    LastReport = "Claude Code no longer lists this session.";
                    Events.Publish(new AgentSessionEvent(
                        "claude:lifecycle",
                        DateTimeOffset.Now,
                        AgentSessionEventKind.Status,
                        AgentSessionEventStatus.Completed,
                        "Claude Code session",
                        LastReport));
                    if (++_inactiveObservations >= 2)
                    {
                        return;
                    }

                    continue;
                }

                LastReport = Describe(snapshot);
                PublishLifecycle(snapshot);
                await PublishRecentOutputAsync().ConfigureAwait(false);

                // Agent View documents idle as a response that has finished and is waiting for
                // another prompt, not as a question. A Filekin run is one turn and its Section 3
                // surface cannot reply, so leaving that background process alive would strand the
                // lease and prevent a written handoff from ever moving. Ask Claude to end the idle
                // session once; the next provider snapshot is still the proof that it ended.
                if (snapshot.Lifecycle == ClaudeBackgroundLifecycle.Idle)
                {
                    _inactiveObservations = 0;
                    if (_idleStopAttempts < 2)
                    {
                        _idleStopAttempts++;
                        try
                        {
                            var stopped = await _adapter
                                .StopAsync(_projectFolderPath, NativeSessionId, _watching.Token)
                                .ConfigureAwait(false);
                            if (stopped is null)
                            {
                                LastReport = "Claude Code no longer lists this session.";
                                _inactiveObservations = 1;
                                continue;
                            }

                            LastReport = Describe(stopped);
                            PublishLifecycle(stopped);
                            if (IsExplicitTerminal(stopped))
                            {
                                return;
                            }

                            if (stopped.Lifecycle == ClaudeBackgroundLifecycle.Stopped)
                            {
                                _inactiveObservations = 1;
                            }
                        }
                        catch (Exception exception) when (exception is InvalidOperationException
                            or System.ComponentModel.Win32Exception
                            or IOException)
                        {
                            var reason = "Claude Code finished its response, but Filekin could not end "
                                + $"the background session: {exception.Message}";
                            Events.Publish(new AgentSessionEvent(
                                "claude:idle-stop-failed",
                                DateTimeOffset.Now,
                                AgentSessionEventKind.Error,
                                AgentSessionEventStatus.NeedsAttention,
                                "Claude Code could not finish",
                                reason));
                            _needsPerson.TrySetResult(reason);
                        }
                    }
                    else
                    {
                        var reason = "Claude Code finished its response, but its background session "
                            + "kept returning after two stop requests. End it in Claude Agent View.";
                        Events.Publish(new AgentSessionEvent(
                            "claude:idle-stop-did-not-stick",
                            DateTimeOffset.Now,
                            AgentSessionEventKind.Error,
                            AgentSessionEventStatus.NeedsAttention,
                            "Claude Code session did not end",
                            reason));
                        _needsPerson.TrySetResult(reason);
                    }

                    continue;
                }

                if (IsExplicitTerminal(snapshot))
                {
                    return;
                }

                // Agent View can briefly omit a pid and then respawn the same stopped session. One
                // pid-less snapshot is therefore not enough proof to release Filekin's writer lease.
                // Two consecutive provider reads, separated by the normal poll interval, are.
                if (snapshot.Lifecycle == ClaudeBackgroundLifecycle.Stopped)
                {
                    if (++_inactiveObservations >= 2)
                    {
                        return;
                    }

                    continue;
                }

                _inactiveObservations = 0;

                // A background session waiting on a person looks exactly like a busy one from outside.
                // Saying so is the whole point: a stuck session must never keep reading as working.
                if (snapshot.RequiresOwnerAttention)
                {
                    var isQuestion = snapshot.Lifecycle == ClaudeBackgroundLifecycle.NeedsInput;
                    var reason = isQuestion
                        ? $"Claude Code is waiting for you: {Describe(snapshot)}. Answering in Filekin "
                            + "is not built yet; use Claude's own Agent View, or stop the agent here."
                        : $"Filekin cannot tell what Claude Code is doing: {Describe(snapshot)}. "
                            + "Review it in Claude's Agent View, or stop the agent here.";
                    Events.Publish(new AgentSessionEvent(
                        isQuestion ? "claude:question" : "claude:unknown",
                        DateTimeOffset.Now,
                        isQuestion ? AgentSessionEventKind.Question : AgentSessionEventKind.Error,
                        AgentSessionEventStatus.NeedsAttention,
                        isQuestion ? "Claude Code needs you" : "Claude Code state is unclear",
                        Describe(snapshot),
                        isQuestion
                            ? "Answering in Filekin is not built yet. Use Claude's own Agent View, or stop the agent here."
                            : "Review this session in Claude's Agent View, or stop it here."));
                    _needsPerson.TrySetResult(reason);
                }
            }
        }

        private static bool IsExplicitTerminal(ClaudeBackgroundSessionSnapshot snapshot)
        {
            var state = snapshot.RawState.Trim().Replace('-', '_').ToLowerInvariant();
            return state is "completed" or "done" or "stopped" or "cancelled" or "canceled"
                or "failed" or "error";
        }

        private void PublishLifecycle(ClaudeBackgroundSessionSnapshot snapshot)
        {
            var (kind, status, title) = snapshot.Lifecycle switch
            {
                ClaudeBackgroundLifecycle.Working =>
                    (AgentSessionEventKind.Status, AgentSessionEventStatus.InProgress, "Claude Code session"),
                ClaudeBackgroundLifecycle.Idle =>
                    (AgentSessionEventKind.Status, AgentSessionEventStatus.InProgress, "Claude Code is finishing"),
                ClaudeBackgroundLifecycle.NeedsInput =>
                    (AgentSessionEventKind.Question, AgentSessionEventStatus.NeedsAttention, "Claude Code needs you"),
                ClaudeBackgroundLifecycle.Unknown =>
                    (AgentSessionEventKind.Error, AgentSessionEventStatus.NeedsAttention, "Claude Code state is unclear"),
                ClaudeBackgroundLifecycle.Failed =>
                    (AgentSessionEventKind.Error, AgentSessionEventStatus.Failed, "Claude Code failed"),
                _ => (AgentSessionEventKind.Status, AgentSessionEventStatus.Completed, "Claude Code session"),
            };
            Events.Publish(new AgentSessionEvent(
                "claude:lifecycle",
                snapshot.StartedAt,
                kind,
                status,
                title,
                Describe(snapshot)));
        }

        private async Task PublishRecentOutputAsync()
        {
            try
            {
                var output = await _adapter
                    .ReadRecentOutputAsync(_projectFolderPath, NativeSessionId, _watching.Token)
                    .ConfigureAwait(false);
                if (output is null || string.Equals(output, _lastOutput, StringComparison.Ordinal))
                {
                    return;
                }

                _lastOutput = output;
                Events.Publish(new AgentSessionEvent(
                    "claude:recent-output",
                    DateTimeOffset.Now,
                    AgentSessionEventKind.Response,
                    AgentSessionEventStatus.Information,
                    "Claude Code",
                    "Recent provider output",
                    output));
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException)
            {
                Events.Publish(new AgentSessionEvent(
                    "claude:recent-output-unavailable",
                    DateTimeOffset.Now,
                    AgentSessionEventKind.Status,
                    AgentSessionEventStatus.Information,
                    "Recent output unavailable",
                    exception.Message));
            }
        }
    }

    /// <summary>
    /// One Codex turn on its own thread. Codex has no "finish up when you can" command, so Filekin's
    /// stop request reaches the agent through the coordination state it reads, and this handle only
    /// waits for the turn Codex itself ends. Interrupting a turn would be a kill, not a cooperative
    /// stop, so it is not done here.
    /// </summary>
    private sealed class CodexSessionHandle : IAgentSessionHandle
    {
        private readonly CodexAppServerClient _client;
        private readonly CodexAgentSessionEventMapper _eventMapper = new();
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CodexTurnHandle _turn;
        private readonly CancellationTokenSource _watching = new();
        private bool _disposed;

        public CodexSessionHandle(CodexAppServerClient client, CodexThreadSession thread, CodexTurnHandle turn)
        {
            _client = client;
            _turn = turn;
            NativeSessionId = thread.SessionId;
            Events.Publish(new AgentSessionEvent(
                $"codex:turn:{turn.TurnId}",
                DateTimeOffset.Now,
                AgentSessionEventKind.Status,
                AgentSessionEventStatus.InProgress,
                "Codex turn",
                "Turn started."));
            Stopped = WatchAsync();
            _ = WatchForQuestionsAsync();
        }

        public AgentProvider Provider => AgentProvider.Codex;

        public string NativeSessionId { get; }

        public Task Stopped { get; }

        public Task<string> NeedsPerson => _needsPerson.Task;

        public string? LastReport { get; private set; }

        public AgentSessionEventFeed Events { get; } = new();

        public Task RequestStopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _watching.CancelAsync().ConfigureAwait(false);
            _watching.Dispose();
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        private async Task WatchAsync()
        {
            await foreach (var notification in _client.ReadNotificationsAsync(_watching.Token)
                .ConfigureAwait(false))
            {
                if (_eventMapper.MapNotification(notification, DateTimeOffset.Now) is { } sessionEvent)
                {
                    Events.Publish(sessionEvent);
                }

                // Codex's own words about what it did. Filekin keeps the latest one so a turn that
                // ends with nothing to show can still say why.
                if (ReadAgentMessage(notification) is { Length: > 0 } message)
                {
                    LastReport = message;
                }

                if (CodexAppServerProtocol.TryParseTurnCompletion(notification, out var completion) &&
                    completion?.TurnId == _turn.TurnId)
                {
                    if (!string.Equals(completion.Status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        LastReport = completion.ErrorMessage is { Length: > 0 } error
                            ? $"{completion.Status}: {error}"
                            : completion.Status;
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Codex asks for permission through server-initiated requests. Filekin never answers one for
        /// the user, so the only honest thing it can do is say the session is waiting for them.
        /// </summary>
        private async Task WatchForQuestionsAsync()
        {
            try
            {
                await foreach (var request in _client.ReadServerRequestsAsync(_watching.Token)
                    .ConfigureAwait(false))
                {
                    Events.Publish(CodexAgentSessionEventMapper.MapRequest(request, DateTimeOffset.Now));
                    _needsPerson.TrySetResult(
                        $"Codex is waiting for permission ({request.Method}). Answering in Filekin is "
                        + "not built yet; use Codex's own session UI, or stop the agent here.");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Filekin stopped watching. That is not a question.
            }
        }

        private static string? ReadAgentMessage(CodexAppServerNotification notification)
        {
            if (!string.Equals(notification.Method, "item/completed", StringComparison.Ordinal) ||
                !notification.Parameters.TryGetProperty("item", out var item) ||
                !item.TryGetProperty("type", out var type) ||
                type.GetString() != "agentMessage" ||
                !item.TryGetProperty("text", out var text))
            {
                return null;
            }

            return text.GetString();
        }
    }
}
