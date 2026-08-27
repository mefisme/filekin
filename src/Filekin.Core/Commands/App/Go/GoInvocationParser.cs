using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Go;

/// <summary>
/// Parses <c>/go</c> as a command name followed by one folder target. Unlike the shared slash-command
/// tokenizer, the entire remainder is the target, so an ordinary Windows path containing spaces does
/// not need quotes. Quotes remain accepted for users carrying PowerShell habits into Filekin.
/// </summary>
public sealed class GoInvocationParser
{
    private const string Usage = "Usage: /go <folder>";

    private readonly IReferenceResolver _references;

    public GoInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public GoInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryGetTarget(input, out var target))
        {
            return GoInvocationParseResult.Fail(Usage);
        }

        target = RemoveMatchingOuterQuotes(target);
        if (target.Length == 0)
        {
            return GoInvocationParseResult.Fail(Usage);
        }

        var resolution = _references.ResolveToken(target, context);
        if (resolution.IsKnownReference)
        {
            if (resolution.Paths.Count == 0)
            {
                return GoInvocationParseResult.Fail("That reference resolves to no folders.");
            }

            if (resolution.Paths.Count > 1)
            {
                return GoInvocationParseResult.Fail("/go needs one folder, but that reference resolves to several items.");
            }

            target = resolution.Paths[0];
        }

        try
        {
            var fullPath = context.CurrentFolderPath is { Length: > 0 } currentFolder
                ? Path.GetFullPath(target, currentFolder)
                : Path.GetFullPath(target);
            return GoInvocationParseResult.Success(new GoInvocation(fullPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return GoInvocationParseResult.Fail($"'{target}' is not a valid folder path.");
        }
    }

    private static bool TryGetTarget(string input, out string target)
    {
        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith("/go", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > 3 && !char.IsWhiteSpace(trimmed[3])))
        {
            target = string.Empty;
            return false;
        }

        target = trimmed.Length == 3 ? string.Empty : trimmed[3..].Trim();
        return true;
    }

    private static string RemoveMatchingOuterQuotes(string target)
    {
        if (target.Length >= 2 &&
            target[0] is '"' or '\'' &&
            target[^1] == target[0])
        {
            return target[1..^1].Trim();
        }

        return target;
    }
}
