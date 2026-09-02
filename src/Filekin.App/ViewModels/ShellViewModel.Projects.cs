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
