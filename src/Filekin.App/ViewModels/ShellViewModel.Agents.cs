using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
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

    /// <summary>
    /// Exactly what the owner approves, kept as one sentence so the words shown, the words stored, and
    /// the words checked before a launch can never drift apart.
    /// </summary>
    internal const string SharedFolderApproval =
        "Agents may work in this folder itself instead of a private copy, and Filekin's own helper may "
        + "run so it can read how much usage each agent has left. Each agent may use Filekin's own "
        + "coordination tools without asking you every time; everything else follows the permission "
        + "settings you already chose for that tool.";

    /// <summary>The wider of the two answers, kept beside the narrow one for the same reason.</summary>
    internal const string TrustedFolderApproval = SharedFolderApproval
        + " This folder is safe to work in: an agent may read, write and run things inside it without "
        + "asking first. Anything outside this folder still fails, and Filekin never answers a "
        + "permission question for you.";

    private const string AutomaticChoice = "Whoever has more usage left";

    private readonly AgentModelCatalog _agentModelCatalog = new();
    private readonly Dictionary<AgentProvider, IReadOnlyList<AgentModelChoice>> _agentModels = [];
    private AgentCoordinationRuntime? _agentRuntime;
    private AgentRunService? _agentRun;
    private DispatcherTimer? _agentWatch;
    private SqliteAgentProjectStore? _agentStore;
    private AgentProjectState? _agentProject;

    private bool _isAgentsOpen;
    private bool _isAgentsBusy;
    private string _agentsFolderPath = string.Empty;
    private string _agentsObjective = string.Empty;
    private string _agentsStatus = string.Empty;
    private string _agentChoice = AutomaticChoice;
    private string _lastNotedStatus = string.Empty;
    private string _lastNotedReport = string.Empty;

    /// <summary>Both agents, in a stable order, or empty while the folder has not opted in.</summary>
    public ObservableCollection<AgentParticipantViewModel> AgentParticipants { get; } = [];

    /// <summary>
    /// Opens one exact native session as a persistent read-only task. Opening an existing task only
    /// selects it; it does not restart, resume, or otherwise contact the provider.
    /// </summary>
    public void OpenAgentSession(AgentParticipantViewModel participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (_agentProject is not { } project || participant.NativeSessionId is not { Length: > 0 } nativeSessionId)
        {
            return;
        }

        // The project's session identity is Filekin's own record of what it started, so a session this
        // window is still watching is the same one the participant names.
        var observation = _agentRun?.Session(project.Id, participant.Provider);
        var existing = AgentSessionTabs.FirstOrDefault(session =>
            session.ProjectId == project.Id &&
            session.Provider == participant.Provider &&
            string.Equals(session.NativeSessionId, nativeSessionId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Update(project);
            SelectAgentSession(existing);
            return;
        }

        var session = new AgentSessionViewModel(
            project,
            participant.Provider,
            nativeSessionId,
            observation,
            _dispatcher);
        AgentSessionTabs.Add(session);
        SelectAgentSession(session);
    }

    /// <summary>
    /// What has happened in this project, newest first. A status line that overwrites itself hides the
    /// run it is describing, so every change is kept here instead and nothing is thrown away until the
    /// list is long enough to be a burden.
    /// </summary>
    public ObservableCollection<AgentEventViewModel> AgentEvents { get; } = [];

    /// <summary>Who the user wants to start. Leaving it alone lets Filekin decide.</summary>
    public ObservableCollection<string> AgentChoices { get; } =
        [AutomaticChoice, "Codex", "Claude Code"];

    public string AgentChoice
    {
        get => _agentChoice;
        set => SetProperty(ref _agentChoice, value);
    }

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
                OnPropertyChanged(nameof(CanViewAgentWork));
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

    /// <summary>The plain words the owner is asked to approve before any agent can be started.</summary>
    public static string AgentConsentText => SharedFolderApproval;

    /// <summary>What trusting the folder adds, so the wider answer is never the quiet one.</summary>
    public static string AgentTrustText =>
        "Trust this folder: an agent may read, write and run things inside it without asking you first. "
        + "Anything outside this folder still fails. Filekin never answers a permission question for you, "
        + "and never turns a tool's permission checks off.";

    /// <summary>How the recorded approval reads back, once it exists.</summary>
    public string AgentTrustSummary => _agentProject?.SharedCheckoutConsent?.Trust switch
    {
        SharedFolderTrust.TrustThisFolder => "You trust this folder. Agents work in it without asking.",
        SharedFolderTrust.UseMyOwnSettings =>
            "Your own Codex and Claude settings are in charge. An agent that needs permission waits for you.",
        _ => string.Empty,
    };

    /// <summary>Whether the owner still has to approve working in this folder.</summary>
    public bool IsAgentConsentNeeded => _agentProject is { SharedCheckoutConsent: null };

    public bool IsAgentStartVisible => _agentProject is { SharedCheckoutConsent: not null };

    public bool CanApproveSharedFolder => !_isAgentsBusy && IsAgentConsentNeeded;

    /// <summary>Starting needs an approved folder, an unfinished project, and a free turn.</summary>
    public bool CanStartAgents =>
        !_isAgentsBusy &&
        _agentProject is { SharedCheckoutConsent: not null, Lease: null } project &&
        project.Status != AgentProjectStatus.Completed;

    /// <summary>Stopping and passing the turn need somebody to actually hold it.</summary>
    public bool CanStopAgents => !_isAgentsBusy && _agentProject is { Lease: not null };

    public bool CanPassTheAgentTurn =>
        !_isAgentsBusy &&
        _agentProject is { Lease: not null, Status: AgentProjectStatus.Working };

    /// <summary>Whether the project is asking for a person, which offers the way out of it.</summary>
    public bool CanClearAgentAttention =>
        !_isAgentsBusy &&
        _agentProject is { Lease: null, Status: AgentProjectStatus.NeedsAttention };

    /// <summary>
    /// Whether this project works even when an agent is low on allowance. The safety threshold is a
    /// sensible default, not a wall: without this an agent at eight percent cannot be given the turn
    /// at all, and the work stops for a reason the user never agreed to.
    /// </summary>
    public bool WorkOnLowAllowance
    {
        get => _agentProject?.WorkOnLowAllowance ?? false;
        set
        {
            if (value != WorkOnLowAllowance)
            {
                _ = SetWorkOnLowAllowanceAsync(value);
            }
        }
    }

    public bool CanChooseLowAllowanceWork => !_isAgentsBusy && _agentProject is not null;

    /// <summary>Whether an agent holds the turn, which keeps the command-bar strip visible.</summary>
    public bool IsAgentWorkRunning => _agentProject is { Lease: not null };

    /// <summary>Whether the strip's View button has anywhere to take the user.</summary>
    public bool CanViewAgentWork => IsAgentWorkRunning && !_isAgentsOpen;

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
                OnPropertyChanged(nameof(CanApproveSharedFolder));
                OnPropertyChanged(nameof(CanStartAgents));
                OnPropertyChanged(nameof(CanStopAgents));
                OnPropertyChanged(nameof(CanPassTheAgentTurn));
                OnPropertyChanged(nameof(CanClearAgentAttention));
                OnPropertyChanged(nameof(CanChooseLowAllowanceWork));
            }
        }
    }

    public bool CanSetUpAgentProject => !_isAgentsBusy && _agentProject is null;

    public bool CanSaveAgentObjective =>
        !_isAgentsBusy &&
        _agentProject is { } project &&
        (project.Status == AgentProjectStatus.Completed ||
         !string.Equals(project.Objective, _agentsObjective.Trim(), StringComparison.Ordinal));

    public string AgentObjectiveActionLabel =>
        _agentProject?.Status == AgentProjectStatus.Completed ? "New objective" : "Save";

    /// <summary>What the agents were last asked to do, for the control room.</summary>
    public string AgentObjectiveSummary =>
        _agentProject is { Objective.Length: > 0 } project
            ? project.Objective
            : "No objective yet.";

    /// <summary>
    /// What the agent's own tool last said, in its words. Filekin does not rewrite it, because when a
    /// run produces nothing this sentence is usually the only explanation there is.
    /// </summary>
    public string AgentReport
    {
        get
        {
            if (_agentRun is not { } run || _agentProject is not { } project)
            {
                return string.Empty;
            }

            foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
            {
                if (run.LastReport(project.Id, provider) is { Length: > 0 } report)
                {
                    return $"{AgentParticipantViewModel.DisplayName(provider)}: {report}";
                }
            }

            return string.Empty;
        }
    }

    public bool HasAgentReport => AgentReport.Length > 0;

    /// <summary>The one line the command bar shows while an agent holds the turn.</summary>
    public string AgentWorkSummary
    {
        get
        {
            if (_agentProject is not { Lease: { } lease } project)
            {
                return string.Empty;
            }

            var name = AgentParticipantViewModel.DisplayName(lease.Owner);
            return project.Status switch
            {
                AgentProjectStatus.HandoffPending => $"{name} was asked to hand over.",
                AgentProjectStatus.StopPending => $"{name} was asked to stop.",
                AgentProjectStatus.CompletionPending => $"{name} says the work is done.",
                AgentProjectStatus.NeedsAttention => $"{name} needs you.",
                _ => $"{name} is working.",
            };
        }
    }

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
        await ShowModelChoicesAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Asks each installed tool which models it offers, so the choice is that tool's own list and
    /// never an invented one. A tool that cannot answer simply offers its default.
    /// </summary>
    private async Task ShowModelChoicesAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            if (_agentModels.ContainsKey(provider))
            {
                continue;
            }

            IReadOnlyList<AgentModelChoice> models;
            try
            {
                models = await _agentModelCatalog.ReadAsync(provider, cancellationToken)
                    .ConfigureAwait(true);
            }
#pragma warning disable CA1031 // A tool that cannot list its models is not a broken surface.
            catch (Exception)
#pragma warning restore CA1031
            {
                models = [];
            }

            _agentModels[provider] = models;
            foreach (var row in AgentParticipants.Where(row => row.Provider == provider))
            {
                row.ShowModels(models);
            }
        }
    }

    /// <summary>
    /// Records the model and effort chosen for one agent. It starts nothing, and a session already
    /// running keeps what it started with.
    /// </summary>
    private void ChooseAgentModel(AgentParticipantViewModel participant)
    {
        if (_agentProject is not { } project)
        {
            return;
        }

        _ = SaveAgentModelAsync(project.Id, participant);
    }

    private async Task SaveAgentModelAsync(Guid projectId, AgentParticipantViewModel participant)
    {
        try
        {
            var runtime = await AgentRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
            _agentProject = await runtime
                .ChooseModelAsync(
                    projectId,
                    participant.Provider,
                    participant.ChosenModel,
                    participant.ChosenEffort)
                .ConfigureAwait(true);
            NoteAgentEvent(participant.ChosenModel is null
                ? $"{participant.Name} will use its own default model."
                : $"{participant.Name} will use {participant.ModelSummary}.");
        }
#pragma warning disable CA1031 // A coordination failure is a visible line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            AgentsStatus = $"That model could not be saved: {exception.Message}";
        }
    }

    /// <summary>
    /// Closes the agent surface and returns to the preserved Files hierarchy. It hides a view and
    /// nothing else: the project, its turn, and any running agent keep going, exactly as a dismissed
    /// archive or tidy run keeps going. Stopping work is always a deliberate, separate action.
    /// </summary>
    public void CloseAgents()
    {
        IsAgentsOpen = false;

        // The strip keeps a running turn visible, so the watch keeps running with it.
        WatchAgentProject();
    }

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

    /// <summary>
    /// Records a changed objective, or deliberately opens a completed project for a new job. Neither
    /// path starts a provider; Start work remains the separate action that grants a turn.
    /// </summary>
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
            _agentProject = project.Status == AgentProjectStatus.Completed
                ? await runtime.StartNewObjectiveAsync(project.Id, _agentsObjective, cancellationToken)
                    .ConfigureAwait(true)
                : await runtime.SetObjectiveAsync(project.Id, _agentsObjective, cancellationToken)
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

    /// <summary>
    /// Records the owner's approval to let agents work in this folder itself. It starts no agent and
    /// writes nothing into the folder; it only makes a later start possible.
    /// </summary>
    /// <param name="trust">
    /// How far the approval goes. Trusting the folder lets an agent work inside it without asking;
    /// the other answer leaves each tool's own settings in charge, and an agent that needs permission
    /// waits for the user. Filekin never answers a permission question either way.
    /// </param>
    public async Task ApproveSharedFolderAsync(
        SharedFolderTrust trust,
        CancellationToken cancellationToken = default)
    {
        if (!CanApproveSharedFolder || _agentProject is not { } project)
        {
            return;
        }

        var words = trust == SharedFolderTrust.TrustThisFolder
            ? TrustedFolderApproval
            : SharedFolderApproval;
        await RunAgentActionAsync(
            "This folder could not be approved",
            async runtime => await runtime
                .GrantSharedCheckoutConsentAsync(project.Id, words, trust, cancellationToken)
                .ConfigureAwait(true),
            cancellationToken).ConfigureAwait(true);
        NoteAgentEvent(trust == SharedFolderTrust.TrustThisFolder
            ? "You trusted this folder. Agents can work in it without asking."
            : "You kept your own Codex and Claude settings in charge.");
    }

    /// <summary>
    /// Starts one agent and gives it the turn. This is the only action in Filekin that starts a
    /// provider process, and it cannot run until the owner has approved this folder.
    /// </summary>
    public async Task StartAgentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStartAgents || _agentProject is not { } project)
        {
            return;
        }

        var chosen = ChosenProvider();
        await RunAgentActionAsync(
            "The agent could not be started",
            async _ =>
            {
                var run = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
                return await run.StartAsync(project.Id, chosen, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Asks the working agent to stop. The project is kept, so it can be resumed later. The turn is
    /// released only when that agent's own tool reports the session ended.
    /// </summary>
    public async Task StopAgentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStopAgents || _agentProject is not { } project)
        {
            return;
        }

        await RunAgentActionAsync(
            "The agent could not be asked to stop",
            async _ =>
            {
                var run = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
                return await run.RequestStopAsync(project.Id, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Clears a "needs you" state once the user has read it, so the project can be used again. It is a
    /// separate, deliberate action: Filekin never decides on its own that a problem has been dealt with.
    /// </summary>
    public async Task ClearAgentAttentionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanClearAgentAttention || _agentProject is not { } project)
        {
            return;
        }

        await RunAgentActionAsync(
            "This could not be cleared",
            async runtime => await runtime.ClearAttentionAsync(project.Id, cancellationToken)
                .ConfigureAwait(true),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Ends one agent's sessions in this project, whether or not it holds the turn. A session that has
    /// finished its turn stays open and idle, and keeps its Filekin helper process alive with it, so
    /// this is how a person clears them without going looking for processes.
    /// </summary>
    public async Task StopAgentSessionAsync(
        AgentParticipantViewModel participant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (_isAgentsBusy || _agentProject is not { } project)
        {
            return;
        }

        IsAgentsBusy = true;
        try
        {
            var run = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
            var stopped = await run.StopSessionsAsync(project.Id, participant.Provider, cancellationToken)
                .ConfigureAwait(true);
            NoteAgentEvent(stopped switch
            {
                null => $"{participant.Name} has no session of its own to stop; its sessions end with their turn.",
                0 => $"{participant.Name} has no session open in this folder.",
                1 => $"Asked {participant.Name} to end its session.",
                var many => $"Asked {participant.Name} to end {many} sessions.",
            });
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await runtime.FindProjectAsync(project.FolderPath, cancellationToken)
                .ConfigureAwait(true) ?? _agentProject;
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            AgentsStatus = $"The session could not be stopped: {exception.Message}";
            NoteAgentEvent(AgentsStatus);
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    /// <summary>Asks the working agent to hand over to the other one early.</summary>
    public async Task PassTheAgentTurnAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPassTheAgentTurn || _agentProject is not { } project)
        {
            return;
        }

        await RunAgentActionAsync(
            "The turn could not be passed",
            async _ =>
            {
                var run = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
                return await run.PassTheTurnAsync(project.Id, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Records whether low allowance may still be worked through. Filekin still reads and shows every
    /// number; it never buys usage, never enables metered overage, and never spends a reset credit.
    /// </summary>
    public async Task SetWorkOnLowAllowanceAsync(
        bool allowed,
        CancellationToken cancellationToken = default)
    {
        if (_agentProject is not { } project)
        {
            return;
        }

        await RunAgentActionAsync(
            "This could not be changed",
            async runtime => await runtime
                .SetWorkOnLowAllowanceAsync(project.Id, allowed, cancellationToken)
                .ConfigureAwait(true),
            cancellationToken).ConfigureAwait(true);
        NoteAgentEvent(allowed
            ? "You allowed work to carry on when little usage is left."
            : "You put the usage safety limit back.");
    }

    /// <summary>Brings the agent surface back while work is running, like the tidy progress strip.</summary>
    public void ViewAgentWork()
    {
        if (_agentProject is not null)
        {
            _ = OpenAgentsAsync();
        }
    }

    private AgentProvider? ChosenProvider() => _agentChoice switch
    {
        "Codex" => AgentProvider.Codex,
        "Claude Code" => AgentProvider.ClaudeCode,
        _ => null,
    };

    /// <summary>
    /// Runs one coordination action and shows the new project. A failure is a sentence on the surface,
    /// never a crashed shell, and never a silent retry.
    /// </summary>
    private async Task RunAgentActionAsync(
        string failurePrefix,
        Func<AgentCoordinationRuntime, Task<AgentProjectState>> action,
        CancellationToken cancellationToken)
    {
        IsAgentsBusy = true;
        try
        {
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await action(runtime).ConfigureAwait(true);
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            AgentsStatus = $"{failurePrefix}: {exception.Message}";
            NoteAgentEvent(AgentsStatus);
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    /// <summary>
    /// Adds one line to the running account, unless it repeats the line already at the top. The list
    /// is capped so a long run cannot grow without limit; the oldest lines go first.
    /// </summary>
    private void NoteAgentEvent(string text)
    {
        if (text.Length == 0 ||
            (AgentEvents.Count > 0 &&
             string.Equals(AgentEvents[^1].Text, text, StringComparison.Ordinal)))
        {
            return;
        }

        AgentEvents.Add(new AgentEventViewModel(DateTimeOffset.Now, text));
        while (AgentEvents.Count > 200)
        {
            AgentEvents.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasAgentEvents));
    }

    public bool HasAgentEvents => AgentEvents.Count > 0;

    /// <summary>
    /// Builds the run service on first use. Creating it starts nothing: it is the explicit Start
    /// action that reaches a provider.
    /// </summary>
    private async Task<AgentRunService> AgentRunAsync(CancellationToken cancellationToken)
    {
        if (_agentRun is { } existing)
        {
            return existing;
        }

        var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
        _agentRun = new AgentRunService(
            runtime,
            _agentStore!,
            new AgentProjectCoordinator(AgentPolicy),
            new NativeAgentSessionLauncher());
        return _agentRun;
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
            var found = await runtime.FindProjectAsync(_agentsFolderPath, cancellationToken)
                .ConfigureAwait(true);
            if (found?.Id != _agentProject?.Id)
            {
                AgentEvents.Clear();
                _lastNotedStatus = string.Empty;
                _lastNotedReport = string.Empty;
                OnPropertyChanged(nameof(HasAgentEvents));
            }

            _agentProject = found;
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

    /// <summary>
    /// Keeps the surface honest while an agent works. The agents change the project through their own
    /// MCP calls, so a snapshot goes stale on its own. This only re-reads the coordination database:
    /// it probes no provider and starts nothing.
    /// </summary>
    private void WatchAgentProject()
    {
        var needed = _agentProject is not null &&
            (_isAgentsOpen || IsAgentWorkRunning || AgentSessionTabs.Any(tab => tab.ProjectId == _agentProject.Id));
        if (!needed)
        {
            _agentWatch?.Stop();
            return;
        }

        if (_agentWatch is null)
        {
            _agentWatch = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            _agentWatch.Tick += (_, _) => _ = RefreshAgentProjectAsync();
        }

        _agentWatch.Start();
    }

    /// <summary>
    /// Re-reads the project. A read that fails stops the watch and says so once, rather than retrying
    /// quietly behind a picture that is no longer true. The next action starts it again.
    /// </summary>
    private async Task RefreshAgentProjectAsync()
    {
        if (_isAgentsBusy || _agentProject is not { } project)
        {
            return;
        }

        try
        {
            var runtime = await AgentRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
            var latest = await runtime.FindProjectAsync(project.FolderPath).ConfigureAwait(true);
            if (latest is null)
            {
                return;
            }

            _agentProject = latest;
            ShowAgentProject();
        }
#pragma warning disable CA1031 // A coordination failure is a visible status line, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _agentWatch?.Stop();
            AgentsStatus = $"Agent state could not be re-read: {exception.Message}";
        }
    }

    /// <summary>Rebuilds every derived part of the surface from the current project snapshot.</summary>
    private void ShowAgentProject()
    {
        if (_agentProject is not { } project)
        {
            AgentParticipants.Clear();
        }
        else
        {
            foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
            {
                var row = AgentParticipants.FirstOrDefault(candidate => candidate.Provider == provider);
                if (row is null)
                {
                    row = new AgentParticipantViewModel(
                        project.Participant(provider),
                        project.ActiveAgent == provider,
                        ChooseAgentModel);
                    if (_agentModels.TryGetValue(provider, out var models))
                    {
                        row.ShowModels(models);
                    }

                    AgentParticipants.Add(row);
                }
                else
                {
                    row.Update(project.Participant(provider), project.ActiveAgent == provider);
                }
            }

            foreach (var session in AgentSessionTabs.Where(session => session.ProjectId == project.Id))
            {
                session.Update(project);
            }
        }

        AgentsStatus = DescribeAgentProject();

        // Only real changes are worth a line. The watch re-reads every few seconds, and repeating the
        // same sentence would bury the moment something actually happened.
        if (!string.Equals(_lastNotedStatus, AgentsStatus, StringComparison.Ordinal))
        {
            _lastNotedStatus = AgentsStatus;
            NoteAgentEvent(AgentsStatus);
        }

        var report = AgentReport;
        if (report.Length > 0 && !string.Equals(_lastNotedReport, report, StringComparison.Ordinal))
        {
            _lastNotedReport = report;
            NoteAgentEvent(report);
        }

        WatchAgentProject();
        OnPropertyChanged(nameof(IsAgentConsentNeeded));
        OnPropertyChanged(nameof(IsAgentStartVisible));
        OnPropertyChanged(nameof(CanApproveSharedFolder));
        OnPropertyChanged(nameof(CanStartAgents));
        OnPropertyChanged(nameof(CanStopAgents));
        OnPropertyChanged(nameof(CanPassTheAgentTurn));
        OnPropertyChanged(nameof(CanClearAgentAttention));
        OnPropertyChanged(nameof(CanChooseLowAllowanceWork));
        OnPropertyChanged(nameof(WorkOnLowAllowance));
        OnPropertyChanged(nameof(AgentTrustSummary));
        OnPropertyChanged(nameof(AgentReport));
        OnPropertyChanged(nameof(HasAgentReport));
        OnPropertyChanged(nameof(IsAgentWorkRunning));
        OnPropertyChanged(nameof(CanViewAgentWork));
        OnPropertyChanged(nameof(AgentWorkSummary));
        OnPropertyChanged(nameof(IsAgentProjectSetUp));
        OnPropertyChanged(nameof(IsAgentSetupVisible));
        OnPropertyChanged(nameof(CanSetUpAgentProject));
        OnPropertyChanged(nameof(CanSaveAgentObjective));
        OnPropertyChanged(nameof(AgentObjectiveActionLabel));
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
        // One line that answers what is happening and what the person does about it. It is the first
        // thing on the surface, so it says the next move rather than leaving somebody to work it out.
        return project.Status switch
        {
            AgentProjectStatus.ClockingIn => "Waiting for an agent to report in.",
            AgentProjectStatus.Ready => "Nobody is working. Press Start work.",
            AgentProjectStatus.Working => $"{active} is working now.",
            AgentProjectStatus.HandoffPending => $"{active} was asked to hand over. The other agent starts when this session ends.",
            AgentProjectStatus.StopPending => $"{active} was asked to stop, and is finishing safely.",
            AgentProjectStatus.Paused => reason is null
                ? "Paused. Press Start work to carry on."
                : $"Paused. {reason} Press Start work to carry on.",
            AgentProjectStatus.NeedsAttention => reason is null
                ? "Needs you. Press Carry on when you have read it."
                : $"Needs you. {reason} Press Carry on when you have read it.",
            AgentProjectStatus.CompletionPending => $"{active} says the work is done, and is finishing.",
            AgentProjectStatus.Completed => "Finished. Write a new objective to run another one.",
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

    /// <summary>
    /// How many native agent sessions this window has open right now. A Claude session outlives the
    /// window that started it and keeps its own Filekin helper alive with it, so closing must be able
    /// to say plainly what is still running.
    /// </summary>
    public int LiveAgentSessionCount => _agentRun?.LiveSessions().Count ?? 0;

    /// <summary>How long a closing window waits for every agent session to be asked to stop.</summary>
    private static readonly TimeSpan EndAllAgentSessionsBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ends every agent session this window has open, through each provider's own stop. Nothing is
    /// killed: this is the same cooperative stop the End session button makes, for all of them.
    /// </summary>
    /// <returns>
    /// The reason an agent could not be ended, or <see langword="null"/> when every session was asked
    /// to stop. A window that is closing must not report a clean exit it did not achieve.
    /// </returns>
    public async Task<string?> EndAllAgentSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (_agentRun is not { } run)
        {
            return null;
        }

        // A window is waiting on this, so a provider that never answers must not hold the close open
        // for ever. Running out of time is reported as a failure, which is what it is: Filekin cannot
        // say the sessions were ended.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(EndAllAgentSessionsBudget);

        IsAgentsBusy = true;
        try
        {
            var failure = await run.StopAllSessionsAsync(budget.Token).ConfigureAwait(true);
            NoteAgentEvent(failure ?? "Ended every agent session before closing.");
            return failure;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            var timedOut = "The agents did not finish ending in time, so Filekin cannot say they stopped.";
            AgentsStatus = timedOut;
            NoteAgentEvent(timedOut);
            return timedOut;
        }
#pragma warning disable CA1031 // A provider failure is a sentence on screen, never a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            var failure = $"The agent sessions could not be ended: {exception.Message}";
            AgentsStatus = failure;
            NoteAgentEvent(failure);
            return failure;
        }
        finally
        {
            IsAgentsBusy = false;
        }
    }

    private async ValueTask DisposeAgentsAsync()
    {
        _agentWatch?.Stop();
        _agentWatch = null;

        // Disposal only lets go of the native sessions; it never decides their fate. The window asks
        // that question before it closes, because a session left running is a real process the person
        // can no longer see. See EndAllAgentSessionsAsync and MainWindow.OnClosing.
        if (_agentRun is { } run)
        {
            _agentRun = null;
            await run.DisposeAsync().ConfigureAwait(false);
        }

        if (_agentRuntime is { } runtime)
        {
            _agentRuntime = null;
            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        _agentStore?.Dispose();
        _agentStore = null;
    }
}
