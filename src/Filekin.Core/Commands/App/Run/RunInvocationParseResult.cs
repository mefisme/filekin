namespace Filekin.Core.Commands.App.Run;

/// <summary>The result of parsing and expanding a structured <c>/run</c> command.</summary>
public sealed record RunInvocationParseResult
{
    private RunInvocationParseResult(RunInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public RunInvocation? Invocation { get; }

    public string? Error { get; }

    public static RunInvocationParseResult Success(RunInvocation invocation) => new(invocation, null);

    public static RunInvocationParseResult Fail(string error) => new(null, error);
}
