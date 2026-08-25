using Filekin.Core.Shell;
using Filekin.Core.Terminal;

namespace Filekin.Core.Commands;

/// <summary>
/// The outcome of routing one line of command-bar input.
/// <list type="bullet">
/// <item><see cref="CommandRoute.AppCommand"/>: <see cref="AppCommandInput"/> holds the raw input for the
/// application command subsystem; nothing was executed here.</item>
/// <item><see cref="CommandRoute.FiniteShell"/>: <see cref="ShellResult"/> holds the finite result. If the
/// command navigated to a non-filesystem provider, <see cref="TerminalSession"/> is the delegated terminal.</item>
/// <item><see cref="CommandRoute.InteractiveTerminal"/>: <see cref="TerminalSession"/> is the started session.</item>
/// </list>
/// </summary>
public sealed record CommandRouterResult
{
    private CommandRouterResult(
        CommandRoute route,
        ShellExecutionResult? shellResult,
        ITerminalSession? terminalSession,
        string? appCommandInput)
    {
        Route = route;
        ShellResult = shellResult;
        TerminalSession = terminalSession;
        AppCommandInput = appCommandInput;
    }

    public CommandRoute Route { get; }

    public ShellExecutionResult? ShellResult { get; }

    public ITerminalSession? TerminalSession { get; }

    public string? AppCommandInput { get; }

    public static CommandRouterResult ForFinite(ShellExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new CommandRouterResult(CommandRoute.FiniteShell, result, terminalSession: null, appCommandInput: null);
    }

    public static CommandRouterResult ForFiniteWithDelegation(ShellExecutionResult result, ITerminalSession delegatedSession)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(delegatedSession);
        return new CommandRouterResult(CommandRoute.FiniteShell, result, delegatedSession, appCommandInput: null);
    }

    public static CommandRouterResult ForTerminal(ITerminalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new CommandRouterResult(CommandRoute.InteractiveTerminal, shellResult: null, session, appCommandInput: null);
    }

    public static CommandRouterResult ForAppCommand(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new CommandRouterResult(CommandRoute.AppCommand, shellResult: null, terminalSession: null, input);
    }
}
