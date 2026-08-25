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

    Task<AppCommandResult> ExecuteAsync(AppCommandContext context, CancellationToken cancellationToken = default);
}
