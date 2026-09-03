using Filekin.Core.Terminal;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// A terminal session with no ConPTY behind it. The tab view model only needs something that can be
/// subscribed to and disposed, so reattachment can be checked without starting a shell.
/// </summary>
internal sealed class FakeTerminalSession : ITerminalSession
{
    public int RootProcessId => 0;

    public bool HasExited { get; private set; }

    public int? ExitCode => HasExited ? 0 : null;

#pragma warning disable CS0067 // Nothing in these tests produces output or exits by itself.
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    public event EventHandler<TerminalExitEventArgs>? Exited;
#pragma warning restore CS0067

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask WriteAsync(string text, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public void Resize(TerminalSize size)
    {
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        HasExited = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A directory lister that reads nothing, so a shell can be built without touching a disk.</summary>
internal sealed class FakeDirectoryLister : Filekin.Core.FileSystem.IDirectoryLister
{
    public IReadOnlyList<Filekin.Core.FileSystem.DirectoryEntry> List(string path) => [];
}
