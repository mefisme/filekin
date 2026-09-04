using System.IO;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// The <c>/projects</c> rich view: every folder agents have been set up in, on one screen. It is a
/// reading surface. It starts nothing, stops nothing, and opts no folder in; the only thing a row
/// does is take the user to that folder's own Agent Control Room.
/// </summary>
public sealed partial class ShellViewModel
{
    private bool _isAgentProjectsOpen;
    private bool _hasAgentProjects;
    private IReadOnlyList<AgentProjectRowViewModel> _agentProjects = [];
    private string _agentProjectsStatus = string.Empty;

    /// <summary>Whether the agent-project list is showing over the Files hierarchy.</summary>
    public bool IsAgentProjectsOpen
    {
        get => _isAgentProjectsOpen;
        private set
        {
            if (SetProperty(ref _isAgentProjectsOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>
    /// Whether any folder has ever been set up for agents. The sidebar entry appears only then: a
    /// surface that can only ever say "nothing here" is one more thing to read and no help at all.
    /// </summary>
    public bool HasAgentProjects
    {
        get => _hasAgentProjects;
        private set
        {
            if (SetProperty(ref _hasAgentProjects, value))
            {
                ShowSurfaces();
            }
        }
    }

    public IReadOnlyList<AgentProjectRowViewModel> AgentProjects
    {
        get => _agentProjects;
        private set => SetProperty(ref _agentProjects, value);
    }

    /// <summary>The count line beside the heading, in the same slot as the Places and Drives counts.</summary>
    public string AgentProjectsStatus
    {
        get => _agentProjectsStatus;
        private set => SetProperty(ref _agentProjectsStatus, value);
    }

    /// <summary>Opens the agent-project list and reads it fresh.</summary>
    public async Task OpenAgentProjectsAsync(CancellationToken cancellationToken = default)
    {
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();
        IsFilesWorkspaceSelected = true;
        IsAgentProjectsOpen = true;
        await RefreshAgentProjectsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Closes the list and returns to the preserved Files hierarchy.</summary>
    public void CloseAgentProjects() => IsAgentProjectsOpen = false;

    /// <summary>
    /// Asks whether any project exists at all, without opening the coordination runtime. Startup
    /// calls this so the sidebar is right before anything has touched an agent.
    /// </summary>
    public async Task ReadAgentProjectCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            HasAgentProjects = await SqliteAgentProjectStore
                .AnyProjectAsync(SqliteAgentProjectStore.DefaultDatabasePath, cancellationToken)
                .ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A state database that will not answer simply hides the entry.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            HasAgentProjects = false;
        }
    }

    /// <summary>
    /// Re-reads every project and asks what is actually running in each one. Running is never read
    /// from stored state: a session this window owns, a CLI tab this window opened, and a background
    /// session still alive out of sight are the three ways an agent can be running, and all three are
    /// answered live.
    /// </summary>
    public async Task<bool> RefreshAgentProjectsAsync(CancellationToken cancellationToken = default)
    {
        // `/projects` remains a read even when invoked directly before anybody has set up a project.
        // Constructing AgentCoordinationRuntime would initialize state.db, so use the store's
        // non-creating existence check first and stop here when there is nothing to list.
        if (!await SqliteAgentProjectStore
                .AnyProjectAsync(SqliteAgentProjectStore.DefaultDatabasePath, cancellationToken)
                .ConfigureAwait(true))
        {
            HasAgentProjects = false;
            AgentProjects = [];
            AgentProjectsStatus = CountLine([]);
            return true;
        }

        IReadOnlyList<AgentProjectState> projects;
        try
        {
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            projects = await runtime.ListProjectsAsync(cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A list that cannot be read is a status line, never a crashed shell.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            AgentProjects = [];
            AgentProjectsStatus = $"The project list could not be read: {exception.Message}";
            return true;
        }

        HasAgentProjects = projects.Count > 0;
        var now = DateTimeOffset.UtcNow;
        var rows = new List<AgentProjectRowViewModel>(projects.Count);
        foreach (var project in projects.OrderBy(project => project.FolderPath, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new AgentProjectRowViewModel(project, RunningProvidersHere(project), [], now));
        }

        AgentProjects = rows;
        AgentProjectsStatus = CountLine(rows);

        // The out-of-sight answer costs a call to the tool per folder, so it lands second and updates
        // the rows it changes. The list is readable immediately either way.
        _ = RefreshRunningProjectsAsync(projects, cancellationToken);
        return true;
    }

    /// <summary>
    /// Removes one project after the caller has already confirmed with the user. Refuses when any
    /// session for this project might still be running, checked live here and never from stored
    /// connection state — that is the one fact this irreversible action must never get wrong. Nothing
    /// on the folder itself is touched; only Filekin's own coordination memory is deleted.
    /// </summary>
    public async Task RemoveAgentProjectAsync(
        AgentProjectRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var (_, message) = await RemoveAgentProjectCoreAsync(row.FolderPath, cancellationToken)
            .ConfigureAwait(true);
        AgentProjectsStatus = message;
    }

    /// <summary>The <c>/projects remove &lt;folder&gt;</c> command path, run without a prior confirm step.</summary>
    public async Task<CommandExecutionOutcome> RemoveAgentProjectByCommandAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var (severity, message) = await RemoveAgentProjectCoreAsync(folderPath, cancellationToken)
            .ConfigureAwait(true);
        return CommandExecutionOutcome.Inline(severity, message);
    }

    /// <summary>
    /// The shared removal path both surfaces use. Loads the project fresh, refuses while anything might
    /// still be running for it — a live check against `_agentRun`, never the persisted connection state
    /// the row was last drawn from — then deletes it and, if this window has that project's own control
    /// room tab open, closes it so nothing is left pointing at a project that no longer exists.
    /// </summary>
    private async Task<(CommandResultSeverity Severity, string Message)> RemoveAgentProjectCoreAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var folderName = Path.GetFileName(
            folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.Length == 0)
        {
            folderName = folderPath;
        }

        AgentCoordinationRuntime runtime;
        AgentProjectState? project;
        try
        {
            runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            project = await runtime.FindProjectAsync(folderPath, cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A coordination failure is a status line, never a crashed shell.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return (CommandResultSeverity.Error, $"The project list could not be read: {exception.Message}");
        }

        if (project is null)
        {
            return (CommandResultSeverity.Error, $"{folderName} is not an agent project.");
        }

        if (RunningProvidersHere(project).Count > 0)
        {
            return (
                CommandResultSeverity.Error,
                $"{folderName} still has a live session here. End it before removing the project.");
        }

        if (_agentRun is { } run)
        {
            foreach (var provider in Enum.GetValues<AgentProvider>())
            {
                AgentSessionLiveness liveness;
                try
                {
                    liveness = await run
                        .UnwatchedSessionLivenessAsync(project, provider, cancellationToken)
                        .ConfigureAwait(true);
                }
#pragma warning disable CA1031 // A tool that will not answer is not proof that nothing is running.
                catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    liveness = AgentSessionLiveness.Unknown;
                }

                if (liveness != AgentSessionLiveness.NotRunning)
                {
                    return (
                        CommandResultSeverity.Error,
                        $"{folderName} still has a live or unreachable session. " +
                        "End it before removing the project.");
                }
            }
        }

        bool removed;
        try
        {
            removed = await runtime.RemoveProjectAsync(project.Id, cancellationToken).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            return (CommandResultSeverity.Error, exception.Message);
        }

        if (!removed)
        {
            return (CommandResultSeverity.Error, $"{folderName} is already gone.");
        }

        CloseAgentProjectTabForRemovedFolder(project.FolderPath);

        await RefreshAgentProjectsAsync(cancellationToken).ConfigureAwait(true);
        return (CommandResultSeverity.Success, $"Removed {folderName}. Nothing on the folder itself was touched.");
    }

    /// <summary>
    /// Closes this window's own control-room tab for a folder whose project has just been deleted, if
    /// one was open, so nothing is left pointing at a project that no longer exists — the tab falls
    /// back to Files exactly as any other closed project tab does. Internal so a test can prove that
    /// reset without going through the runtime-owning removal path, which needs a real database.
    /// </summary>
    internal void CloseAgentProjectTabForRemovedFolder(string folderPath)
    {
        var openTab = AgentProjectTabs.FirstOrDefault(tab =>
            string.Equals(tab.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        if (openTab is not null)
        {
            CloseAgentProjectTab(openTab);
        }
    }

    /// <summary>Opens one project's own Agent Control Room, moving Files to that folder first.</summary>
    public async Task OpenAgentProjectAsync(
        AgentProjectRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!Directory.Exists(row.FolderPath))
        {
            AgentProjectsStatus = $"{row.FolderPath} is not there any more.";
            return;
        }

        await NavigateToAsync(row.FolderPath, cancellationToken).ConfigureAwait(true);
        IsAgentProjectsOpen = false;
        await OpenAgentsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>What this window can answer about a project without asking either tool.</summary>
    private HashSet<AgentProvider> RunningProvidersHere(AgentProjectState project)
    {
        var running = new HashSet<AgentProvider>();
        foreach (var live in _agentRun?.LiveSessions() ?? [])
        {
            if (live.ProjectId == project.Id)
            {
                running.Add(live.Provider);
            }
        }

        foreach (var tab in TerminalTabs)
        {
            if (tab.AgentSession is { } identity && identity.ProjectId == project.Id)
            {
                running.Add(identity.Provider);
            }
        }

        return running;
    }

    /// <summary>
    /// Asks the tools themselves which background sessions are still alive, then republishes the rows
    /// that answer changes. A tool that will not answer becomes No answer instead of retaining stale
    /// Running or claiming that nothing is connected.
    /// </summary>
    private async Task RefreshRunningProjectsAsync(
        IReadOnlyList<AgentProjectState> projects,
        CancellationToken cancellationToken)
    {
        if (_agentRun is not { } run)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<AgentProjectRowViewModel>(projects.Count);
        var changed = false;
        foreach (var project in projects.OrderBy(project => project.FolderPath, StringComparer.OrdinalIgnoreCase))
        {
            var running = RunningProvidersHere(project);
            var unknown = new HashSet<AgentProvider>();
            try
            {
                foreach (var provider in Enum.GetValues<AgentProvider>())
                {
                    var liveness = await run
                        .UnwatchedSessionLivenessAsync(project, provider, cancellationToken)
                        .ConfigureAwait(true);
                    if (liveness == AgentSessionLiveness.Running)
                    {
                        changed |= running.Add(provider);
                    }
                    else if (liveness == AgentSessionLiveness.Unknown)
                    {
                        changed |= unknown.Add(provider);
                    }
                }
            }
#pragma warning disable CA1031 // A tool that will not answer is not proof that nothing is running.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                // An unexpected presentation failure is still unknown, never proof of a stop.
                changed |= unknown.Add(AgentProvider.ClaudeCode);
            }

            rows.Add(new AgentProjectRowViewModel(project, running, unknown, now));
        }

        if (!changed || !IsAgentProjectsOpen)
        {
            return;
        }

        AgentProjects = rows;
        AgentProjectsStatus = CountLine(rows);
    }

    private static string CountLine(List<AgentProjectRowViewModel> rows)
    {
        var folders = rows.Count == 1 ? "1 folder" : $"{rows.Count} folders";
        var running = rows.Count(row => row.IsRunning);
        var unknown = rows.Count(row => row.IsConnectionUnknown && !row.IsRunning);
        if (running == 0 && unknown == 0)
        {
            return $"{folders} · none running";
        }

        var facts = new List<string>(2);
        if (running > 0)
        {
            facts.Add($"{running} running");
        }

        if (unknown > 0)
        {
            facts.Add($"{unknown} no answer");
        }

        return $"{folders} · {string.Join(" · ", facts)}";
    }
}
