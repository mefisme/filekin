namespace Filekin.App.ViewModels;

/// <summary>How strongly a command result reads: informational, a success, or a failure.</summary>
public enum CommandResultSeverity
{
    Info,
    Success,
    Error,
}

/// <summary>How the command result should occupy space, per the adaptive output model (UX-DESIGN.md).</summary>
public enum CommandResultDisplay
{
    None,
    Inline,
    Summary,
    Notice,
}

/// <summary>
/// The presentation-ready result of running one command-bar line. It tells the view model how much
/// space the result should take (a transient/inline line, a compact summary with expandable output,
/// or an informational notice), whether the Files location changed (a <c>cd</c>), and whether the
/// listing should be refreshed.
/// </summary>
public sealed record CommandExecutionOutcome
{
    private CommandExecutionOutcome(
        CommandResultDisplay display,
        CommandResultSeverity severity,
        string text,
        string? fullOutput,
        string? newFolderPath,
        bool refreshListing,
        bool opensRecycleBin = false)
    {
        Display = display;
        Severity = severity;
        Text = text;
        FullOutput = fullOutput;
        NewFolderPath = newFolderPath;
        RefreshListing = refreshListing;
        OpensRecycleBin = opensRecycleBin;
    }

    public CommandResultDisplay Display { get; }

    public CommandResultSeverity Severity { get; }

    /// <summary>The inline output, summary label, or notice text shown beneath the command bar.</summary>
    public string Text { get; }

    /// <summary>The full output for the expandable region, present only for <see cref="CommandResultDisplay.Summary"/>.</summary>
    public string? FullOutput { get; }

    /// <summary>The filesystem folder to navigate to when a command changed the location, else <c>null</c>.</summary>
    public string? NewFolderPath { get; }

    /// <summary>Whether the current folder should be re-listed because the command may have changed it.</summary>
    public bool RefreshListing { get; }

    /// <summary>Whether the command opens the Recycle Bin view (<c>/recycle</c>).</summary>
    public bool OpensRecycleBin { get; }

    public static CommandExecutionOutcome Inline(
        CommandResultSeverity severity,
        string text,
        bool refreshListing = false,
        string? newFolderPath = null) =>
        new(CommandResultDisplay.Inline, severity, text, null, newFolderPath, refreshListing);

    public static CommandExecutionOutcome Summary(
        CommandResultSeverity severity,
        string label,
        string fullOutput,
        bool refreshListing = false,
        string? newFolderPath = null) =>
        new(CommandResultDisplay.Summary, severity, label, fullOutput, newFolderPath, refreshListing);

    public static CommandExecutionOutcome Notice(string text, string? newFolderPath = null) =>
        new(CommandResultDisplay.Notice, CommandResultSeverity.Info, text, null, newFolderPath, refreshListing: false);

    /// <summary>The <c>/recycle</c> command: no result line, just open the Recycle Bin view.</summary>
    public static CommandExecutionOutcome RecycleBin() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensRecycleBin: true);
}
