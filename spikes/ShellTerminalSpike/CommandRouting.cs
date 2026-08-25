namespace Filekin.ShellTerminalSpike;

internal enum RouteKind
{
    AppOwned,
    FiniteRunspace,
    InteractiveTerminal,
}

internal static class CommandRouting
{
    private static readonly HashSet<string> AlwaysInteractive = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex",
        "claude",
        "pwsh",
        "powershell",
        "ssh",
    };

    public static RouteKind Classify(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith('/'))
        {
            return RouteKind.AppOwned;
        }

        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return RouteKind.FiniteRunspace;
        }

        var executable = Path.GetFileNameWithoutExtension(tokens[0]);
        if (AlwaysInteractive.Contains(executable))
        {
            return RouteKind.InteractiveTerminal;
        }

        // The spike proves one argument-sensitive registry rule. It is not the v1 registry.
        if (executable.Equals("python", StringComparison.OrdinalIgnoreCase) && tokens.Length == 1)
        {
            return RouteKind.InteractiveTerminal;
        }

        return RouteKind.FiniteRunspace;
    }
}
