namespace Filekin.Core.Commands;

/// <summary>
/// Decides whether a shell invocation is a known interactive tool that should open in a terminal
/// session. Rules are deterministic and may be argument-sensitive; they must not use runtime
/// heuristics or AI to guess (DECISIONS.md, 2026-08-24 — "Interactive Routing Must Not Depend on AI").
/// This registry is kept independent of the routing logic so its contents can evolve on their own.
/// </summary>
public interface IInteractiveCommandRegistry
{
    /// <param name="executable">The normalized executable name (no path, no extension).</param>
    /// <param name="arguments">The remaining tokens after the executable.</param>
    bool IsInteractive(string executable, IReadOnlyList<string> arguments);
}
