namespace Filekin.Core.Commands.App;

/// <summary>
/// A single built-in application (<c>/</c>) command. Application commands execute as structured
/// app-owned behavior rather than being rewritten into PowerShell (DECISIONS.md, 2026-08-24 —
/// "Application Commands Are Not PowerShell Translations"). Implementations validate their own
/// argument grammar and return an <see cref="AppCommandResult"/> instead of throwing for ordinary
/// user errors.
/// </summary>
public interface IAppCommand
{
    /// <summary>The command name without the leading slash, lower-cased (for example <c>copy</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Additional lower-cased names that invoke this same command. Aliases exist only where the
    /// owner has confirmed that several words name one operation; they are not a general synonym
    /// mechanism, and each alias must be registered exactly once across all commands.
    /// </summary>
    IReadOnlyList<string> Aliases => [];

    Task<AppCommandResult> ExecuteAsync(AppCommandContext context, CancellationToken cancellationToken = default);
}
