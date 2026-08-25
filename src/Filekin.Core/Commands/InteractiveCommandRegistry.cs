namespace Filekin.Core.Commands;

/// <summary>
/// The built-in v1 interactive-tool registry. It covers the confirmed interactive tools —
/// AI coding agents, explicit shell launches, and SSH — plus one argument-sensitive rule for the
/// Python REPL. It intentionally does not attempt to enumerate every interactive program; unknown
/// commands default to the finite shell path with a later "Run in terminal" escape hatch
/// (DECISIONS.md, 2026-08-24). Version one has no user-defined interactive rules.
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

    public bool IsInteractive(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        if (AlwaysInteractive.Contains(executable))
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
