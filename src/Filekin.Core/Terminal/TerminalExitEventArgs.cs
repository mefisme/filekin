namespace Filekin.Core.Terminal;

/// <summary>
/// Raised when the root shell process of a hosted terminal session exits. When this
/// fires the session is over; the owning surface should close the terminal tab.
/// </summary>
public sealed class TerminalExitEventArgs : EventArgs
{
    public TerminalExitEventArgs(int exitCode)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
