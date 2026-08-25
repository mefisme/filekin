namespace Filekin.Core.Commands;

/// <summary>
/// How a line of Files command-bar input is dispatched.
/// </summary>
public enum CommandRoute
{
    /// <summary>Application-owned input (a leading <c>/</c> command). Not executed as shell input.</summary>
    AppCommand,

    /// <summary>Ordinary finite shell input executed through the persistent runspace backend.</summary>
    FiniteShell,

    /// <summary>A known interactive tool that opens in a ConPTY-backed terminal session.</summary>
    InteractiveTerminal,
}
