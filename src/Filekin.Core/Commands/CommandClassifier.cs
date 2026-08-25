namespace Filekin.Core.Commands;

/// <summary>
/// Default command-bar classifier. A leading <c>/</c> is application-owned; everything else is
/// shell input, routed to a terminal only when the interactive registry matches the invocation.
/// </summary>
/// <remarks>
/// Tokenization is a simple whitespace split, matching the validated spike. Quote-aware argument
/// parsing is deliberately out of scope for classification; the raw input is still what the shell
/// or terminal ultimately executes.
/// </remarks>
public sealed class CommandClassifier : ICommandClassifier
{
    private readonly IInteractiveCommandRegistry _registry;

    public CommandClassifier(IInteractiveCommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public CommandClassification Classify(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var trimmed = input.Trim();
        if (trimmed.StartsWith('/'))
        {
            return new CommandClassification(CommandRoute.AppCommand, Executable: null);
        }

        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return new CommandClassification(CommandRoute.FiniteShell, Executable: null);
        }

        var executable = NormalizeExecutable(tokens[0]);
        var arguments = tokens.Length > 1 ? tokens[1..] : [];

        var route = _registry.IsInteractive(executable, arguments)
            ? CommandRoute.InteractiveTerminal
            : CommandRoute.FiniteShell;

        return new CommandClassification(route, executable);
    }

    private static string NormalizeExecutable(string token)
    {
        // Strip any directory and extension so C:\Python\python.exe and python both match.
        var name = Path.GetFileNameWithoutExtension(token);
        return string.IsNullOrEmpty(name) ? token : name;
    }
}
