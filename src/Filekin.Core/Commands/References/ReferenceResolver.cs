using System.Text.RegularExpressions;

namespace Filekin.Core.Commands.References;

/// <summary>
/// The default <see cref="IReferenceResolver"/>. It handles the intrinsic references
/// <c>@thisfolder</c> and <c>@selection</c> from the <see cref="ReferenceContext"/> and delegates any
/// other name to an <see cref="INamedLocationResolver"/> (Windows known folders, user Locations).
///
/// <para>Line resolution is deliberately light-touch: only a token of the form <c>@name</c> at a
/// token boundary — optionally followed by a <c>\subpath</c> or <c>/subpath</c> — is considered, and
/// only when <c>name</c> is a recognized reference. Everything else is copied through verbatim, so
/// PowerShell's own <c>@</c> forms (<c>@()</c>, <c>@{}</c>, <c>@"..."@</c>, and splatting of an
/// unrecognized variable) keep working. Resolved paths are emitted as PowerShell single-quoted
/// literals so paths containing spaces survive; a multi-item <c>@selection</c> expands to several
/// space-separated quoted paths.</para>
/// </summary>
public sealed partial class ReferenceResolver : IReferenceResolver
{
    private const string ThisFolder = "thisfolder";
    private const string Selection = "selection";

    private readonly INamedLocationResolver _namedLocations;

    public ReferenceResolver(INamedLocationResolver namedLocations)
    {
        ArgumentNullException.ThrowIfNull(namedLocations);
        _namedLocations = namedLocations;
    }

    public string ResolveLine(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        return ReferencePattern().Replace(input, match => ReplaceMatch(match, context));
    }

    public ReferenceResolution ResolveReference(string name, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(context);

        var normalized = name.ToLowerInvariant();

        if (normalized == ThisFolder)
        {
            return context.CurrentFolderPath is { } folder
                ? ReferenceResolution.Known([folder])
                : ReferenceResolution.Unknown;
        }

        if (normalized == Selection)
        {
            return ReferenceResolution.Known(context.Selection);
        }

        return _namedLocations.TryResolve(name, out var path)
            ? ReferenceResolution.Known([path])
            : ReferenceResolution.Unknown;
    }

    private string ReplaceMatch(Match match, ReferenceContext context)
    {
        var lead = match.Groups["lead"].Value;
        var name = match.Groups["name"].Value;
        var subPath = match.Groups["sub"].Value;

        var resolution = ResolveReference(name, context);
        if (!resolution.IsKnownReference)
        {
            // Not a workspace reference — leave the original text (including any subpath) untouched.
            return match.Value;
        }

        var paths = resolution.Paths;
        if (subPath.Length > 0)
        {
            var relative = subPath.TrimStart('\\', '/');
            paths = [.. paths.Select(basePath => Path.Combine(basePath, relative))];
        }

        var quoted = string.Join(' ', paths.Select(QuoteForPowerShell));
        return lead + quoted;
    }

    private static string QuoteForPowerShell(string path)
    {
        // PowerShell single-quoted literal: only the single quote is special, escaped by doubling.
        return "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    // A reference token: a boundary (start of string or a shell separator/quote), then @name, then an
    // optional \subpath or /subpath that stops at whitespace, quotes, and shell metacharacters.
    [GeneratedRegex(@"(?<lead>^|[\s""'(,=|&;])@(?<name>[\w-]+)(?<sub>[\\/][^\s""';,|&<>]*)?")]
    private static partial Regex ReferencePattern();
}
