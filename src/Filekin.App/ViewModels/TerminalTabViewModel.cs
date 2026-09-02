using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Filekin.Core.Agents;
using Filekin.Core.Terminal;
using Filekin.Core.Terminal.Emulation;

namespace Filekin.App.ViewModels;

/// <summary>The coordinated provider conversation hosted by a specially marked terminal tab.</summary>
public sealed record AgentTerminalIdentity(
    Guid ProjectId,
    AgentProvider Provider,
    string NativeSessionId);

/// <summary>Owns one ConPTY session and its interpreted terminal screen for a workspace tab.</summary>
public sealed class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITerminalSession _session;
    private readonly Dispatcher _dispatcher;
    private readonly ITrackedInitialCommandTerminalSession? _trackedInitialCommand;
    private IAsyncDisposable? _agentSessionLifetime;
    private int _disposed;
    private bool _isSelected;

    public TerminalTabViewModel(
        string title,
        ITerminalSession session,
        Dispatcher dispatcher,
        AgentTerminalIdentity? agentSession = null,
        IAsyncDisposable? agentSessionLifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Title = title;
        AgentSession = agentSession;
        _agentSessionLifetime = agentSessionLifetime;
        _trackedInitialCommand = session as ITrackedInitialCommandTerminalSession;
        _session = session;
        _dispatcher = dispatcher;
        Emulator = new TerminalEmulator();
        Emulator.ResponseGenerated += OnResponseGenerated;
        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;
        if (agentSession is not null && _trackedInitialCommand is not null)
        {
            _trackedInitialCommand.InitialCommandCompleted += OnInitialCommandCompleted;
        }

        if (_session.HasExited)
        {
            QueueExited(_session.ExitCode ?? -1);
        }
    }

    public event EventHandler<TerminalExitEventArgs>? RootShellExited;

    /// <summary>Raised when the provider CLI returns to the still-running PowerShell root.</summary>
    public event EventHandler? AgentProcessExited;

    public string Title { get; }

    /// <summary>
    /// Whether this terminal is hosting one of the agent sessions Filekin coordinates. It is a plain
    /// shell either way; the tab is marked so a coordinated session is not mistaken for a shell
    /// somebody opened for themselves.
    /// </summary>
    public bool IsAgentSession => AgentSession is not null;

    /// <summary>
    /// The exact project/provider conversation this tab opened, or <see langword="null"/> for an
    /// ordinary terminal. Identity is retained after the child CLI returns to PowerShell so the tab
    /// remains visibly distinct and lifecycle actions never target an unrelated terminal.
    /// </summary>
    public AgentTerminalIdentity? AgentSession { get; private set; }

    public TerminalEmulator Emulator { get; }

    public bool HasExited => _session.HasExited;

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public ValueTask WriteAsync(string text, CancellationToken cancellationToken = default) =>
        _session.WriteAsync(text, cancellationToken);

    public void Resize(TerminalSize size)
    {
        if (!HasExited)
        {
            _session.Resize(size);
        }
    }

    private void OnOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        var bytes = e.Data.ToArray();
        _ = _dispatcher.BeginInvoke(() => Emulator.Process(bytes));
    }

    private void OnExited(object? sender, TerminalExitEventArgs e) => QueueExited(e.ExitCode);

    private void OnInitialCommandCompleted(object? sender, EventArgs e) =>
        _ = _dispatcher.BeginInvoke(() => AgentProcessExited?.Invoke(this, EventArgs.Empty));

    private void QueueExited(int exitCode) =>
        _ = _dispatcher.BeginInvoke(() => RootShellExited?.Invoke(this, new TerminalExitEventArgs(exitCode)));

    private void OnResponseGenerated(object? sender, TerminalResponseEventArgs e) =>
        _ = SendResponseAsync(e.Response);

    private async Task SendResponseAsync(string response)
    {
        try
        {
            await _session.WriteAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
        {
            // The root shell can exit between producing a query and receiving its response.
        }
    }

    /// <summary>
    /// Ends only the agent identity/lifecycle attached to this tab. The root PowerShell session stays
    /// open and becomes an ordinary terminal after the provider CLI returns.
    /// </summary>
    internal async Task<AgentTerminalIdentity?> CompleteAgentProcessAsync()
    {
        var identity = AgentSession;
        if (identity is null)
        {
            return null;
        }

        var lifetime = Interlocked.Exchange(ref _agentSessionLifetime, null);
        if (lifetime is not null)
        {
            await lifetime.DisposeAsync().ConfigureAwait(false);
        }

        AgentSession = null;
        OnPropertyChanged(nameof(AgentSession));
        OnPropertyChanged(nameof(IsAgentSession));
        return identity;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnExited;
        if (_trackedInitialCommand is not null)
        {
            _trackedInitialCommand.InitialCommandCompleted -= OnInitialCommandCompleted;
        }
        Emulator.ResponseGenerated -= OnResponseGenerated;
        try
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await CompleteAgentProcessAsync().ConfigureAwait(false);
        }
    }
}
