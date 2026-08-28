namespace Filekin.Core.Commands.App.Where;

/// <summary>A validated single-query <c>/where</c> request.</summary>
public sealed record WhereInvocation(string Query);

/// <summary>The result of parsing a structured <c>/where</c> command.</summary>
public sealed record WhereInvocationParseResult
{
    private WhereInvocationParseResult(WhereInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public WhereInvocation? Invocation { get; }

    public string? Error { get; }

    public static WhereInvocationParseResult Success(WhereInvocation invocation) => new(invocation, null);

    public static WhereInvocationParseResult Fail(string error) => new(null, error);
}
