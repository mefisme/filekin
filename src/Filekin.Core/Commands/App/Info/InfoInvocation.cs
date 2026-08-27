namespace Filekin.Core.Commands.App.Info;

/// <summary>A validated <c>/info</c> request: the absolute paths to describe, in the order given.</summary>
public sealed record InfoInvocation(IReadOnlyList<string> Targets);

/// <summary>The result of parsing and expanding a structured <c>/info</c> command.</summary>
public sealed record InfoInvocationParseResult
{
    private InfoInvocationParseResult(InfoInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public InfoInvocation? Invocation { get; }

    public string? Error { get; }

    public static InfoInvocationParseResult Success(InfoInvocation invocation) => new(invocation, null);

    public static InfoInvocationParseResult Fail(string error) => new(null, error);
}
