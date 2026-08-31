using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// The <c>/agents</c> surface: one adaptive rich view for the current Files folder (DECISIONS.md,
/// 2026-08-31). A folder that has not opted in shows setup; a folder that has shows the control room.
///
/// Coordination is lazy and strictly opt-in, so nothing here runs until the user types the command:
/// the coordination database is opened on first use, and only <see cref="SetUpAgentProjectAsync"/>
/// creates state. Opening the surface starts no agent, probes no provider, and writes no project file.
/// </summary>
public sealed partial class ShellViewModel
{
    // Safe implementation defaults, not a settled product decision: the conservative percentage to
    // ship is still an open product question awaiting live provider validation (FEATURES.md).
    private static readonly AgentCoordinationPolicy AgentPolicy = new(
        MinimumRemainingPercent: 10,
        HandoffRequestRemainingPercent: 30,
        MaximumUsageAge: TimeSpan.FromMinutes(5));

    private AgentCoordinationRuntime? _agentRuntime;
    private SqliteAgentProjectStore? _agentStore;
    private AgentProjectState? _agentProject;

    private bool _isAgentsOpen;
    private bool _isAgentsBusy;
    private string _agentsFolderPath = string.Empty;
    private string _agentsObjective = string.Empty;
    private string _agentsStatus = string.Empty;

    /// <summary>Both agents, in a stable order, or empty while the folder has not opted in.</summary>
    public ObservableCollection<AgentParticipantViewModel> AgentParticipants { get; } = [];

    /// <summary>Whether the agent surface is showing over the Files hierarchy.</summary>
    public bool IsAgentsOpen
    {
        get => _isAgentsOpen;
        private set
        {
            if (SetProperty(ref _isAgentsOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    public string AgentsTitle =>
        $"Agents — {Path.GetFileName(_agentsFolderPath.TrimEnd(Path.DirectorySeparatorChar))}";

    public string AgentsFolderPath
    {
        get => _agentsFolderPath;
        private set
        {
            if (SetProperty(ref _agentsFolderPath, value))
            {
                OnPropertyChanged(nameof(AgentsTitle));
            }
        }
    }

    /// <summary>Whether this folder is already an agent project, which decides what the surface shows.</summary>
    public bool IsAgentProjectSetUp => _agentProject is not null;

    public bool IsAgentSetupVisible => !IsAgentProjectSetUp;

    /// <summary>The objective text box: what the user wants the agents to do, in their own words.</summary>
    public string AgentsObjective
    {
        get => _agentsObjective;
        set
        {
            if (SetProperty(ref _agentsObjective, value))
            {
                OnPropertyChanged(nameof(CanSaveAgentObjective));
            }
        }
    }

    /// <summary>The one sentence describing where the project stands, or why it cannot start.</summary>
    public string AgentsStatus
    {
        get => _agentsStatus;
        private set => SetProperty(ref _agentsStatus, value);
    }

    public bool IsAgentsBusy
    {
        get => _isAgentsBusy;
        private set
        {
            if (SetProperty(ref _isAgentsBusy, value))
            {
                OnPropertyChanged(nameof(CanSetUpAgentProject));
                OnPropertyChanged(nameof(CanSaveAgentObjective));
            }
        }
    }

    public bool CanSetUpAgentProject => !_isAgentsBusy && _agentProject is null;

    public bool CanSaveAgentObjective =>
        !_isAgentsBusy &&
        _agentProject is { } project &&
        !string.Equals(project.Objective, _agentsObjective.Trim(), StringComparison.Ordinal);

    /// <summary>What the agents were last asked to do, for the control room.</summary>
    public string AgentObjectiveSummary =>
        _agentProject is { Objective.Length: > 0 } project
            ? project.Objective
            : "No objective yet.";

    /// <summary>The latest structured handoff, or that there has not been one.</summary>
    public string AgentHandoffSummary
    {
        get
        {
            if (_agentProject?.LastHandoff is not { } handoff)
            {
                return "No handoff yet.";
            }

            var from = AgentParticipantViewModel.DisplayName(handoff.From);
            var to = AgentParticipantViewModel.DisplayName(handoff.To);
            return $"{from} → {to}: {handoff.Summary}";
        }
    }

    /// <summary>Opens the agent surface for the current Files folder without opting the folder in.</summary>
    public async Task OpenAgentsAsync(CancellationToken cancellationToken = default)
    {
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();

        AgentsFolderPath = _currentPath ?? string.Empty;
        IsAgentsOpen = true;
        await LoadAgentProjectAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Closes the agent surface and returns to the preserved Files hierarchy. It hides a view and
    /// nothing else: the project, its turn, and any running agent keep going, exactly as a dismissed
    /// archive or tidy run keeps going. Stopping work is always a deliberate, separate action.
    /// </summary>
    public void CloseAgents() => IsAgentsOpen = false;

    /// <summary>
    /// The explicit opt-in. It records the project and the objective; it starts no agent, writes no
    /// instruction file, and grants no turn.
    /// </summary>
    public async Task SetUpAgentProjectAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetUpAgentProject || string.IsNullOrEmpty(_agentsFolderPath))
        {
            return;
        }

        IsAgentsBusy = true;
        try
        {
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await runtime
                .CreateProjectAsync(_agentsFolderPath, _agentsObjective, cancellationToken)
                .ConfigureAwait(true);
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible status line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            AgentsStatus = $"This folder could not be set up: {exception.Message}";
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    /// <summary>Records a changed objective. It changes no turn, no lease, and no provider state.</summary>
    public async Task SaveAgentObjectiveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSaveAgentObjective || _agentProject is not { } project)
        {
            return;
        }

        IsAgentsBusy = true;
        try
        {
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await runtime
                .SetObjectiveAsync(project.Id, _agentsObjective, cancellationToken)
                .ConfigureAwait(true);
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible status line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            AgentsStatus = $"The objective could not be saved: {exception.Message}";
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    private async Task LoadAgentProjectAsync(CancellationToken cancellationToken)
    {
        IsAgentsBusy = true;
        try
        {
            if (string.IsNullOrEmpty(_agentsFolderPath))
            {
                _agentProject = null;
                ShowAgentProject();
                AgentsStatus = "Open a folder in Files first.";
                return;
            }

            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await runtime.FindProjectAsync(_agentsFolderPath, cancellationToken)
                .ConfigureAwait(true);
            AgentsObjective = _agentProject?.Objective ?? string.Empty;
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible status line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _agentProject = null;
            ShowAgentProject();
            AgentsStatus = $"Agent state could not be read: {exception.Message}";
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    /// <summary>Rebuilds every derived part of the surface from the current project snapshot.</summary>
    private void ShowAgentProject()
    {
        AgentParticipants.Clear();
        if (_agentProject is { } project)
        {
            foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
            {
                AgentParticipants.Add(new AgentParticipantViewModel(
                    project.Participant(provider),
                    project.ActiveAgent == provider));
            }
        }

        AgentsStatus = DescribeAgentProject();
        OnPropertyChanged(nameof(IsAgentProjectSetUp));
        OnPropertyChanged(nameof(IsAgentSetupVisible));
        OnPropertyChanged(nameof(CanSetUpAgentProject));
        OnPropertyChanged(nameof(CanSaveAgentObjective));
        OnPropertyChanged(nameof(AgentObjectiveSummary));
        OnPropertyChanged(nameof(AgentHandoffSummary));
    }

    private string DescribeAgentProject()
    {
        if (_agentProject is not { } project)
        {
            return "This folder is not set up for agents yet.";
        }

        var active = project.ActiveAgent is { } owner
            ? AgentParticipantViewModel.DisplayName(owner)
            : null;
        var reason = project.AttentionReason;
        return project.Status switch
        {
            AgentProjectStatus.ClockingIn => "Waiting for both agents to join.",
            AgentProjectStatus.Ready => "Ready. Nobody is working yet.",
            AgentProjectStatus.Working => $"{active} is working.",
            AgentProjectStatus.HandoffPending => $"{active} was asked to hand over.",
            AgentProjectStatus.Paused => reason is null ? "Paused." : $"Paused. {reason}",
            AgentProjectStatus.NeedsAttention => reason is null ? "Needs you." : $"Needs you. {reason}",
            AgentProjectStatus.CompletionPending => $"{active} says the work is done.",
            AgentProjectStatus.Completed => "Finished.",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Builds the coordination runtime on first use and reconciles any lease left by a previous run
    /// before anything else may touch project state.
    /// </summary>
    private async Task<AgentCoordinationRuntime> AgentRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_agentRuntime is { } existing)
        {
            return existing;
        }

        _agentStore = new SqliteAgentProjectStore();
        var runtime = new AgentCoordinationRuntime(
            _agentStore,
            AgentPolicy,
            ResolveMcpCompanionPath());
        await runtime.StartAsync(cancellationToken).ConfigureAwait(true);
        _agentRuntime = runtime;
        return runtime;
    }

    /// <summary>
    /// The packaged MCP companion. A missing companion only matters when coordination actually starts,
    /// so its expected path is still returned and the surface keeps working for reading and setup.
    /// </summary>
    private string ResolveMcpCompanionPath()
    {
        try
        {
            return FilekinMcpExecutableLocator.Resolve();
        }
        catch (FileNotFoundException exception)
        {
            AgentsStatus = "Filekin.Mcp.exe is missing beside Filekin. Repair or reinstall Filekin before starting agents.";
            return exception.FileName ?? Path.Combine(
                AppContext.BaseDirectory,
                FilekinMcpExecutableLocator.ExecutableFileName);
        }
    }

    private async ValueTask DisposeAgentsAsync()
    {
        if (_agentRuntime is { } runtime)
        {
            _agentRuntime = null;
            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        _agentStore?.Dispose();
        _agentStore = null;
    }
}
