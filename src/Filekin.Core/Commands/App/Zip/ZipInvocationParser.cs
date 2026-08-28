using Filekin.Core.Archives;
using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Zip;

/// <summary>
/// Parses <c>/zip &lt;item...&gt; [name.zip]</c>.
///
/// The trailing-argument rule is the inverse of <c>/unzip</c>'s and simpler for it: an argument
/// ending in <c>.zip</c> is the archive being written, anything else is a source. That is
/// unambiguous, so <c>/zip @selection</c>, <c>/zip photos</c>, and
/// <c>/zip photos notes.txt D:\backup\stuff.zip</c> all read the way they look.
///
/// The switches mirror <c>/unzip</c>'s, minus <c>-noroot</c>, which describes where extracted files
/// land and so has no meaning here. See <see cref="ZipInvocation"/> for why <c>/zip</c> gained them.
///
/// <c>/zip</c> is new scope: it appears in no specification document. The owner asked for it on
/// 2026-08-27 alongside <c>/unzip</c>, and it needs a <c>DECISIONS.md</c> entry to become real.
/// </summary>
public sealed class ZipInvocationParser
{
    private const string MissingSourceError = "/zip needs something to compress. Try: /zip @selection";

    private readonly IReferenceResolver _references;

    public ZipInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public ZipInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!AppCommandParser.TryParse(input, out var command) ||
            !command.Name.Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            return ZipInvocationParseResult.Fail(MissingSourceError);
        }

        if (context.CurrentFolderPath is not { Length: > 0 } folder)
        {
            return ZipInvocationParseResult.Fail("Open a filesystem folder, then run /zip.");
        }

        CollisionPolicy? collisions = null;
        bool? skipPreview = null;
        var sawSkip = false;
        var sawOverwrite = false;
        var positional = new List<string>();

        foreach (var argument in command.Arguments)
        {
            if (!IsSwitch(argument))
            {
                positional.Add(argument);
                continue;
            }

            switch (argument.TrimStart('-').ToLowerInvariant())
            {
                case "skip":
                    collisions = CollisionPolicy.Skip;
                    sawSkip = true;
                    break;
                case "overwrite":
                    collisions = CollisionPolicy.Overwrite;
                    sawOverwrite = true;
                    break;
                case "y":
                case "yes":
                    skipPreview = true;
                    break;
                case "noroot":
                    // Named separately from an unknown switch: someone reaching for it has /unzip in
                    // mind, and "not a /zip switch" would not tell them why.
                    return ZipInvocationParseResult.Fail(
                        "-noroot describes where an extraction lands, so it means nothing to /zip.");
                default:
                    return ZipInvocationParseResult.Fail(
                        $"{argument} is not a /zip switch. Use -skip, -overwrite, or -y.");
            }
        }

        if (sawSkip && sawOverwrite)
        {
            return ZipInvocationParseResult.Fail("Use -skip or -overwrite, not both.");
        }
        string? output = null;
        IReadOnlyList<string> sourceTokens = positional;

        if (positional.Count > 0 && ArchiveFormats.IsSupported(positional[^1]))
        {
            var resolved = Expand(positional[^1], context);
            if (resolved.Count != 1)
            {
                return ZipInvocationParseResult.Fail("Name one archive to write.");
            }

            output = resolved[0];
            sourceTokens = [.. positional.Take(positional.Count - 1)];
        }

        var sources = new List<string>();
        foreach (var token in sourceTokens)
        {
            sources.AddRange(Expand(token, context));
        }

        // Bare /zip compresses what is selected, the same shorthand bare /unzip and /info use.
        if (sources.Count == 0)
        {
            if (context.Selection.Count == 0)
            {
                return ZipInvocationParseResult.Fail(MissingSourceError);
            }

            sources.AddRange(context.Selection);
        }

        output ??= ZipPlanner.DefaultOutputPath(sources, folder);

        return ZipInvocationParseResult.Success(
            new ZipInvocation(sources, output, collisions, skipPreview));
    }

    /// <summary>A token is a switch when it starts with <c>-</c> and is not a path, matching <c>/unzip</c>.</summary>
    private static bool IsSwitch(string argument) =>
        argument.Length > 1 &&
        argument[0] == '-' &&
        !argument.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !argument.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private IReadOnlyList<string> Expand(string token, ReferenceContext context)
    {
        var resolution = _references.ResolveToken(token, context);
        if (resolution.IsKnownReference)
        {
            return resolution.Paths;
        }

        try
        {
            return context.CurrentFolderPath is { Length: > 0 } folder
                ? [Path.GetFullPath(token, folder)]
                : [Path.GetFullPath(token)];
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [token];
        }
    }
}
