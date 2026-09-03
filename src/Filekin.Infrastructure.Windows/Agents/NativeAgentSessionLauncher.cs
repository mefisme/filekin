using System.Collections.Concurrent;
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
        // Claude has no event to push, so its lifecycle is polled, and every poll costs one
        // `claude agents --json` process. Five seconds meant a finished turn sat unnoticed for up to
        // ten seconds before the lease moved, because an inferred stop must hold across two polls.
        // Two seconds cuts that to about four and is still one process every two seconds, only while
        // a session is actually open.
        _claudePollInterval = claudePollInterval ?? TimeSpan.FromSeconds(2);
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
                request.Consent.WorkMode,
                request.Model,
                request.Effort,
                request.ResumeSessionId,
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

    /// <summary>
    /// The background agents Claude reports for one folder, with both identities a session has.
    /// </summary>
    /// <remarks>
    /// A failure is not an empty list, and this deliberately does not turn one into the other.
    /// "Claude says nothing is running here" and "Claude could not be asked" lead to opposite
    /// actions — the first lets a new session start, the second must stop it — so answering both
    /// with an empty list told the duplicate-session guard that a folder was clear whenever the
    /// check itself had failed, and made <see cref="AgentSessionLiveness.Unknown"/> unreachable, so
    /// the control room could never say it had no answer however carefully it was written to.
    /// Deciding what an unanswered question means belongs to the caller that knows what it is for.
    /// </remarks>
    public async Task<IReadOnlyList<ClaudeBackgroundAgent>> ListClaudeBackgroundAgentsAsync(
        string projectFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        return await new ClaudeBackgroundSessionAdapter(_claudeExecutable)
            .ListBackgroundAgentsAsync(Path.GetFullPath(projectFolderPath), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int?> CountLiveSessionsAsync(
        AgentProvider provider,
        string projectFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        if (provider != AgentProvider.ClaudeCode)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(projectFolderPath);
        var sessions = await new ClaudeBackgroundSessionAdapter(_claudeExecutable)
            .ReadLiveSessionsAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);
        return sessions.Count;
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

            var thread = string.IsNullOrWhiteSpace(request.ResumeSessionId)
                ? await client.StartThreadAsync(
                        request.ProjectFolderPath,
                        request.Model,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await client.ResumeThreadAsync(
                        request.ResumeSessionId,
                        request.ProjectFolderPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            var turn = await client.StartTurnAsync(
                    thread.ThreadId,
                    request.ProjectFolderPath,
                    request.Prompt,
                    effort: request.Effort,
                    workMode: request.Consent.WorkMode,
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
    /// report rather than watching a process. A finished turn is proof only when Claude also reports
    /// no live process; <c>state: done</c> with a pid is an idle session that still needs ending.
    /// </summary>
    private sealed class ClaudeSessionHandle : IAgentSessionHandle, ITurnScopedAgentSessionHandle
    {
        private readonly ClaudeBackgroundSessionAdapter _adapter;
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Claude cannot be given another turn while it runs, so one background session serves one
        // turn and this is answered once. The next turn is a stop and a resume, which is a new handle.
        private readonly TaskCompletionSource _turnFinished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly string _projectFolderPath;
        private readonly string _backgroundSessionId;
        private readonly CancellationTokenSource _watching = new();
        private bool _disposed;
        private int _idleObservations;
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
            _backgroundSessionId = snapshot.NativeId;

            // The short attach handle is not a conversation id and must never stand in for one: it is
            // what Filekin persists and resumes by, so a handle stored here is a resume that fails and
            // falls back to a new conversation. The adapter waits for the real one rather than
            // returning a snapshot without it, so there is nothing left to substitute.
            NativeSessionId = snapshot.ConversationSessionId
                ?? throw new ArgumentException(
                    "A launched Claude session must carry the conversation id Filekin resumes it by.",
                    nameof(snapshot));
            PublishLifecycle(snapshot);
            Stopped = WatchAsync(pollInterval);
        }

        public AgentProvider Provider => AgentProvider.ClaudeCode;

        public string NativeSessionId { get; }

        public Task Stopped { get; }

        public Task<string> NeedsPerson => _needsPerson.Task;

        public Task TurnFinished => _turnFinished.Task;

        public string? LastReport { get; private set; }

        public AgentSessionEventFeed Events { get; } = new();

        public async Task RequestStopAsync(CancellationToken cancellationToken = default) =>
            await _adapter.StopAsync(_projectFolderPath, _backgroundSessionId, cancellationToken)
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
                ? "Turn finished. The session stays open."
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
                    .ReadAsync(_projectFolderPath, _backgroundSessionId, _watching.Token)
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
                // another prompt, not as a question. That is a finished turn, and this is where
                // Filekin says so. It no longer ends the session to prove it: the lease moves on the
                // finished turn, and the session is left for whoever is reading it.
                if (snapshot.Lifecycle == ClaudeBackgroundLifecycle.Idle)
                {
                    _inactiveObservations = 0;

                    // A session that has been given its prompt but has not started answering can read
                    // idle for a single poll. Calling that a finished turn would end one before it
                    // began, so a finished turn has to still be finished on the next read.
                    if (++_idleObservations < 2)
                    {
                        continue;
                    }

                    // The turn is over; the session is not. Proving a finished turn by ending the
                    // session closed a CLI a person was reading, for a reason that had nothing to do
                    // with them. Filekin releases the turn on this signal and leaves the session
                    // alive and idle, which is what it actually is (owner decision, 2026-09-02).
                    _turnFinished.TrySetResult();
                    continue;
                }

                // Anything that is not idle is the session working again, so the run of idle reads
                // that would end a turn starts over.
                _idleObservations = 0;

                if (IsExplicitTerminal(snapshot))
                {
                    return;
                }

                // Agent View can briefly omit a pid and then respawn the same stopped session. One
                // pid-less snapshot is therefore not enough proof to release Filekin's writer lease.
                // Two consecutive provider reads, separated by the normal poll interval, are.
                if (snapshot.Lifecycle == ClaudeBackgroundLifecycle.Stopped)
                {
                    if (snapshot.ProcessId is not null)
                    {
                        _inactiveObservations = 0;
                        continue;
                    }

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
            if (snapshot.ProcessId is not null)
            {
                return false;
            }

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
                    .ReadRecentOutputAsync(_projectFolderPath, _backgroundSessionId, _watching.Token)
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
    private sealed class CodexSessionHandle : IAgentSessionHandle, IInteractiveAgentSessionHandle
    {
        private readonly CodexAppServerClient _client;
        private readonly CodexAgentSessionEventMapper _eventMapper = new();
        private readonly ConcurrentDictionary<long, CodexAppServerRequest> _pendingRequests = new();
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

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default) =>
            _client.SteerTurnAsync(_turn.ThreadId, _turn.TurnId, prompt, cancellationToken);

        public async Task RespondAsync(
            AgentSessionRequestResponse response,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (!_pendingRequests.TryGetValue(response.RequestId, out var request))
            {
                throw new InvalidOperationException("That provider request is no longer pending.");
            }

            object result = request.Method switch
            {
                "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" =>
                    ApprovalResult(response.Decision),
                "item/tool/requestUserInput" => UserInputResult(request.Parameters, response.Answer),
                _ => throw new InvalidOperationException(
                    $"Filekin cannot answer the provider request '{request.Method}'."),
            };

            await _client.RespondToServerRequestAsync(request.Id, result, cancellationToken)
                .ConfigureAwait(false);
            _pendingRequests.TryRemove(request.Id, out _);
            Events.Publish(new AgentSessionEvent(
                $"codex:request:{request.Id}",
                DateTimeOffset.Now,
                AgentSessionEventKind.Question,
                AgentSessionEventStatus.Completed,
                "Request answered",
                "Your response was sent to Codex."));
        }

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
                    _pendingRequests[request.Id] = request;
                    Events.Publish(CodexAgentSessionEventMapper.MapRequest(request, DateTimeOffset.Now));
                    _needsPerson.TrySetResult(
                        $"Codex is waiting for your response ({request.Method}).");
                }
            }
            catch (OperationCanceledException)
            {
                // Filekin stopped watching. That is not a question.
            }
        }

        private static object ApprovalResult(string? decision)
        {
            if (decision is not ("accept" or "acceptForSession" or "decline" or "cancel"))
            {
                throw new InvalidOperationException("Choose Allow once, Allow for session, Deny, or Cancel.");
            }

            return new { decision };
        }

        private static object UserInputResult(System.Text.Json.JsonElement parameters, string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException("Type an answer before sending it.");
            }

            if (!parameters.TryGetProperty("questions", out var questions) ||
                questions.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                throw new InvalidOperationException("Codex sent a user-input request without questions.");
            }

            var questionIds = questions.EnumerateArray()
                .Select(question => question.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();
            var answers = answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (questionIds.Length == 0 || (questionIds.Length > 1 && answers.Length != questionIds.Length))
            {
                throw new InvalidOperationException(
                    questionIds.Length > 1
                        ? $"Answer all {questionIds.Length} questions, one answer per line."
                        : "Codex sent a user-input request without a usable question id.");
            }

            var values = questionIds
                .Select((id, index) => new KeyValuePair<string, object>(
                    id,
                    new { answers = new[] { questionIds.Length == 1 ? answer.Trim() : answers[index] } }))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return new { answers = values };
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
