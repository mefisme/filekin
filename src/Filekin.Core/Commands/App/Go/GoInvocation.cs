namespace Filekin.Core.Commands.App.Go;

/// <summary>A validated <c>/go</c> request containing one absolute filesystem folder path.</summary>
public sealed record GoInvocation(string FolderPath);

/// <summary>The result of parsing and resolving a structured <c>/go</c> command.</summary>
public sealed record GoInvocationParseResult
{
    private GoInvocationParseResult(GoInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public GoInvocation? Invocation { get; }

    public string? Error { get; }

    public static GoInvocationParseResult Success(GoInvocation invocation) => new(invocation, null);

    public static GoInvocationParseResult Fail(string error) => new(null, error);
}
