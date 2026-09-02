using Filekin.Core.Commands.App;
using Filekin.Core.Commands.App.Tidy;
using Filekin.Core.Commands.App.Unzip;
using Filekin.Core.Commands.App.Where;
using Filekin.Core.Commands.App.Zip;
using Filekin.Core.Terminal;

namespace Filekin.App.ViewModels;

/// <summary>How strongly a command result reads: informational, a success, or a failure.</summary>
public enum CommandResultSeverity
{
    Info,
    Success,
    Warning,
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
        bool opensRecycleBin = false,
        bool opensPlaces = false,
        bool opensDrives = false,
        bool opensSettings = false,
        bool opensAgents = false,
        bool opensAgentProjects = false,
        IReadOnlyList<TerminalLaunchOutcome>? terminalLaunches = null,
        IReadOnlyList<string>? infoTargets = null,
        WhereInvocation? whereRequest = null,
        UnzipInvocation? unzipRequest = null,
        ZipInvocation? zipRequest = null,
        TidyInvocation? tidyRequest = null,
        AppCommandExecutionDetail? appCommandExecution = null)
    {
        InfoTargets = infoTargets;
        WhereRequest = whereRequest;
        UnzipRequest = unzipRequest;
        ZipRequest = zipRequest;
        TidyRequest = tidyRequest;
        AppCommandExecution = appCommandExecution;
        Display = display;
        Severity = severity;
        Text = text;
        FullOutput = fullOutput;
        NewFolderPath = newFolderPath;
        RefreshListing = refreshListing;
        OpensRecycleBin = opensRecycleBin;
        OpensPlaces = opensPlaces;
        OpensDrives = opensDrives;
        OpensSettings = opensSettings;
        OpensAgents = opensAgents;
        OpensAgentProjects = opensAgentProjects;
        TerminalLaunches = terminalLaunches ?? [];
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

    /// <summary>Whether the command opens the system-folder view (<c>/places</c>).</summary>
    public bool OpensPlaces { get; }

    /// <summary>Whether the command opens the assigned-drive view (<c>/drives</c>).</summary>
    public bool OpensDrives { get; }

    /// <summary>Whether the command opens the Settings surface (<c>/settings</c>).</summary>
    public bool OpensSettings { get; }

    /// <summary>Whether the command opens the agent project surface (<c>/agents</c>).</summary>
    public bool OpensAgents { get; }

    /// <summary>Whether the command opens the list of every agent project (<c>/projects</c>).</summary>
    public bool OpensAgentProjects { get; }

    /// <summary>Hosted sessions created by this command; multi-target <c>/run</c> may create several.</summary>
    public IReadOnlyList<TerminalLaunchOutcome> TerminalLaunches { get; }

    /// <summary>The paths <c>/info</c> should describe, or <c>null</c> when this is not <c>/info</c>.</summary>
    public IReadOnlyList<string>? InfoTargets { get; }

    /// <summary>The single-query <c>/where</c> request, or <c>null</c> for every other command.</summary>
    public WhereInvocation? WhereRequest { get; }

    /// <summary>The validated <c>/unzip</c> request, or <c>null</c> when this is not <c>/unzip</c>.</summary>
    public UnzipInvocation? UnzipRequest { get; }

    /// <summary>The validated <c>/zip</c> request, or <c>null</c> when this is not <c>/zip</c>.</summary>
    public ZipInvocation? ZipRequest { get; }

    /// <summary>The parsed <c>/tidy</c> request, when the line was one.</summary>
    public TidyInvocation? TidyRequest { get; }

    /// <summary>The parsed identity and filesystem result of a common app command, when applicable.</summary>
    public AppCommandExecutionDetail? AppCommandExecution { get; }

    public static CommandExecutionOutcome Inline(
        CommandResultSeverity severity,
        string text,
        bool refreshListing = false,
        string? newFolderPath = null,
        AppCommandExecutionDetail? appCommandExecution = null) =>
        new(
            CommandResultDisplay.Inline,
            severity,
            text,
            null,
            newFolderPath,
            refreshListing,
            appCommandExecution: appCommandExecution);

    /// <summary>Appends an honest secondary warning without changing any execution behavior.</summary>
    public CommandExecutionOutcome AppendText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new CommandExecutionOutcome(
            Display,
            Severity,
            $"{Text} {text}",
            FullOutput,
            NewFolderPath,
            RefreshListing,
            OpensRecycleBin,
            OpensPlaces,
            OpensDrives,
            OpensSettings,
            OpensAgents,
            OpensAgentProjects,
            TerminalLaunches,
            InfoTargets,
            WhereRequest,
            UnzipRequest,
            ZipRequest,
            TidyRequest,
            AppCommandExecution);
    }

    public static CommandExecutionOutcome Summary(
        CommandResultSeverity severity,
        string label,
        string fullOutput,
        bool refreshListing = false,
        string? newFolderPath = null) =>
        new(CommandResultDisplay.Summary, severity, label, fullOutput, newFolderPath, refreshListing);

    public static CommandExecutionOutcome Notice(string text, string? newFolderPath = null) =>
        new(CommandResultDisplay.Notice, CommandResultSeverity.Info, text, null, newFolderPath, refreshListing: false);

    /// <summary>The <c>/go</c> command: the Files location itself is the feedback.</summary>
    public static CommandExecutionOutcome Navigate(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: folderPath,
            refreshListing: false);
    }

    /// <summary>The <c>/recycle</c> command: no result line, just open the Recycle Bin view.</summary>
    public static CommandExecutionOutcome RecycleBin() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensRecycleBin: true);

    /// <summary>The <c>/places</c> command: no result line, just open the Places rich view.</summary>
    public static CommandExecutionOutcome Places() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensPlaces: true);

    /// <summary>The <c>/drives</c> command: no result line, just open the Drives rich view.</summary>
    public static CommandExecutionOutcome Drives() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensDrives: true);

    /// <summary>The <c>/settings</c> command: no result line, just open the Settings surface.</summary>
    public static CommandExecutionOutcome Settings() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensSettings: true);

    /// <summary>The <c>/agents</c> command: no result line, just open the agent project surface.</summary>
    public static CommandExecutionOutcome Agents() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensAgents: true);

    /// <summary>The <c>/projects</c> command: no result line, just open the agent project list.</summary>
    public static CommandExecutionOutcome AgentProjects() =>
        new(CommandResultDisplay.None, CommandResultSeverity.Info, string.Empty, null, null, refreshListing: false, opensAgentProjects: true);

    public static CommandExecutionOutcome Terminal(ITerminalSession session, string title)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            terminalLaunches: [new TerminalLaunchOutcome(session, title)]);
    }

    /// <summary>The <c>/info</c> command: no result line, just open the Info sheet on these targets.</summary>
    public static CommandExecutionOutcome Info(IReadOnlyList<string> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            infoTargets: targets);
    }

    /// <summary>The <c>/where</c> command opens its progressive discovery view immediately.</summary>
    public static CommandExecutionOutcome Where(WhereInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            whereRequest: request);
    }

    /// <summary>
    /// The <c>/unzip</c> command: no result line here. Planning reads the archive, which is I/O, so
    /// the view model does that off the UI thread and then opens the preview.
    /// </summary>
    public static CommandExecutionOutcome Unzip(UnzipInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            unzipRequest: request);
    }

    /// <summary>The <c>/zip</c> command, on the same terms as <see cref="Unzip"/>.</summary>
    public static CommandExecutionOutcome Zip(ZipInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            zipRequest: request);
    }

    /// <summary>
    /// The <c>/tidy</c> command, on the same terms as <see cref="Unzip"/>: listing the folder and
    /// probing every destination is I/O, so the view model plans off the UI thread and then decides
    /// between the preview and an immediate run.
    /// </summary>
    public static CommandExecutionOutcome Tidy(TidyInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CommandExecutionOutcome(
            CommandResultDisplay.None,
            CommandResultSeverity.Info,
            string.Empty,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            tidyRequest: request);
    }

    public static CommandExecutionOutcome RunResult(
        CommandResultSeverity severity,
        string text,
        IReadOnlyList<TerminalLaunchOutcome> terminalLaunches)
    {
        ArgumentNullException.ThrowIfNull(terminalLaunches);
        return new CommandExecutionOutcome(
            string.IsNullOrEmpty(text) ? CommandResultDisplay.None : CommandResultDisplay.Inline,
            severity,
            text,
            fullOutput: null,
            newFolderPath: null,
            refreshListing: false,
            terminalLaunches: terminalLaunches);
    }
}
