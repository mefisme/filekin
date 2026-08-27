namespace Filekin.Core.Commands.Completion;

/// <summary>A recognized app-owned token and its matching completion candidates.</summary>
public sealed record CommandCompletionMatch
{
    public CommandCompletionMatch(
        int tokenStart,
        int tokenLength,
        string prefix,
        IReadOnlyList<CommandCompletionSuggestion> suggestions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokenStart);
        ArgumentOutOfRangeException.ThrowIfNegative(tokenLength);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suggestions);

        TokenStart = tokenStart;
        TokenLength = tokenLength;
        Prefix = prefix;
        Suggestions = suggestions;
    }

    public int TokenStart { get; }

    public int TokenLength { get; }

    public string Prefix { get; }

    public IReadOnlyList<CommandCompletionSuggestion> Suggestions { get; }
}
