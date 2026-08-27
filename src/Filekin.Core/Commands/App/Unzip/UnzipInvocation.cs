using Filekin.Core.Archives;

namespace Filekin.Core.Commands.App.Unzip;

/// <summary>
/// A validated <c>/unzip</c> request.
///
/// <see cref="CollisionPolicy"/> and <see cref="SkipPreview"/> are nullable on purpose: <c>null</c>
/// means "the user did not say", so the Settings default applies. A switch on the command line is an
/// override for that one command and never changes the stored preference.
/// </summary>
/// <param name="ArchivePaths">The archives to extract, in the order given.</param>
/// <param name="DestinationPath">The folder to extract into. It may not exist yet.</param>
/// <param name="Layout">Whether to create one folder per archive, or extract straight in.</param>
/// <param name="CollisionPolicy">An explicit <c>-skip</c> / <c>-overwrite</c>, or <c>null</c> for the setting.</param>
/// <param name="SkipPreview"><c>true</c> for <c>-y</c>, or <c>null</c> for the setting.</param>
public sealed record UnzipInvocation(
    IReadOnlyList<string> ArchivePaths,
    string DestinationPath,
    UnzipLayout Layout,
    CollisionPolicy? CollisionPolicy,
    bool? SkipPreview);

/// <summary>The result of parsing and expanding a structured <c>/unzip</c> command.</summary>
public sealed record UnzipInvocationParseResult
{
    private UnzipInvocationParseResult(UnzipInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public UnzipInvocation? Invocation { get; }

    public string? Error { get; }

    public static UnzipInvocationParseResult Success(UnzipInvocation invocation) => new(invocation, null);

    public static UnzipInvocationParseResult Fail(string error) => new(null, error);
}
