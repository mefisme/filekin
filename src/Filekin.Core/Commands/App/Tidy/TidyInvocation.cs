namespace Filekin.Core.Commands.App.Tidy;

/// <summary>
/// A validated <c>/tidy</c> request.
///
/// <see cref="SkipPreview"/> is nullable for the same reason <c>/unzip</c>'s is: <c>null</c> means
/// "the user did not say", so the Settings default applies. A <c>-y</c> on the command line overrides
/// it for that one command and never writes the stored preference
/// (<c>ShellViewModel.Archive.cs</c> established this precedence; owner decision, 2026-08-27).
/// </summary>
/// <param name="FolderPath">The folder to organize. Always fully qualified.</param>
/// <param name="SkipPreview"><c>true</c> for <c>-y</c>, or <c>null</c> for the setting.</param>
public sealed record TidyInvocation(string FolderPath, bool? SkipPreview);

/// <summary>The result of parsing a structured <c>/tidy</c> command.</summary>
public sealed record TidyInvocationParseResult
{
    private TidyInvocationParseResult(TidyInvocation? invocation, string? error)
    {
        Invocation = invocation;
        Error = error;
    }

    public bool Succeeded => Invocation is not null;

    public TidyInvocation? Invocation { get; }

    public string? Error { get; }

    public static TidyInvocationParseResult Success(TidyInvocation invocation) => new(invocation, null);

    public static TidyInvocationParseResult Fail(string error) => new(null, error);
}
