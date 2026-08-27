namespace Filekin.Core.Commands.Completion;

/// <summary>
/// Locates and edits the deliberately small language Filekin owns in the Files command bar:
/// the leading application-command token and recognized workspace-reference tokens. It does not
/// claim ordinary shell text or filesystem-target completion.
/// </summary>
public static class CommandCompletion
{
    private static readonly char[] ReferenceBoundaries = [' ', '\t', '\r', '\n', '"', '\'', '(', ',', '=', '|', '&', ';'];

    public static CommandCompletionMatch? Find(
        string input,
        int caretIndex,
        IEnumerable<CommandCompletionSuggestion> catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegative(caretIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretIndex, input.Length);

        var tokenStart = FindTokenStart(input, caretIndex);
        if (tokenStart < 0)
        {
            return null;
        }

        var sigil = input[tokenStart];
        if (sigil == '/' && !IsLeadingCommandToken(input, tokenStart))
        {
            return null;
        }

        if (sigil == '@' && tokenStart > 0 && !ReferenceBoundaries.Contains(input[tokenStart - 1]))
        {
            return null;
        }

        var tokenEnd = tokenStart + 1;
        while (tokenEnd < input.Length && IsNameCharacter(input[tokenEnd]))
        {
            tokenEnd++;
        }

        // Completion owns only the name portion. Once the caret is in an @reference subpath or in
        // command arguments, the text belongs to the command/shell again.
        if (caretIndex > tokenEnd)
        {
            return null;
        }

        var prefix = input[tokenStart..caretIndex];
        var suggestions = catalog
            .Where(candidate => candidate.Text.Length > 0 &&
                candidate.Text[0] == sigil &&
                candidate.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static candidate => candidate.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return suggestions.Length == 0
            ? null
            : new CommandCompletionMatch(tokenStart, tokenEnd - tokenStart, prefix, suggestions);
    }

    public static string CommonPrefix(IReadOnlyList<CommandCompletionSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (suggestions.Count == 0)
        {
            return string.Empty;
        }

        var prefix = suggestions[0].Text;
        for (var index = 1; index < suggestions.Count && prefix.Length > 0; index++)
        {
            var candidate = suggestions[index].Text;
            var sharedLength = 0;
            var limit = Math.Min(prefix.Length, candidate.Length);
            while (sharedLength < limit &&
                   char.ToUpperInvariant(prefix[sharedLength]) == char.ToUpperInvariant(candidate[sharedLength]))
            {
                sharedLength++;
            }

            prefix = prefix[..sharedLength];
        }

        return prefix;
    }

    public static CommandCompletionEdit Apply(
        string input,
        CommandCompletionMatch match,
        CommandCompletionSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(suggestion);

        if (match.TokenStart + match.TokenLength > input.Length)
        {
            throw new ArgumentException("The completion token is outside the input.", nameof(match));
        }

        var completed = string.Concat(
            input.AsSpan(0, match.TokenStart),
            suggestion.Text,
            input.AsSpan(match.TokenStart + match.TokenLength));
        return new CommandCompletionEdit(completed, match.TokenStart + suggestion.Text.Length);
    }

    private static int FindTokenStart(string input, int caretIndex)
    {
        if (caretIndex == 0)
        {
            return -1;
        }

        var index = caretIndex - 1;
        while (index >= 0 && IsNameCharacter(input[index]))
        {
            index--;
        }

        if (index >= 0 && (input[index] == '/' || input[index] == '@'))
        {
            return index;
        }

        // A bare sigil has no name characters to scan backwards through.
        return input[caretIndex - 1] is '/' or '@' ? caretIndex - 1 : -1;
    }

    private static bool IsLeadingCommandToken(string input, int tokenStart) =>
        input.AsSpan(0, tokenStart).Trim().Length == 0;

    private static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '-';
}
