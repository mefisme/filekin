namespace Filekin.Core.Commands.App;

/// <summary>
/// Parses a line of Files command-bar input that begins with <c>/</c> into a
/// <see cref="ParsedAppCommand"/>. Application commands own their own argument grammar
/// (DECISIONS.md, 2026-08-24 — "Application Commands Are Not PowerShell Translations"), so the
/// tokenizer is deliberately simple and quote-aware rather than deferring to PowerShell: whitespace
/// separates tokens, and either single or double quotes group a token so a filesystem target may
/// contain spaces. A closing quote does not terminate the token, so <c>"a b".txt</c> is one token.
/// </summary>
public static class AppCommandParser
{
    /// <summary>
    /// Attempts to parse <paramref name="input"/> as an application command. Returns <c>false</c>
    /// when the input is not <c>/</c>-prefixed or carries no command name after the slash (for
    /// example <c>"/"</c> or <c>"/   "</c>).
    /// </summary>
    public static bool TryParse(string input, out ParsedAppCommand command)
    {
        ArgumentNullException.ThrowIfNull(input);
        command = null!;

        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return false;
        }

        var tokens = Tokenize(trimmed[1..]);
        if (tokens.Count == 0)
        {
            return false;
        }

        var name = tokens[0].ToLowerInvariant();
        var arguments = tokens.Count > 1 ? tokens.GetRange(1, tokens.Count - 1) : [];
        command = new ParsedAppCommand(name, arguments);
        return true;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inToken = false;
        var quote = '\0';

        foreach (var ch in text)
        {
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                // Opening a quote begins a token even if it turns out to be empty ("").
                quote = ch;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Append(ch);
            inToken = true;
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
