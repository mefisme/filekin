using Filekin.Core.Shell;

namespace Filekin.Core.Commands.App;

/// <summary>
/// The execution context handed to an <see cref="IAppCommand"/>. It carries the parsed command and
/// the current Files location so relative targets resolve against the visible folder (DECISIONS.md,
/// 2026-08-24 — "Relative targets in app-owned commands resolve against the current Files
/// location"). Resolved <c>@</c> references are expected to be expanded into the argument list by a
/// separate reference-resolution pass before dispatch; this context intentionally does not itself
/// perform reference resolution.
/// </summary>
public sealed record AppCommandContext
{
    public AppCommandContext(ShellLocation currentLocation, ParsedAppCommand command)
    {
        ArgumentNullException.ThrowIfNull(currentLocation);
        ArgumentNullException.ThrowIfNull(command);

        CurrentLocation = currentLocation;
        Command = command;
    }

    public ShellLocation CurrentLocation { get; }

    public ParsedAppCommand Command { get; }
}
