using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Tidy;

/// <summary>
/// Parses <c>/tidy [-y] [&lt;folder&gt;]</c>.
///
/// After an optional leading <c>-y</c>, the entire remainder is one folder target, for the reason
/// <c>/go</c> gives: an ordinary Windows path containing spaces must not need quotes. Matching outer
/// quotes are still accepted.
///
/// Bare <c>/tidy</c> organizes the visible Files folder. That follows the established Filekin pattern
/// where a bare command acts on the current context — <c>/info</c> describes the current selection,
/// bare <c>/unzip</c> extracts what is selected — and the preview stands between the user and the
/// result either way.
/// </summary>
public sealed class TidyInvocationParser
{
    private const string Usage = "Usage: /tidy [-y] [<folder>]";

    private readonly IReferenceResolver _references;

    public TidyInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public TidyInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryGetRemainder(input, out var remainder))
        {
            return TidyInvocationParseResult.Fail(Usage);
        }

        bool? skipPreview = null;
        if (TryTakeSkipSwitch(ref remainder))
        {
            skipPreview = true;
        }

        var target = RemoveMatchingOuterQuotes(remainder);

        if (target.Length == 0)
        {
            return context.CurrentFolderPath is { Length: > 0 } current
                ? TidyInvocationParseResult.Success(new TidyInvocation(current, skipPreview))
                : TidyInvocationParseResult.Fail("Open a filesystem folder, or name a folder to tidy.");
        }

        var resolution = _references.ResolveToken(target, context);
        if (resolution.IsKnownReference)
        {
            if (resolution.Paths.Count == 0)
            {
                return TidyInvocationParseResult.Fail("That reference resolves to no folders.");
            }

            if (resolution.Paths.Count > 1)
            {
                return TidyInvocationParseResult.Fail(
                    "/tidy organizes one folder, but that reference resolves to several items.");
            }

            target = resolution.Paths[0];
        }

        try
        {
            var fullPath = context.CurrentFolderPath is { Length: > 0 } currentFolder
                ? Path.GetFullPath(target, currentFolder)
                : Path.GetFullPath(target);
            return TidyInvocationParseResult.Success(new TidyInvocation(fullPath, skipPreview));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return TidyInvocationParseResult.Fail($"'{target}' is not a valid folder path.");
        }
    }

    private static bool TryGetRemainder(string input, out string remainder)
    {
        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith("/tidy", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > 5 && !char.IsWhiteSpace(trimmed[5])))
        {
            remainder = string.Empty;
            return false;
        }

        remainder = trimmed.Length == 5 ? string.Empty : trimmed[5..].Trim();
        return true;
    }

    /// <summary>
    /// Consumes a leading <c>-y</c> / <c>-yes</c>. Only a leading switch is recognized, because
    /// everything after it is one folder target that may legitimately contain a hyphen.
    /// </summary>
    private static bool TryTakeSkipSwitch(ref string remainder)
    {
        foreach (var form in (string[])["-y", "-yes"])
        {
            if (!remainder.StartsWith(form, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (remainder.Length == form.Length)
            {
                remainder = string.Empty;
                return true;
            }

            if (char.IsWhiteSpace(remainder[form.Length]))
            {
                remainder = remainder[(form.Length + 1)..].Trim();
                return true;
            }
        }

        return false;
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
