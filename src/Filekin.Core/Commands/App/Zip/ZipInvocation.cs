using Filekin.Core.Archives;

namespace Filekin.Core.Commands.App.Zip;

/// <summary>
/// A validated <c>/zip</c> request.
///
/// <c>/zip</c> originally took no switches, on the reasoning that its preview made every remaining
/// choice visible. That reasoning could not cover <c>-y</c>, which exists precisely to not show the
/// preview — and because one shared setting governs the preview for both archive commands, wanting
/// <c>/zip</c> without a preview while keeping <c>/unzip</c>'s was inexpressible. <c>-y</c> then
/// requires the collision switches as companions: skipping the preview removes the only surface
/// where that choice was visible (owner decision, 2026-08-27, superseding the same day's
/// no-switches decision).
///
/// <c>-noroot</c> is deliberately still absent. It describes where extracted files land, which is
/// not a question compression asks.
///
/// <see cref="CollisionPolicy"/> and <see cref="SkipPreview"/> are nullable for the reason
/// <see cref="Unzip.UnzipInvocation"/>'s are: <c>null</c> means "the user did not say", so the
/// Settings default applies, and a switch overrides it for that one command without writing it.
/// </summary>
/// <param name="SourcePaths">The files and folders to compress, in the order given.</param>
/// <param name="OutputPath">The archive to write.</param>
/// <param name="CollisionPolicy">An explicit <c>-skip</c> / <c>-overwrite</c>, or <c>null</c> for the setting.</param>
/// <param name="SkipPreview"><c>true</c> for <c>-y</c>, or <c>null</c> for the setting.</param>
public sealed record ZipInvocation(
    IReadOnlyList<string> SourcePaths,
    string OutputPath,
    CollisionPolicy? CollisionPolicy = null,
    bool? SkipPreview = null);

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
