using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Run;

/// <summary>
/// Parses <c>/run</c> before the ordinary shell-quoting reference pass. This preserves the boundary
/// between targets and arguments: <c>@selection</c> may expand to many targets, while references in
/// later tokens expand as process arguments.
/// </summary>
public sealed class RunInvocationParser
{
    private readonly IReferenceResolver _references;

    public RunInvocationParser(IReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references;
    }

    public RunInvocationParseResult Parse(string input, ReferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (!AppCommandParser.TryParse(input, out var command) ||
            !command.Name.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            return RunInvocationParseResult.Fail("Enter /run followed by a file or application.");
        }

        if (command.Arguments.Count == 0)
        {
            return RunInvocationParseResult.Fail("Usage: /run <target> [arguments]");
        }

        var targets = Expand(command.Arguments[0], context);
        if (targets.Count == 0)
        {
            return RunInvocationParseResult.Fail("The run target resolves to no items.");
        }

        if (targets.Count > 1 && command.Arguments.Count > 1)
        {
            return RunInvocationParseResult.Fail("Arguments can be supplied only when /run has one target.");
        }

        var arguments = new List<string>();
        foreach (var argument in command.Arguments.Skip(1))
        {
            arguments.AddRange(Expand(argument, context));
        }

        return RunInvocationParseResult.Success(new RunInvocation(targets, arguments));
    }

    private IReadOnlyList<string> Expand(string token, ReferenceContext context)
    {
        var resolution = _references.ResolveToken(token, context);
        return resolution.IsKnownReference ? resolution.Paths : [token];
    }
}
