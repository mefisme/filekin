using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Info;

/// <summary>
/// Parses <c>/info</c> before the ordinary shell-quoting reference pass, for the same reason
/// <c>/run</c> does: a multi-item <c>@selection</c> must stay several targets rather than collapsing
/// into one quoted string.
///
/// Bare <c>/info</c> describes the current selection, or the visible folder when nothing is selected
/// (DECISIONS.md, 2026-08-27), so the most common inspection is one word.
/// </summary>
public sealed class InfoInvocationParser
{
    private readonly IReferenceResolver _references;

    public InfoInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public InfoInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!AppCommandParser.TryParse(input, out var command) ||
            !command.Name.Equals("info", StringComparison.OrdinalIgnoreCase))
        {
            return InfoInvocationParseResult.Fail("Enter /info followed by a file or folder.");
        }

        if (command.Arguments.Count == 0)
        {
            return ParseImplicitTarget(context);
        }

        var targets = new List<string>();
        foreach (var argument in command.Arguments)
        {
            foreach (var path in Expand(argument, context))
            {
                targets.Add(path);
            }
        }

        return targets.Count == 0
            ? InfoInvocationParseResult.Fail("That target resolves to no items.")
            : InfoInvocationParseResult.Success(new InfoInvocation(targets));
    }

    private static InfoInvocationParseResult ParseImplicitTarget(ReferenceContext context)
    {
        if (context.Selection.Count > 0)
        {
            return InfoInvocationParseResult.Success(new InfoInvocation([.. context.Selection]));
        }

        return context.CurrentFolderPath is { Length: > 0 } folder
            ? InfoInvocationParseResult.Success(new InfoInvocation([folder]))
            : InfoInvocationParseResult.Fail("Select something, or open a folder, then run /info.");
    }

    private IReadOnlyList<string> Expand(string token, ReferenceContext context)
    {
        var resolution = _references.ResolveToken(token, context);
        if (resolution.IsKnownReference)
        {
            return resolution.Paths;
        }

        // A literal target is relative to the visible folder, the same rule /run uses.
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
