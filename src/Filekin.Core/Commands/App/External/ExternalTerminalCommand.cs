namespace Filekin.Core.Commands.App.External;

/// <summary>
/// <c>/ext</c> — escapes to an external process at the current Files folder (UX-DESIGN.md — External
/// Terminal Escape Hatch). Named <c>ext</c> (external) rather than <c>terminal</c> because the command
/// bar already is a terminal-backed surface. With no argument it opens the user's default external
/// terminal; with an argument it launches that program (for example <c>/ext code</c>, <c>/ext wt</c>)
/// as an independent external process whose working directory is this folder.
/// </summary>
public sealed class ExternalTerminalCommand : ExternalLauncherCommand
{
    public ExternalTerminalCommand(IExternalLauncher launcher)
        : base(launcher)
    {
    }

    public override string Name => "ext";

    protected override AppCommandResult Execute(string folderPath, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            Launcher.OpenTerminal(folderPath);
            return AppCommandResult.Ok("Opened an external terminal here.");
        }

        var program = arguments[0];
        var programArguments = arguments.Skip(1).ToArray();
        Launcher.OpenExternal(folderPath, program, programArguments);
        return AppCommandResult.Ok($"Launched {program} externally here.");
    }
}
