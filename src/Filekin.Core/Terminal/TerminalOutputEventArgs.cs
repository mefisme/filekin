namespace Filekin.Core.Terminal;

/// <summary>
/// Raw terminal output produced by a hosted session. The payload is the unmodified
/// byte stream from the shell, including VT/ANSI escape sequences. Interpreting or
/// rendering that stream is the responsibility of a terminal surface, not this boundary.
/// </summary>
public sealed class TerminalOutputEventArgs : EventArgs
{
    public TerminalOutputEventArgs(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    public ReadOnlyMemory<byte> Data { get; }
}
