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
/// There are no switches. Everything else <c>/zip</c> could decide is offered by the preview
/// instead — see <see cref="ZipInvocation"/> for why the two archive commands differ here.
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

        foreach (var argument in command.Arguments)
        {
            if (IsSwitch(argument))
            {
                return ZipInvocationParseResult.Fail(
                    $"/zip takes items and an optional name, not switches. Remove {argument}.");
            }
        }

        var positional = command.Arguments;
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

        return ZipInvocationParseResult.Success(new ZipInvocation(sources, output));
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
