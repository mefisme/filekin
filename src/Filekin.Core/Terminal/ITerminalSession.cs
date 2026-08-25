namespace Filekin.Core.Terminal;

/// <summary>
/// A live terminal session hosting a root shell process. Input, output, resize, exit
/// notification, and teardown all cross this boundary; the raw VT/ANSI byte stream is
/// surfaced unmodified through <see cref="OutputReceived"/>. This boundary intentionally
/// does not render, so it has no dependency on any UI framework.
/// </summary>
public interface ITerminalSession : IAsyncDisposable
{
    /// <summary>Process id of the root shell.</summary>
    int RootProcessId { get; }

    /// <summary>True once the root shell process has exited.</summary>
    bool HasExited { get; }

    /// <summary>The root shell exit code, or null while it is still running.</summary>
    int? ExitCode { get; }

    /// <summary>Raised for each chunk of raw output produced by the session.</summary>
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <summary>Raised once when the root shell process exits.</summary>
    event EventHandler<TerminalExitEventArgs>? Exited;

    /// <summary>Writes raw bytes to the session input.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Writes UTF-8 encoded text to the session input.</summary>
    ValueTask WriteAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Resizes the pseudoconsole to the requested character dimensions.</summary>
    void Resize(TerminalSize size);

    /// <summary>Completes when the root shell process exits.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
