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
        var snapshot = await adapter.LaunchAsync(plan.ApproveSharedCheckout(), cancellationToken)
            .ConfigureAwait(false);
        return new ClaudeSessionHandle(adapter, request.ProjectFolderPath, snapshot.NativeId, _claudePollInterval);
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

            var thread = await client.StartThreadAsync(request.ProjectFolderPath, cancellationToken)
                .ConfigureAwait(false);
            var turn = await client.StartTurnAsync(
                    thread.ThreadId,
                    request.ProjectFolderPath,
                    request.Prompt,
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
        private readonly string _projectFolderPath;
        private readonly CancellationTokenSource _watching = new();
        private bool _disposed;

        public ClaudeSessionHandle(
            ClaudeBackgroundSessionAdapter adapter,
            string projectFolderPath,
            string nativeSessionId,
            TimeSpan pollInterval)
        {
            _adapter = adapter;
            _projectFolderPath = projectFolderPath;
            NativeSessionId = nativeSessionId;
            Stopped = WatchAsync(pollInterval);
        }

        public AgentProvider Provider => AgentProvider.ClaudeCode;

        public string NativeSessionId { get; }

        public Task Stopped { get; }

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
                if (snapshot is null ||
                    snapshot.Lifecycle is ClaudeBackgroundLifecycle.Completed
                        or ClaudeBackgroundLifecycle.Stopped
                        or ClaudeBackgroundLifecycle.Failed)
                {
                    return;
                }
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
        private readonly CodexTurnHandle _turn;
        private readonly CancellationTokenSource _watching = new();
        private bool _disposed;

        public CodexSessionHandle(CodexAppServerClient client, CodexThreadSession thread, CodexTurnHandle turn)
        {
            _client = client;
            _turn = turn;
            NativeSessionId = thread.SessionId;
            Stopped = WatchAsync();
        }

        public AgentProvider Provider => AgentProvider.Codex;

        public string NativeSessionId { get; }

        public Task Stopped { get; }

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
                if (CodexAppServerProtocol.TryParseTurnCompletion(notification, out var completion) &&
                    completion?.TurnId == _turn.TurnId)
                {
                    return;
                }
            }
        }
    }
}
