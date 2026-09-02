using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Connects a Codex CLI hosted by a Filekin terminal to the coordinator that already owns its
/// conversation. Disposing the registration reports only that terminal process as ended; it never
/// clears the provider's saved conversation.
/// </summary>
public sealed class AgentTerminalSessionRegistration : IAsyncDisposable
{
    private readonly TerminalSessionHandle _handle;
    private int _disposed;

    internal AgentTerminalSessionRegistration(TerminalSessionHandle handle)
    {
        _handle = handle;
    }

    public AgentProvider Provider => _handle.Provider;

    public string NativeSessionId => _handle.NativeSessionId;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _handle.ReportStopped();
        await _handle.Reconciled.ConfigureAwait(false);
    }

    /// <summary>
    /// A terminal-hosted Codex CLI has no private App Server handle for Filekin to watch. Its
    /// terminal is the supported lifecycle boundary, so this small handle lets the ordinary agent
    /// stop watcher apply the same lease and presence rules when that terminal ends.
    /// </summary>
    internal sealed class TerminalSessionHandle(AgentProvider provider, string nativeSessionId)
        : IAgentSessionHandle
    {
        private readonly TaskCompletionSource _reconciled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public AgentProvider Provider { get; } = provider;

        public string NativeSessionId { get; } = nativeSessionId;

        public Task Stopped => _stopped.Task;

        public Task<string> NeedsPerson => _needsPerson.Task;

        public string? LastReport => null;

        public AgentSessionEventFeed Events { get; } = new();

        internal Task Reconciled => _reconciled.Task;

        public Task RequestStopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal void ReportStopped() => _stopped.TrySetResult();

        internal void ReportReconciled() => _reconciled.TrySetResult();

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _stopped.TrySetCanceled();
                _needsPerson.TrySetCanceled();
                _reconciled.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }
    }
}
