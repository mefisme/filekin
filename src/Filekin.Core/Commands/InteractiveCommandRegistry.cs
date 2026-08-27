namespace Filekin.Core.Commands;

/// <summary>
/// The interactive-tool registry. It covers the confirmed built-in interactive tools — AI coding
/// agents, explicit shell launches, and SSH — plus one argument-sensitive rule for the Python REPL,
/// and any additional program names the user has registered in Settings. It intentionally does not
/// attempt to enumerate every interactive program; unknown commands default to the finite shell path
/// with a later "Run in terminal" escape hatch (DECISIONS.md, 2026-08-24).
///
/// User rules are plain executable names, never heuristics: routing stays deterministic
/// (DECISIONS.md, 2026-08-24 — "Interactive Routing Must Not Depend on AI").
/// </summary>
public sealed class InteractiveCommandRegistry : IInteractiveCommandRegistry
{
    // Always interactive: AI coding agents (claude, codex), explicit shell launches (pwsh,
    // powershell, cmd — DECISIONS.md:297), and remote sessions (ssh).
    private static readonly HashSet<string> AlwaysInteractive = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude",
        "codex",
        "pwsh",
        "powershell",
        "cmd",
        "ssh",
    };

    private static readonly HashSet<string> PythonExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "python",
        "python3",
    };

    // Replaced wholesale rather than mutated so a settings change cannot be observed half-applied by
    // a classification already in flight.
    private HashSet<string> _userPrograms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in names, sorted for display. These can never be removed by the user.</summary>
    public static IReadOnlyList<string> BuiltInPrograms { get; } =
        [.. AlwaysInteractive.Concat(PythonExecutables).OrderBy(static name => name, StringComparer.Ordinal)];

    /// <summary>Whether <paramref name="executable"/> is one of the built-in rules.</summary>
    public static bool IsBuiltIn(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return AlwaysInteractive.Contains(executable) || PythonExecutables.Contains(executable);
    }

    /// <summary>Applies the user's registered program names, replacing any previous set.</summary>
    public void ReplaceUserPrograms(IEnumerable<string> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        _userPrograms = new HashSet<string>(programs, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsInteractive(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        if (AlwaysInteractive.Contains(executable) || _userPrograms.Contains(executable))
        {
            return true;
        }

        // Python is a REPL only when launched with no script or command; `python script.py` is
        // finite (DECISIONS.md, 2026-08-24 — "Interactive Rules May Be Argument-Sensitive").
        if (PythonExecutables.Contains(executable) && arguments.Count == 0)
        {
            return true;
        }

        return false;
    }
}
