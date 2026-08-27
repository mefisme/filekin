namespace Filekin.Core.Commands.References;

/// <summary>
/// Resolves Filekin workspace <c>@</c> references in Files command-bar input before it reaches the
/// shell or an application command. Recognized references win over PowerShell's own <c>@</c> usage in
/// the command bar (DECISIONS.md, 2026-08-25 — "Known Command-Bar References Win Over PowerShell
/// Splatting"); unrecognized <c>@</c> syntax is left untouched. This preprocessing does not apply to
/// independent terminal tabs.
/// </summary>
public interface IReferenceResolver
{
    /// <summary>
    /// Returns <paramref name="input"/> with every recognized <c>@reference</c> (optionally followed
    /// by a <c>\subpath</c>) replaced by its resolved, shell-quoted path(s). Unrecognized tokens are
    /// preserved exactly.
    /// </summary>
    string ResolveLine(string input, ReferenceContext context);

    /// <summary>Resolves a single reference name (without the leading <c>@</c> or any subpath).</summary>
    ReferenceResolution ResolveReference(string name, ReferenceContext context);

    /// <summary>
    /// Resolves one complete structured-command token such as <c>@projects\tool.exe</c> without
    /// converting paths to shell-quoted text. Unknown or non-reference tokens return Unknown.
    /// </summary>
    ReferenceResolution ResolveToken(string token, ReferenceContext context);
}
