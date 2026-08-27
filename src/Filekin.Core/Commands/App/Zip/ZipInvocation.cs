namespace Filekin.Core.Commands.App.Zip;

/// <summary>
/// A validated <c>/zip</c> request.
///
/// Deliberately smaller than <see cref="Unzip.UnzipInvocation"/>: sources and a name, nothing else
/// (owner decision, 2026-08-27). <c>/unzip</c> earns its switches because it decides where hundreds
/// of files land, and typing the switch is faster than steering a preview. <c>/zip</c> decides one
/// thing — what the archive is called — and that is already the second argument. The remaining
/// choices, whether a folder keeps its own name inside the archive and what happens if the archive
/// already exists, live in the preview and in Settings, where they can be seen rather than
/// remembered.
/// </summary>
/// <param name="SourcePaths">The files and folders to compress, in the order given.</param>
/// <param name="OutputPath">The archive to write.</param>
public sealed record ZipInvocation(
    IReadOnlyList<string> SourcePaths,
    string OutputPath);

/// <summary>The result of parsing and expanding a structured <c>/zip</c> command.</summary>
public sealed record ZipInvocationParseResult
{
    private ZipInvocationParseResult(ZipInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public ZipInvocation? Invocation { get; }

    public string? Error { get; }

    public static ZipInvocationParseResult Success(ZipInvocation invocation) => new(invocation, null);

    public static ZipInvocationParseResult Fail(string error) => new(null, error);
}
