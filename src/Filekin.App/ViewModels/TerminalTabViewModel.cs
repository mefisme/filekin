using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Filekin.Core.Terminal;
using Filekin.Core.Terminal.Emulation;

namespace Filekin.App.ViewModels;

/// <summary>Owns one ConPTY session and its interpreted terminal screen for a workspace tab.</summary>
public sealed class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITerminalSession _session;
    private readonly Dispatcher _dispatcher;
    private int _disposed;
    private bool _isSelected;

    public TerminalTabViewModel(string title, ITerminalSession session, Dispatcher dispatcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Title = title;
        _session = session;
        _dispatcher = dispatcher;
        Emulator = new TerminalEmulator();
        Emulator.ResponseGenerated += OnResponseGenerated;
        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;

        if (_session.HasExited)
        {
            QueueExited(_session.ExitCode ?? -1);
        }
    }

    public event EventHandler<TerminalExitEventArgs>? RootShellExited;

    public string Title { get; }

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnExited;
        Emulator.ResponseGenerated -= OnResponseGenerated;
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
