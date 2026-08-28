namespace Filekin.Core.Commands.App.Where;

/// <summary>
/// Parses the one application/tool name accepted by <c>/where</c>. The ordinary reference rewrite
/// is deliberately not involved: Where searches for a named program and never turns
/// <c>@selection</c> into several searches (ARCHITECTURE.md Topic 5Q).
/// </summary>
public static class WhereInvocationParser
{
    public static WhereInvocationParseResult Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!AppCommandParser.TryParse(input, out var command) ||
            !command.Name.Equals("where", StringComparison.OrdinalIgnoreCase))
        {
            return WhereInvocationParseResult.Fail("Enter /where followed by one program or tool name.");
        }

        if (command.Arguments.Count == 0)
        {
            return WhereInvocationParseResult.Fail("Enter a program or tool name, for example /where python.");
        }

        if (command.Arguments.Count > 1)
        {
            return WhereInvocationParseResult.Fail(
                "Enter one query. Put a name containing spaces in quotes, for example /where \"Visual Studio Code\".");
        }

        var query = command.Arguments[0].Trim();
        if (query.StartsWith('@'))
        {
            return WhereInvocationParseResult.Fail(
                "/where expects one program or tool name, not a Files reference such as @selection.");
        }

        return query.Length == 0 || !query.Any(char.IsLetterOrDigit)
            ? WhereInvocationParseResult.Fail("Enter a program or tool name, for example /where python.")
            : WhereInvocationParseResult.Success(new WhereInvocation(query));
    }
}
