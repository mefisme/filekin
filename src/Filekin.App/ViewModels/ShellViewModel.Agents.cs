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
    // One try per resumed session. A session a provider will not attach to yet must not be retried
    // on every refresh, or a tab reopens itself in a loop while somebody is looking at it.
    private readonly HashSet<(Guid ProjectId, AgentProvider Provider, string SessionId)>
        _cliTabsPutBack = [];

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
        + "coordination tools without asking you every time. Filekin never bypasses a tool's permission "
        + "system or answers an approval for you.";

    /// <summary>The default answer leaves each provider's configured app settings in charge.</summary>
    internal const string UseAppSettingsApproval = SharedFolderApproval
        + " Codex and Claude use their configured app settings; Filekin sends no permission or sandbox "
        + "choice of its own.";

    /// <summary>The widest answer, kept beside the others for the same reason.</summary>
    internal const string WorkOnItsOwnApproval = SharedFolderApproval
        + " An agent may read, write and run things for this project without stopping for routine "
        + "permission prompts. Codex and Claude still enforce their own safeguards.";

    /// <summary>The strictest answer both tools understand.</summary>
    internal const string LookDontTouchApproval = SharedFolderApproval
        + " An agent may inspect this folder and make a plan, but it cannot modify files.";

    private const string AutomaticChoice = "Whoever has more usage left";

    private readonly AgentModelCatalog _agentModelCatalog = new();
    private readonly Dictionary<AgentProvider, IReadOnlyList<AgentModelChoice>> _agentModels = [];
    private readonly Dictionary<Guid, AgentProjectState> _knownAgentProjects = [];
    private AgentCoordinationRuntime? _agentRuntime;
    private AgentRunService? _agentRun;
    private DispatcherTimer? _agentWatch;
    private SqliteAgentProjectStore? _agentStore;
    private AgentProjectState? _agentProject;

    private bool _isAgentsOpen;
    private bool _isAgentsBusy;
    private string _agentsFolderPath = string.Empty;
    private string _agentsObjective = string.Empty;
    private bool _isAgentsObjectiveDirty;
    private string _agentsStatus = string.Empty;
    private string _agentChoice = AutomaticChoice;
    // Each answer costs a provider process, so the question is asked at a human pace rather than at
    // the pace of the refresh that happens to notice it.
    private static readonly TimeSpan UnwatchedCheckInterval = TimeSpan.FromSeconds(10);

    private readonly HashSet<string> _notedCoordinationIds = new(StringComparer.Ordinal);
    private DateTimeOffset _lastUnwatchedCheck = DateTimeOffset.MinValue;
    private Guid _unwatchedCheckProjectId;
    private bool _isChoosingStartAgent;
    private Guid? _agentStartOperation;

    /// <summary>
    /// The agent sessions this window has seen running since it opened. Closing a CLI stops the
    /// work, so this window can offer to open that conversation again; a window that has just
    /// started has run nothing and offers nothing (owner decision, 2026-09-01).
    /// </summary>
    private readonly HashSet<(Guid ProjectId, AgentProvider Provider)> _agentSessionsRunHere = [];
    private string _lastNotedStatus = string.Empty;
    private string _lastNotedReport = string.Empty;
    private bool _isAgentActivityLogExpanded;

    /// <summary>Both agents, in a stable order, or empty while the folder has not opted in.</summary>
    public ObservableCollection<AgentParticipantViewModel> AgentParticipants { get; } = [];

    /// <summary>
    /// Opens the exact coordinated session in a real terminal tab, using the provider's own command.
    /// Filekin keeps coordinating it: attaching watches and steers the same session, and clock-in,
    /// messages, and handoffs stay MCP tool calls that do not care which front end is on screen.
    /// Returns the reason it could not, or <see langword="null"/> when the tab opened.
    /// </summary>
    private async Task<string?> OpenAgentSessionTerminalAsync(
        AgentProvider provider,
        string? nativeSessionId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return "This agent project has no folder yet.";
        }

        var coordinatedSessionId = nativeSessionId;

        // A Claude background session has two identities. Filekin records the conversation, because
        // that is what a handoff resumes, but `claude attach` takes a short handle of its own. Only
        // Claude can match them, and the same answer says whether the session is still running.
        if (provider == AgentProvider.ClaudeCode && !string.IsNullOrWhiteSpace(nativeSessionId))
        {
            var resolver = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
            string? attachId;
            try
            {
                attachId = await resolver
                    .ResolveClaudeAttachIdAsync(folderPath, nativeSessionId, cancellationToken)
                    .ConfigureAwait(true);
            }
#pragma warning disable CA1031 // Any failure to ask is the same fact to state: Filekin does not know.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                // Claude not answering is not Claude saying the session has gone. Saying "press Start
                // work to carry it on" here would invite a person to start beside a session that may
                // still be running.
                return AgentSessionAttachCommand.Explain(
                    provider,
                    AgentSessionAttachRefusal.ClaudeCheckFailed);
            }

            if (attachId is null)
            {
                return AgentSessionAttachCommand.Explain(
                    provider,
                    AgentSessionAttachRefusal.ClaudeSessionNotLive);
            }

            nativeSessionId = attachId;
        }

        // A resumed Codex process reads only the user's own configuration, and Filekin never writes
        // its coordination server there, so the same identity has to travel on the command line.
        // Claude attaches to the process it already started and keeps the one it has.
        var project = _agentProject;
        var run = provider == AgentProvider.Codex
            ? await AgentRunAsync(cancellationToken).ConfigureAwait(true)
            : _agentRun;
        var coordinationIdentity = provider == AgentProvider.Codex && project is not null && run is not null
            ? TryReadCoordinationIdentity(project, provider)
            : null;
        var codexThreadIsLive = provider == AgentProvider.Codex &&
            project is not null &&
            run?.LiveSessions().Any(live => live.ProjectId == project.Id && live.Provider == provider) == true;

        var command = AgentSessionAttachCommand.Create(
            provider,
            nativeSessionId,
            out var refusal,
            coordinationIdentity,
            codexThreadIsLive);
        if (command is null)
        {
            return AgentSessionAttachCommand.Explain(provider, refusal);
        }

        var title = AgentSessionAttachCommand.Title(provider, folderPath);
        AgentTerminalSessionRegistration? registration = null;
        try
        {
            if (provider == AgentProvider.Codex)
            {
                if (project is null || run is null || string.IsNullOrWhiteSpace(coordinatedSessionId))
                {
                    return "This Codex conversation is not attached to an agent project.";
                }

                registration = await run.RegisterTerminalSessionAsync(
                        project.Id,
                        provider,
                        coordinatedSessionId,
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            var outcome = _executor.StartAgentTerminal(folderPath, command, title);
            if (outcome.TerminalLaunches.Count != 1)
            {
                return "The terminal did not start.";
            }

            var launch = outcome.TerminalLaunches[0];
            AddTerminal(
                launch.Title,
                launch.Session,
                new AgentTerminalIdentity(project!.Id, provider, coordinatedSessionId!),
                registration);
            registration = null;
            ShowAgentProject();
            return null;
        }
        finally
        {
            if (registration is not null)
            {
                await registration.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private AgentMcpLaunchConfiguration? TryReadCoordinationIdentity(
        AgentProjectState project,
        AgentProvider provider)
    {
        try
        {
            return _agentRun!.McpLaunch(project, provider);
        }
#pragma warning disable CA1031 // A missing identity is reported as a refusal, not an unhandled fault.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Opens the terminal for the agent row in the selected project.</summary>
    public async Task<bool> OpenAgentSessionTerminalAsync(
        AgentParticipantViewModel participant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participant);

        // The CLI this window already opened is the one to go to. Starting a second client on the
        // same conversation is exactly what neither tool allows, so the button walks there instead.
        if (_agentProject is { } opened &&
            TerminalTabs.FirstOrDefault(tab =>
                tab.AgentSession is { } identity &&
                identity.ProjectId == opened.Id &&
                identity.Provider == participant.Provider) is { } existing)
        {
            SelectTerminal(existing);
            return true;
        }

        string? refused;
        try
        {
            refused = _agentProject is not { } project
                ? "No agent project is selected."
                : await OpenAgentSessionTerminalAsync(
                        participant.Provider,
                        participant.NativeSessionId,
                        project.FolderPath,
                        cancellationToken)
                    .ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A provider that will not answer is a visible line, not a crashed shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            refused = $"That session could not be opened: {exception.Message}";
        }

        if (refused is null)
        {
            return true;
        }

        AgentsStatus = refused;
        NoteAgentEvent(refused);
        return false;
    }

    private void RefreshSelectedAgentProject(AgentProjectState project)
    {
        if (SelectedAgentProjectTab?.Project?.Id == project.Id)
        {
            _agentProject = project;
            ShowAgentProject();
        }

        OnPropertyChanged(nameof(IsAgentWorkRunning));
        OnPropertyChanged(nameof(AgentWorkSummary));
    }

    /// <summary>
    /// What has happened in this project, oldest first. A status line that overwrites itself hides the
    /// run it is describing, so every change is kept here instead and nothing is thrown away until the
    /// list is long enough to be a burden.
    /// </summary>
    public ObservableCollection<AgentEventViewModel> AgentEvents { get; } = [];

    /// <summary>
    /// Whether the selected project's supporting activity log is open. The current status remains in
    /// the status band; this is historical detail and starts collapsed on every newly opened tab.
    /// </summary>
    public bool IsAgentActivityLogExpanded
    {
        get => _isAgentActivityLogExpanded;
        set
        {
            if (SetProperty(ref _isAgentActivityLogExpanded, value) && SelectedAgentProjectTab is { } tab)
            {
                tab.IsActivityLogExpanded = value;
            }
        }
    }

    public string AgentActivityLogHeader => AgentEvents.Count switch
    {
        1 => "Activity log · 1 event",
        var count => $"Activity log · {count} events",
    };

    /// <summary>Who the user wants to start. Leaving it alone lets Filekin decide.</summary>
    public ObservableCollection<string> AgentChoices { get; } =
        [AutomaticChoice, "Codex", "Claude Code"];

    public string AgentChoice
    {
        get => _agentChoice;
        set
        {
            if (SetProperty(ref _agentChoice, value))
            {
                // One list, one answer: picking closes it, the way a menu does.
                IsChoosingStartAgent = false;
                OnPropertyChanged(nameof(AgentStartActionLabel));
                OnPropertyChanged(nameof(AgentStartActionHint));
                OnPropertyChanged(nameof(CanStartAgents));
            }
        }
    }

    /// <summary>
    /// Whether the "start with" list is open. It is the same picker the agent rows use for their
    /// model, because a stock ComboBox draws its own white surface and cannot follow the theme.
    /// </summary>
    public bool IsChoosingStartAgent
    {
        get => _isChoosingStartAgent;
        set => SetProperty(ref _isChoosingStartAgent, value);
    }

    /// <summary>Whether a persistent Agents project tab is the selected workspace.</summary>
    public bool IsAgentsOpen
    {
        get => _isAgentsOpen;
        private set
        {
            if (SetProperty(ref _isAgentsOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(IsFilesOrAgentsWorkspaceSelected));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
                OnPropertyChanged(nameof(CanViewAgentWork));
            }
        }
    }

    public string AgentsTitle =>
        $"Agents · {Path.GetFileName(_agentsFolderPath.TrimEnd(Path.DirectorySeparatorChar))}";

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

    /// <summary>
    /// The question behind the three answers. Filekin starts agents where nobody can answer a
    /// permission prompt, so this is chosen once instead, and can be changed later.
    /// </summary>
    public static string AgentWorkModeText =>
        "Filekin runs agents with no window of their own, so a permission question would have nowhere "
        + "to appear. Choose how they may work here. You can change this whenever nothing is running.";

    /// <summary>How each answer reads, wherever it is offered or read back.</summary>
    public const string WorkOnItsOwnLabel = "Trust (auto)";

    /// <summary>The shared plain-language name for Claude's plan mode and Codex's read-only mode.</summary>
    public const string LookDontTouchLabel = "Plan / read-only";

    /// <summary>The answer that sends nothing and leaves each tool's own settings in charge.</summary>
    public const string UseMyOwnSettingsLabel = "Use app settings";

    /// <summary>What working on its own actually allows, in one sentence.</summary>
    public static string WorkOnItsOwnHelp =>
        "An agent may read, write and run things for this project without stopping for routine permission "
        + "prompts. Codex and Claude still enforce their own safeguards.";

    /// <summary>What looking without touching actually allows.</summary>
    public static string LookDontTouchHelp =>
        "An agent may inspect this folder and make a plan. It cannot modify files.";

    /// <summary>What keeping the user's own settings actually means.</summary>
    public static string UseMyOwnSettingsHelp =>
        "Codex and Claude use their configured app settings. An agent that needs permission stops and waits for you.";

    /// <summary>The three answers, in the order they are offered.</summary>
    public IReadOnlyList<string> AgentWorkModeChoices { get; } =
        [UseMyOwnSettingsLabel, LookDontTouchLabel, WorkOnItsOwnLabel];

    /// <summary>The answer on record, as the picker shows it.</summary>
    public string AgentWorkModeChoice
    {
        get => Label(_agentProject?.SharedCheckoutConsent?.WorkMode ?? AgentWorkMode.UseMyOwnSettings);
        set
        {
            IsChoosingAgentWorkMode = false;
            if (value is not { Length: > 0 } chosen || string.Equals(chosen, AgentWorkModeChoice, StringComparison.Ordinal))
            {
                return;
            }

            _ = ApproveSharedFolderAsync(ModeOf(chosen));
        }
    }

    /// <summary>Whether the list of answers is open.</summary>
    public bool IsChoosingAgentWorkMode
    {
        get => _isChoosingAgentWorkMode;
        set => SetProperty(ref _isChoosingAgentWorkMode, value);
    }

    /// <summary>
    /// The answer can be changed while nothing is running. A session carries the mode it started
    /// with, because a session with no window has nowhere to be told otherwise, so changing it now is
    /// a promise about the next run and offering it mid-run would be a lie.
    /// </summary>
    public bool CanChangeAgentWorkMode =>
        !_isAgentsBusy &&
        _agentProject is { SharedCheckoutConsent: not null } &&
        !AgentParticipants.Any(row => row.IsRunningNow);

    /// <summary>How the recorded answer reads back, once it exists.</summary>
    public string AgentWorkModeSummary => _agentProject?.SharedCheckoutConsent?.WorkMode switch
    {
        AgentWorkMode.WorkOnItsOwn => WorkOnItsOwnHelp,
        AgentWorkMode.LookDontTouch => LookDontTouchHelp,
        AgentWorkMode.UseMyOwnSettings => UseMyOwnSettingsHelp,
        _ => string.Empty,
    };

    private static string Label(AgentWorkMode mode) => mode switch
    {
        AgentWorkMode.WorkOnItsOwn => WorkOnItsOwnLabel,
        AgentWorkMode.LookDontTouch => LookDontTouchLabel,
        _ => UseMyOwnSettingsLabel,
    };

    private static AgentWorkMode ModeOf(string label) => label switch
    {
        WorkOnItsOwnLabel => AgentWorkMode.WorkOnItsOwn,
        LookDontTouchLabel => AgentWorkMode.LookDontTouch,
        _ => AgentWorkMode.UseMyOwnSettings,
    };

    /// <summary>Whether the owner still has to approve working in this folder.</summary>
    public bool IsAgentConsentNeeded => _agentProject is { SharedCheckoutConsent: null };

    public bool IsAgentStartVisible => _agentProject is { SharedCheckoutConsent: not null };

    /// <summary>
    /// A finished project has no turn to start, pass, or stop. Saving a valid next objective returns
    /// it to Ready and makes these controls useful again.
    /// </summary>
    public bool IsAgentTurnActionsVisible =>
        _agentProject is { SharedCheckoutConsent: not null } project &&
        project.Status != AgentProjectStatus.Completed;

    public bool CanApproveSharedFolder => !_isAgentsBusy && IsAgentConsentNeeded;

    private bool _isChoosingAgentWorkMode;

    /// <summary>
    /// Starting needs an approved folder, an unfinished project, a free turn, and something to do.
    /// Text still sitting in the objective box counts: Start saves it first rather than launching an
    /// agent that can only ask what the job is.
    /// </summary>
    public bool CanStartAgents =>
        !_isAgentsBusy &&
        _agentProject is { SharedCheckoutConsent: not null, Lease: null } project &&
        project.Status != AgentProjectStatus.Completed &&
        AgentBlockedByItsOwnCliTab() is null &&
        (_agentsObjective.Trim().Length > 0 ||
         (!_isAgentsObjectiveDirty && project.Objective.Length > 0));

    /// <summary>
    /// Stopping from a project tab targets that exact project. The Files task strip only offers Stop
    /// when exactly one project is running, so two concurrent projects can never make it destructive
    /// and ambiguous.
    /// </summary>
    public bool CanStopAgents =>
        !_isAgentsBusy &&
        (IsAgentsWorkspaceSelected
            ? _agentProject is { Lease: not null }
            : RunningAgentProjects().Length == 1);

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
    public bool IsAgentWorkRunning => RunningAgentProject() is not null;

    /// <summary>Whether the strip's View button has anywhere to take the user.</summary>
    public bool CanViewAgentWork => IsAgentWorkRunning && !IsAgentsWorkspaceSelected;

    /// <summary>Whether this folder is already an agent project, which decides what the surface shows.</summary>
    public bool IsAgentProjectSetUp => _agentProject is not null;

    public bool IsAgentSetupVisible => !IsAgentProjectSetUp;

    /// <summary>The objective text box: what the user wants the agents to do, in their own words.</summary>
    public string AgentsObjective
    {
        get => _agentsObjective;
        set
        {
            if (SetAgentsObjective(value))
            {
                _isAgentsObjectiveDirty = true;
                if (SelectedAgentProjectTab is { } tab)
                {
                    tab.ObjectiveDraft = value;
                    tab.IsObjectiveDraftDirty = true;
                }
            }
        }
    }

    /// <summary>
    /// Replaces the editor from an explicit load/save without pretending that the user typed it.
    /// Background project refreshes never call this, so an in-progress draft remains user-owned.
    /// </summary>
    private void RestoreAgentsObjective(string value, bool isDirty)
    {
        SetAgentsObjective(value);
        _isAgentsObjectiveDirty = isDirty;
        if (SelectedAgentProjectTab is { } tab)
        {
            tab.ObjectiveDraft = value;
            tab.IsObjectiveDraftDirty = isDirty;
        }
    }

    private bool SetAgentsObjective(string value)
    {
        if (!SetProperty(ref _agentsObjective, value, nameof(AgentsObjective)))
        {
            return false;
        }

        OnPropertyChanged(nameof(CanSaveAgentObjective));
        OnPropertyChanged(nameof(CanStartAgents));
        return true;
    }

    /// <summary>The one sentence describing where the project stands, or why it cannot start.</summary>
    public string AgentsStatus
    {
        get => _agentsStatus;
        private set
        {
            if (SetProperty(ref _agentsStatus, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
                OnPropertyChanged(nameof(AgentStatusTone));
            }
        }
    }

    /// <summary>
    /// Which kind of state the status line is reporting, so its band can be read before it is read.
    /// The words are the answer; the tone is how far across the room that answer carries.
    /// </summary>
    public string AgentStatusTone => _agentStartOperation is not null
        ? "Working"
        : _agentProject?.Status switch
        {
            AgentProjectStatus.Working or
            AgentProjectStatus.HandoffPending or
            AgentProjectStatus.CompletionPending or
            AgentProjectStatus.StopPending or
            AgentProjectStatus.ClockingIn => "Working",
            AgentProjectStatus.NeedsAttention => "NeedsYou",
            AgentProjectStatus.Completed => "Done",
            _ => "Quiet",
        };

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
        _agentsObjective.Trim().Length > 0 &&
        (project.Status == AgentProjectStatus.Completed ||
         !string.Equals(project.Objective, _agentsObjective.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// What pressing the clear button does. The status sentence names this action the same way it
    /// names the start action, so the two can never drift into different words for one button.
    /// </summary>
    public static string AgentClearActionLabel => "Clear the warning";

    /// <summary>
    /// What pressing the start button will do, in the words for this exact moment (owner decision,
    /// 2026-09-01). There are only two answers a person has to tell apart: this is new work, or this
    /// carries on. Whether an agent happens to be running right now is Filekin's problem to solve,
    /// not a third thing to read. A finished project says the next step instead, because a button
    /// that cannot be pressed must not sound like one that can.
    /// </summary>
    public string AgentStartActionLabel
    {
        get
        {
            if (_agentProject is not { } project)
            {
                return "Start work";
            }

            if (project.Status == AgentProjectStatus.Completed)
            {
                return "Write a new objective";
            }

            // Continue is only true of something that is going on. A saved conversation is not:
            // nothing is running, so there is nothing to carry on with, and the word left a person
            // asking "continue what?" (owner decision, 2026-09-01). Start work still keeps whatever
            // the agent knew, which is what its help text says.
            var considered = ChosenProvider() is { } chosen
                ? new[] { chosen }
                : [AgentProvider.Codex, AgentProvider.ClaudeCode];
            // A CLI tab somebody opened is a running tool that Filekin cannot send a turn to, so
            // it is not something to continue. Offering Continue there promised a relay that then
            // refused, because nothing had clocked in.
            return AgentParticipants.Any(row =>
                considered.Contains(row.Provider) &&
                row.IsRunningNow &&
                !row.IsCliTabOpenButNotReportedIn)
                ? "Continue"
                : "Start work";
        }
    }

    /// <summary>
    /// Why no start control can do anything while an agent's own CLI tab is open here. It says what
    /// happened rather than only what to press: the tool is running, Filekin is not driving that
    /// session, and being told to close a tab explains neither of those on its own.
    /// </summary>
    /// <summary>The status band's version: the fact, and the one thing to do about it.</summary>
    /// <remarks>
    /// Neither of these ends by naming the start button. The button is beside them and says what it
    /// does; repeating it after every blocker turns the answer into a procedure and buries the fact.
    /// </remarks>
    private static string SayCliTabBlock(AgentParticipantViewModel blocked) =>
        $"Filekin lost track of this {blocked.Name} session. "
        + "Close its CLI tab and Filekin takes it back over.";

    /// <summary>
    /// The button's version, which has room for the cause. The band states it; hovering explains it.
    /// </summary>
    /// <remarks>
    /// Both sentences name <em>this</em> session and never the provider in general. The person may
    /// have that tool running for half a dozen unrelated things, and "Claude Code is running outside
    /// Filekin" reads as though all of it were in the way. Neither says an open CLI tab blocks work
    /// either, because ordinarily it does not: a Codex tab Filekin opened is registered and works.
    /// Losing the session is the fault being reported; the tab is only where it is being held.
    ///
    /// A terminal somebody opened for themselves is not involved and cannot be: a tab only counts
    /// here when Filekin opened it against this project and provider, which is what
    /// <see cref="TerminalTabViewModel.AgentSession"/> records.
    ///
    /// The cause is hedged on purpose. A session outliving a Filekin close is the usual way here, and
    /// a second Filekin window on the same project reaches the same place.
    /// </remarks>
    private static string ExplainCliTabBlock(AgentParticipantViewModel blocked) =>
        $"Filekin lost track of this {blocked.Name} session — usually because it carried on after "
        + "Filekin closed. Close its CLI tab and Filekin takes it back over. Nothing is lost.";

    /// <summary>The sentence behind the start button, matching whichever answer it is offering.</summary>
    public string AgentStartActionHint => AgentBlockedByItsOwnCliTab() is { } blocked
        ? ExplainCliTabBlock(blocked)
        : AgentStartActionLabel switch
        {
            "Write a new objective" => "This job is finished. Write what you want done next, then save it.",
            "Continue" => "Gives the turn to the agent that is already running here. No second agent is started.",
            _ => "Starts an agent on this objective. One that has worked here before keeps what it knew.",
        };

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
            var running = RunningAgentProjects();
            if (running.Length == 0)
            {
                return string.Empty;
            }

            if (running.Length > 1)
            {
                return $"{running.Length} projects are working.";
            }

            var project = running[0];
            var lease = project.Lease!;
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

    /// <summary>
    /// Opens or selects the persistent Agents tab for the current Files folder without opting the
    /// folder in. Another project's tab and provider work remain open and independent.
    /// </summary>
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

        if (_currentPath is not { Length: > 0 } folderPath)
        {
            AgentsStatus = "Open a folder in Files first.";
            return;
        }

        var tab = AgentProjectTabs.FirstOrDefault(candidate =>
            string.Equals(candidate.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            tab = new AgentProjectTabViewModel(folderPath);
            AgentProjectTabs.Add(tab);
        }

        SelectAgentProjectTab(tab);
        await LoadAgentProjectAsync(cancellationToken).ConfigureAwait(true);
        await ShowModelChoicesAsync(cancellationToken).ConfigureAwait(true);
        await ReadAgentAllowanceAsync(cancellationToken).ConfigureAwait(true);
        SaveSelectedAgentProjectTabState();
    }

    /// <summary>
    /// Asks both tools how much allowance is left, once, as the surface opens. Deciding whether to
    /// start at all - and which agent to start - is the reason the numbers are worth having, and they
    /// are no use only after the first turn has already been spent. Reading them means running the
    /// provider tools, so it is bounded, and a tool that will not answer leaves its agent reading
    /// unknown instead of holding the surface up.
    /// </summary>
    private async Task ReadAgentAllowanceAsync(CancellationToken cancellationToken)
    {
        if (_agentProject is not { } project)
        {
            return;
        }

        try
        {
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(ReadAgentAllowanceBudget);
            var latest = await runtime.RefreshAllowanceAsync(project.Id, budget.Token)
                .ConfigureAwait(true);
            _agentProject = latest;
            ShowAgentProject();
        }
#pragma warning disable CA1031 // Not knowing the allowance is a blank reading, never a broken surface.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The rows already read "Unknown" under USAGE LEFT, which is the honest answer here.
        }
    }

    /// <summary>How long the opening surface waits for both tools to report their allowance.</summary>
    private static readonly TimeSpan ReadAgentAllowanceBudget = TimeSpan.FromSeconds(20);

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

    public string AgentWorkTitle
    {
        get
        {
            var running = RunningAgentProjects();
            if (running.Length == 0)
            {
                return "Agents";
            }

            if (running.Length > 1)
            {
                return $"Agents · {running.Length} projects";
            }

            var project = running[0];
            var folder = Path.GetFileName(project.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
            return $"Agents · {(folder.Length == 0 ? project.FolderPath : folder)}";
        }
    }

    public void SelectAgentProjectTab(AgentProjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!AgentProjectTabs.Contains(tab))
        {
            return;
        }

        SaveSelectedAgentProjectTabState();
        IsFilesWorkspaceSelected = false;
        SelectedTerminal = null;
        SelectedAgentProjectTab = tab;
        IsAgentsOpen = true;
        foreach (var candidate in AgentProjectTabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, tab);
        }

        foreach (var terminal in TerminalTabs)
        {
            terminal.IsSelected = false;
        }

        AgentsFolderPath = tab.FolderPath;
        _agentProject = tab.Project;
        RestoreAgentsObjective(tab.ObjectiveDraft, tab.IsObjectiveDraftDirty);
        AgentChoice = tab.AgentChoice.Length == 0 ? AutomaticChoice : tab.AgentChoice;
        IsAgentActivityLogExpanded = tab.IsActivityLogExpanded;
        AgentEvents.Clear();
        foreach (var item in tab.Events)
        {
            AgentEvents.Add(item);
        }

        _notedCoordinationIds.Clear();
        foreach (var noted in tab.NotedCoordinationIds)
        {
            _notedCoordinationIds.Add(noted);
        }

        _lastNotedStatus = tab.LastNotedStatus;
        _lastNotedReport = tab.LastNotedReport;
        OnPropertyChanged(nameof(HasAgentEvents));
        OnPropertyChanged(nameof(AgentActivityLogHeader));
        ShowAgentProject();
    }

    public void CloseAgentProjectTab(AgentProjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var index = AgentProjectTabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasSelected = ReferenceEquals(SelectedAgentProjectTab, tab);
        if (wasSelected)
        {
            SaveSelectedAgentProjectTabState();
            IsAgentsOpen = false;
            SelectedAgentProjectTab = null;
        }

        AgentProjectTabs.RemoveAt(index);
        if (wasSelected)
        {
            SelectWorkspaceAt(Math.Min(index + 1, AgentProjectTabs.Count + TerminalTabs.Count));
        }

        WatchAgentProject();
    }

    private void SaveSelectedAgentProjectTabState()
    {
        if (SelectedAgentProjectTab is not { } tab)
        {
            return;
        }

        if (_agentProject is null ||
            string.Equals(_agentProject.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            tab.Project = _agentProject;
        }

        tab.ObjectiveDraft = _agentsObjective;
        tab.IsObjectiveDraftDirty = _isAgentsObjectiveDirty;
        tab.AgentChoice = _agentChoice;
        tab.IsActivityLogExpanded = _isAgentActivityLogExpanded;
        tab.Events.Clear();
        tab.Events.AddRange(AgentEvents);
        tab.NotedCoordinationIds.Clear();
        tab.NotedCoordinationIds.AddRange(_notedCoordinationIds);
        tab.LastNotedStatus = _lastNotedStatus;
        tab.LastNotedReport = _lastNotedReport;
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
            var submittedObjective = _agentsObjective;
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = await runtime
                .CreateProjectAsync(_agentsFolderPath, submittedObjective, cancellationToken)
                .ConfigureAwait(true);
            if (string.Equals(_agentsObjective, submittedObjective, StringComparison.Ordinal))
            {
                RestoreAgentsObjective(_agentProject.Objective, isDirty: false);
            }

            // There is a project to list now, so the sidebar has somewhere to go.
            HasAgentProjects = true;
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
            var submittedObjective = _agentsObjective;
            var runtime = await AgentRuntimeAsync(cancellationToken).ConfigureAwait(true);
            _agentProject = project.Status == AgentProjectStatus.Completed
                ? await runtime.StartNewObjectiveAsync(project.Id, submittedObjective, cancellationToken)
                    .ConfigureAwait(true)
                : await runtime.SetObjectiveAsync(project.Id, submittedObjective, cancellationToken)
                    .ConfigureAwait(true);
            if (string.Equals(_agentsObjective, submittedObjective, StringComparison.Ordinal))
            {
                RestoreAgentsObjective(_agentProject.Objective, isDirty: false);
            }

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
    /// <param name="workMode">
    /// How an agent may work in this folder once it is started. Filekin sends this to each tool as
    /// that tool's own setting; it never answers a permission question for the user. The same method
    /// records the first answer and every later change, so both are stored the same way.
    /// </param>
    public async Task ApproveSharedFolderAsync(
        AgentWorkMode workMode,
        CancellationToken cancellationToken = default)
    {
        if (_agentProject is not { } project || !(CanApproveSharedFolder || CanChangeAgentWorkMode))
        {
            return;
        }

        var words = workMode switch
        {
            AgentWorkMode.WorkOnItsOwn => WorkOnItsOwnApproval,
            AgentWorkMode.LookDontTouch => LookDontTouchApproval,
            _ => UseAppSettingsApproval,
        };
        await RunAgentActionAsync(
            "This folder could not be approved",
            async runtime => await runtime
                .GrantSharedCheckoutConsentAsync(project.Id, words, workMode, cancellationToken)
                .ConfigureAwait(true),
            cancellationToken).ConfigureAwait(true);
        NoteAgentEvent(workMode switch
        {
            AgentWorkMode.WorkOnItsOwn => "Agents may work in this folder on their own.",
            AgentWorkMode.LookDontTouch => "Agents may read this folder and change nothing.",
            _ => "Codex and Claude use their configured app settings here.",
        });
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

        // Somebody who types the objective and presses Start has said what they want. Saving it is
        // part of starting, so the obvious gesture works instead of quietly launching an agent
        // against the previous objective, or against none at all.
        if (CanSaveAgentObjective)
        {
            await SaveAgentObjectiveAsync(cancellationToken).ConfigureAwait(true);
            if (_agentProject is not { Objective.Length: > 0 } saved)
            {
                AgentsStatus = "The objective could not be saved, so nothing was started.";
                return;
            }

            project = saved;
        }

        var chosen = ChosenProvider();
        var operation = Guid.NewGuid();
        _agentStartOperation = operation;
        ShowAgentStartProgress(new AgentStartProgress(AgentStartStage.CheckingUsage, chosen));
        var progress = new Progress<AgentStartProgress>(report =>
        {
            if (_agentStartOperation == operation && _agentProject?.Id == project.Id)
            {
                ShowAgentStartProgress(report);
            }
        });

        try
        {
            await RunAgentActionAsync(
                "The agent could not be started",
                async _ =>
                {
                    var run = await AgentRunAsync(cancellationToken).ConfigureAwait(true);
                    return await run
                        .StartAsync(project.Id, chosen, progress, cancellationToken)
                        .ConfigureAwait(true);
                },
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            if (_agentStartOperation == operation)
            {
                _agentStartOperation = null;
                OnPropertyChanged(nameof(AgentStatusTone));
            }
        }
    }

    /// <summary>Turns service progress into one short, honest status sentence.</summary>
    private void ShowAgentStartProgress(AgentStartProgress progress)
    {
        var provider = progress.Provider is { } value
            ? AgentParticipantViewModel.DisplayName(value)
            : "an agent";
        AgentsStatus = progress.Stage switch
        {
            AgentStartStage.CheckingUsage when progress.Provider is null =>
                "Checking usage to choose who starts…",
            AgentStartStage.CheckingUsage => $"Checking {provider} usage…",
            AgentStartStage.StartingAgent => $"Starting {provider}…",
            AgentStartStage.WaitingForConnection => $"Waiting for {provider} to connect…",
            AgentStartStage.GivingTurn => $"{provider} connected. Giving it the turn…",
            _ => "Starting work…",
        };
    }

    /// <summary>
    /// Asks the working agent to stop. The project is kept, so it can be resumed later. The turn is
    /// released only when that agent's own tool reports the session ended.
    /// </summary>
    public async Task StopAgentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStopAgents || AgentProjectForAction() is not { } project)
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

            // End is one action with one meaning for both agents: this agent's sessions here stop,
            // and every terminal still marked as one of them goes with them. The reasons differ and
            // the outcome must not. Codex has no cooperative session-stop command, so closing its
            // terminal is the stop. Claude's stop is cooperative and its tab is only a view of it —
            // but a view of a session that has ended is not worth keeping, and a tab left marked
            // goes on being counted as the CLI holding that session long after it is gone.
            var terminals = TerminalTabs.Where(candidate =>
                candidate.AgentSession is { } identity &&
                identity.ProjectId == project.Id &&
                identity.Provider == participant.Provider).ToArray();
            var stopped = await run.StopSessionsAsync(project.Id, participant.Provider, cancellationToken)
                .ConfigureAwait(true);
            foreach (var terminal in terminals)
            {
                await CloseTerminalAsync(terminal).ConfigureAwait(true);
            }

            NoteAgentEvent(terminals.Length switch
            {
                0 => stopped switch
                {
                    null => $"{participant.Name} has no session of its own to stop; its sessions end with their turn.",
                    0 => $"{participant.Name} has no session open in this folder.",
                    1 => $"Asked {participant.Name} to end its session.",
                    var many => $"Asked {participant.Name} to end {many} sessions.",
                },
                1 => $"Ended {participant.Name}'s session and closed its CLI tab.",
                var many => $"Ended {participant.Name}'s session and closed its {many} CLI tabs.",
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

    /// <summary>
    /// Puts an agent's CLI tab back on its resumed session. Claude has to stop to be resumed, so the
    /// tab it was showing is left as an ordinary shell when the turn moves on; losing the session
    /// somebody was reading should not be the price of the relay carrying on.
    ///
    /// It reattaches a tab that is already here and never opens one: a person who has not opened a
    /// CLI has said nothing about wanting one, and a window that grows terminals by itself is a worse
    /// fault than the one being fixed (DECISIONS.md, 2026-09-02).
    /// </summary>
    private Task ReattachAgentCliTabsAsync(AgentProjectState project) =>
        ReattachAgentCliTabsAsync(
            project,
            (provider, resumed) => OpenAgentSessionTerminalAsync(
                provider,
                resumed,
                project.FolderPath,
                CancellationToken.None));

    /// <param name="openCli">
    /// How a resumed CLI is opened. It is a parameter so the order this method works in — open,
    /// then close, then put the selection back — can be checked without launching a provider
    /// (tests/Filekin.App.Tests). It returns the reason it could not, or <see langword="null"/>.
    /// </param>
    /// <inheritdoc cref="ReattachAgentCliTabsAsync(AgentProjectState)"/>
    internal async Task ReattachAgentCliTabsAsync(
        AgentProjectState project,
        Func<AgentProvider, string, Task<string?>> openCli)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(openCli);
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            if (project.Participant(provider).NativeSessionId is not { Length: > 0 } resumed)
            {
                continue;
            }

            var tab = TerminalTabs.FirstOrDefault(candidate =>
                candidate.AgentSession is null &&
                candidate.ReattachableAgentSession is { } was &&
                was.ProjectId == project.Id &&
                was.Provider == provider &&
                !string.Equals(was.NativeSessionId, resumed, StringComparison.Ordinal));
            if (tab is null || !_cliTabsPutBack.Add((project.Id, provider, resumed)))
            {
                continue;
            }

            // Open first. A session the provider will not attach to yet is a reason to leave this tab
            // exactly where it is, not to close it and have nothing to put in its place.
            var wasSelected = ReferenceEquals(SelectedTerminal, tab);
            var previous = SelectedTerminal;
            var index = TerminalTabs.IndexOf(tab);
            var refusal = await openCli(provider, resumed).ConfigureAwait(true);
            if (refusal is not null)
            {
                continue;
            }

            var reattached = TerminalTabs[^1];
            await CloseTerminalAsync(tab).ConfigureAwait(true);
            TerminalTabs.Move(TerminalTabs.Count - 1, Math.Min(index, TerminalTabs.Count - 1));

            // Opening a tab selects it, and this one opened by itself. Somebody reading this exact tab
            // stays on it; somebody who was elsewhere is not dragged here mid-turn.
            if (!wasSelected && previous is not null && TerminalTabs.Contains(previous))
            {
                SelectTerminal(previous);
            }
            else if (wasSelected)
            {
                SelectTerminal(reattached);
            }
        }
    }

    /// <summary>
    /// Reloads the project after an attached terminal reports its provider process ended. The
    /// registration has already reconciled the lease/presence before this runs, so the row and its
    /// enabled actions change together instead of waiting for the periodic watcher.
    /// </summary>
    private async Task RefreshAfterAgentProcessEndedAsync(AgentTerminalIdentity identity)
    {
        if (_agentStore is null)
        {
            return;
        }

        var refreshed = await _agentStore.LoadAsync(identity.ProjectId).ConfigureAwait(true);
        if (refreshed is null)
        {
            return;
        }

        RememberAgentProject(refreshed);
        if (_agentProject?.Id == refreshed.Id)
        {
            _agentProject = refreshed;
            ShowAgentProject();
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
        if (RunningAgentProject() is not { } project)
        {
            return;
        }

        var tab = AgentProjectTabs.FirstOrDefault(candidate =>
            string.Equals(candidate.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            tab = new AgentProjectTabViewModel(project.FolderPath) { Project = project };
            AgentProjectTabs.Add(tab);
        }

        SelectAgentProjectTab(tab);
    }

    private AgentProjectState? AgentProjectForAction() =>
        IsAgentsWorkspaceSelected
            ? _agentProject
            : RunningAgentProjects() is [var only] ? only : null;

    private AgentProjectState? RunningAgentProject() =>
        RunningAgentProjects().FirstOrDefault();

    private AgentProjectState[] RunningAgentProjects() =>
        _knownAgentProjects.Values
            .Where(project => project.Lease is not null)
            .OrderBy(project => project.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// The agent whose open CLI tab is why no start control can do anything right now, or
    /// <see langword="null"/> when nothing is in that state.
    /// </summary>
    /// <remarks>
    /// Proved by hand on 2026-09-02: with Codex's CLI resumed in a tab, the row reads Running and the
    /// coordinator still has Codex Offline, so pressing the start control fails with an internal
    /// sentence about clocking in. Filekin genuinely cannot dispatch a turn into a terminal somebody
    /// else is driving. That is defensible; saying nothing true about it is not, so the surface names
    /// the tab and the way out instead of offering a button that must refuse.
    /// </remarks>
    private AgentParticipantViewModel? AgentBlockedByItsOwnCliTab() =>
        AgentTheStartWouldUse() is { IsCliTabOpenButNotReportedIn: true } row ? row : null;

    /// <summary>
    /// Which agent a start would use, as far as the surface can honestly tell: the chosen one, else
    /// the one a written handoff names, else the only one running. Anything less certain is left to
    /// the run service, which owns the real choice.
    /// </summary>
    private AgentParticipantViewModel? AgentTheStartWouldUse()
    {
        if (ChosenProvider() is { } chosen)
        {
            return AgentParticipants.FirstOrDefault(row => row.Provider == chosen);
        }

        if (_agentProject?.PendingHandoff?.To is { } recipient)
        {
            return AgentParticipants.FirstOrDefault(row => row.Provider == recipient);
        }

        var running = AgentParticipants.Where(row => row.IsRunningNow).ToArray();
        return running.Length == 1 ? running[0] : null;
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
        OnPropertyChanged(nameof(AgentActivityLogHeader));
    }

    /// <summary>
    /// Writes this project's coordination facts into the account: the messages between the agents and
    /// the user, and every handoff with what it said. These are the facts no provider CLI can show,
    /// because no CLI knows the other agent exists, so the control room is where they belong. Each
    /// fact is written once, under its own time rather than the time it was noticed.
    /// </summary>
    /// <summary>
    /// Asks the providers whether they are running a session for this project that Filekin is not
    /// watching, and tells the rows. Only asked when it could change an answer — an agent Filekin
    /// already sees needs no asking — and never more often than <see cref="UnwatchedCheckInterval"/>,
    /// because each answer costs a provider process.
    /// </summary>
    private async Task RefreshUnwatchedSessionsAsync(AgentProjectState project)
    {
        if (_agentRun is not { } run)
        {
            return;
        }

        // Nothing here could be running out of sight, so no row may still be saying that it is. The
        // answer belongs to one project: rows are reused between project tabs, and a yes kept from
        // the project before this one is how a stopped agent went on claiming to be running.
        if (AgentParticipants.All(participant => !participant.MightBeRunningUnwatched))
        {
            foreach (var participant in AgentParticipants)
            {
                participant.UnwatchedLiveness = AgentSessionLiveness.NotRunning;
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (project.Id == _unwatchedCheckProjectId && now - _lastUnwatchedCheck < UnwatchedCheckInterval)
        {
            return;
        }

        _unwatchedCheckProjectId = project.Id;
        _lastUnwatchedCheck = now;
        try
        {
            var observations = await Task.WhenAll(AgentParticipants
                    .Where(participant => participant.MightBeRunningUnwatched)
                    .Select(async participant => new
                    {
                        participant.Provider,
                        Liveness = await run.UnwatchedSessionLivenessAsync(project, participant.Provider)
                            .ConfigureAwait(true),
                    }))
                .ConfigureAwait(true);

            // The tool answered about this project. If the surface has moved on to another one since
            // the question was asked, the answer is no longer about what is on screen.
            if (_agentProject?.Id != project.Id)
            {
                return;
            }

            foreach (var participant in AgentParticipants)
            {
                participant.UnwatchedLiveness = observations
                    .FirstOrDefault(observation => observation.Provider == participant.Provider)
                    ?.Liveness ?? AgentSessionLiveness.NotRunning;
            }
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            if (_agentProject?.Id != project.Id)
            {
                return;
            }

            // Infrastructure normally turns a provider failure into Unknown. Any unexpected failure
            // at this presentation boundary must still replace an earlier Running answer rather than
            // preserving stale success.
            foreach (var participant in AgentParticipants)
            {
                participant.UnwatchedLiveness = participant.MightBeRunningUnwatched
                    ? AgentSessionLiveness.Unknown
                    : AgentSessionLiveness.NotRunning;
            }
        }
    }

    private void NoteCoordinationFacts(AgentProjectState project)
    {
        foreach (var message in project.Messages)
        {
            NoteCoordinationFact(
                $"message:{message.Id:D}",
                message.SentAt,
                $"{AgentParticipantViewModel.DisplayName(message.From)} → {AgentParticipantViewModel.DisplayName(message.To)}: {message.Text}");
        }

        if (project.LastHandoff is { } last)
        {
            NoteCoordinationFact(HandoffId(last), last.CreatedAt, DescribeHandoff(last, pending: false));
        }

        if (project.PendingHandoff is { } pending)
        {
            NoteCoordinationFact(HandoffId(pending), pending.CreatedAt, DescribeHandoff(pending, pending: true));
        }
    }

    private static string HandoffId(AgentHandoff handoff) => $"handoff:{handoff.Id:D}";

    private static string DescribeHandoff(AgentHandoff handoff, bool pending)
    {
        var from = AgentParticipantViewModel.DisplayName(handoff.From);
        var to = AgentParticipantViewModel.DisplayName(handoff.To);
        var opening = pending
            ? $"{from} is handing over to {to}: {handoff.Summary}"
            : $"{from} handed over to {to}: {handoff.Summary}";
        var detail = new[]
        {
            Detail("Completed", handoff.CompletedWork),
            Detail("Remaining", handoff.RemainingWork),
            Detail("Verification", handoff.Verification),
            Detail("Blockers", handoff.Blockers),
        }.Where(line => line is not null);
        return string.Join(Environment.NewLine, detail.Prepend(opening));
    }

    private static string? Detail(string name, string value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{name}: {value}";

    private void NoteCoordinationFact(string id, DateTimeOffset at, string text)
    {
        if (text.Length == 0 || !_notedCoordinationIds.Add(id))
        {
            return;
        }

        AgentEvents.Add(new AgentEventViewModel(at, text));
        while (AgentEvents.Count > 200)
        {
            AgentEvents.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasAgentEvents));
        OnPropertyChanged(nameof(AgentActivityLogHeader));
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
                OnPropertyChanged(nameof(AgentActivityLogHeader));
            }

            _agentProject = found;
            if (!_isAgentsObjectiveDirty)
            {
                RestoreAgentsObjective(_agentProject?.Objective ?? string.Empty, isDirty: false);
            }

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
        var needed = AgentProjectTabs.Any(tab => tab.Project is not null) || IsAgentWorkRunning;
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
        if (_isAgentsBusy)
        {
            return;
        }

        try
        {
            var runtime = await AgentRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
            var projects = AgentProjectTabs
                .Select(tab => tab.Project)
                .Concat(_knownAgentProjects.Values.Where(project => project.Lease is not null))
                .Where(project => project is not null)
                .Cast<AgentProjectState>()
                .DistinctBy(project => project.Id)
                .ToArray();
            foreach (var project in projects)
            {
                var latest = await runtime.FindProjectAsync(project.FolderPath).ConfigureAwait(true);
                if (latest is null)
                {
                    continue;
                }

                RememberAgentProject(latest);
                if (SelectedAgentProjectTab is { } selected &&
                    string.Equals(selected.FolderPath, latest.FolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    _agentProject = latest;
                    ShowAgentProject();
                }
            }

            OnPropertyChanged(nameof(IsAgentWorkRunning));
            OnPropertyChanged(nameof(CanViewAgentWork));
            OnPropertyChanged(nameof(AgentWorkTitle));
            OnPropertyChanged(nameof(AgentWorkSummary));
            OnPropertyChanged(nameof(CanStopAgents));
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
            RememberAgentProject(project);
            NoteCoordinationFacts(project);
            var jobIsFinished = project.Status == AgentProjectStatus.Completed;
            var changedProject = _unwatchedCheckProjectId != project.Id;
            foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
            {
                var sessionOpenHere = _agentRun?.LiveSessions().Any(
                    live => live.ProjectId == project.Id && live.Provider == provider) == true;

                // A CLI this window opened is running too, and it is the one the person can walk to,
                // so the row offers the tab rather than a second copy of the same conversation.
                var cliTabOpenHere = TerminalTabs.Any(tab =>
                    tab.AgentSession is { } identity &&
                    identity.ProjectId == project.Id &&
                    identity.Provider == provider);
                var row = AgentParticipants.FirstOrDefault(candidate => candidate.Provider == provider);
                if (row is null)
                {
                    row = new AgentParticipantViewModel(
                        project.Participant(provider),
                        project.ActiveAgent == provider,
                        jobIsFinished,
                        ChooseAgentModel);
                    if (_agentModels.TryGetValue(provider, out var models))
                    {
                        row.ShowModels(models);
                    }

                    AgentParticipants.Add(row);
                }
                else
                {
                    row.Update(project.Participant(provider), project.ActiveAgent == provider, jobIsFinished);
                }

                if (changedProject)
                {
                    // This answer belongs to the preceding folder. Show no live claim until the
                    // current folder's own asynchronous provider check answers.
                    row.UnwatchedLiveness = AgentSessionLiveness.NotRunning;
                }

                if (sessionOpenHere || cliTabOpenHere)
                {
                    _agentSessionsRunHere.Add((project.Id, provider));
                }

                row.IsSessionOpenHere = sessionOpenHere;
                row.IsCliTabOpenHere = cliTabOpenHere;
                row.HasRunInThisWindow = _agentSessionsRunHere.Contains((project.Id, provider));
            }
        }

        // A finished job clears its own objective box, so the next one is typed into an empty line
        // instead of on top of work that is already done. Only the untouched text is cleared: once a
        // person has started writing the next objective, that draft is theirs and is left alone. What
        // was finished stays readable in the objective summary either way.
        if (_agentProject is { Status: AgentProjectStatus.Completed } finished &&
            !_isAgentsObjectiveDirty &&
            _agentsObjective.Length > 0 &&
            string.Equals(_agentsObjective.Trim(), finished.Objective.Trim(), StringComparison.Ordinal))
        {
            RestoreAgentsObjective(string.Empty, isDirty: false);
        }

        if (_agentProject is { } current)
        {
            _ = ReattachAgentCliTabsAsync(current);
            _ = RefreshUnwatchedSessionsAsync(current);
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
        OnPropertyChanged(nameof(IsAgentTurnActionsVisible));
        OnPropertyChanged(nameof(CanApproveSharedFolder));
        OnPropertyChanged(nameof(CanStartAgents));
        OnPropertyChanged(nameof(CanStopAgents));
        OnPropertyChanged(nameof(CanPassTheAgentTurn));
        OnPropertyChanged(nameof(CanClearAgentAttention));
        OnPropertyChanged(nameof(CanChooseLowAllowanceWork));
        OnPropertyChanged(nameof(WorkOnLowAllowance));
        OnPropertyChanged(nameof(AgentWorkModeSummary));
        OnPropertyChanged(nameof(AgentWorkModeChoice));
        OnPropertyChanged(nameof(CanChangeAgentWorkMode));
        OnPropertyChanged(nameof(AgentReport));
        OnPropertyChanged(nameof(HasAgentReport));
        OnPropertyChanged(nameof(IsAgentWorkRunning));
        OnPropertyChanged(nameof(CanViewAgentWork));
        OnPropertyChanged(nameof(AgentWorkSummary));
        OnPropertyChanged(nameof(AgentWorkTitle));
        OnPropertyChanged(nameof(IsAgentProjectSetUp));
        OnPropertyChanged(nameof(IsAgentSetupVisible));
        OnPropertyChanged(nameof(CanSetUpAgentProject));
        OnPropertyChanged(nameof(CanSaveAgentObjective));
        OnPropertyChanged(nameof(AgentStartActionLabel));
        OnPropertyChanged(nameof(AgentStartActionHint));
        OnPropertyChanged(nameof(AgentObjectiveSummary));
        OnPropertyChanged(nameof(AgentHandoffSummary));
        SaveSelectedAgentProjectTabState();
    }

    private void RememberAgentProject(AgentProjectState project)
    {
        _knownAgentProjects[project.Id] = project;
        foreach (var tab in AgentProjectTabs.Where(tab =>
            string.Equals(tab.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase)))
        {
            tab.Project = project;
        }
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

        // While an agent's own CLI tab is open here, every "press this" sentence is wrong: Codex or
        // Claude is running, the agent has not reported in, and closing that tab is the only thing
        // that helps. In the two states where nothing else is pressing, that is the whole answer, so it
        // replaces the line rather than lengthening it — a status band nobody finishes reading says
        // nothing. Why it happened belongs on the button, which has room for it.
        if (AgentBlockedByItsOwnCliTab() is { } blocked &&
            project.Status is AgentProjectStatus.Ready or AgentProjectStatus.Paused)
        {
            return SayCliTabBlock(blocked);
        }

        var nextMove = $"Press {AgentStartActionLabel} to carry on.";

        // One line that answers what is happening and what the person does about it. It is the first
        // thing on the surface, so it says the next move rather than leaving somebody to work it out.
        return project.Status switch
        {
            AgentProjectStatus.ClockingIn => "Waiting for an agent to report in.",
            AgentProjectStatus.Ready => $"Nobody is working. {nextMove}",
            AgentProjectStatus.Working => $"{active} is working now.",
            AgentProjectStatus.HandoffPending => $"{active} was asked to hand over. The other agent starts when this session ends.",
            AgentProjectStatus.StopPending => $"{active} was asked to stop, and is finishing safely.",
            // The rows say Stopped for this, and one surface must not use two words for one state.
            // The recorded reason is not repeated here: a project is only paused because the person
            // asked it to stop, so telling them that back costs a line and says nothing. It is still
            // stored, and the agents still read it.
            AgentProjectStatus.Paused => $"Stopped. {nextMove}",
            // The button is named for what pressing it does, so this line can name the next move the
            // same way every other status does. "I have read it" promised something to open when the
            // only thing to read is this sentence.
            AgentProjectStatus.NeedsAttention => reason is null
                ? $"Needs you. Press {AgentClearActionLabel} to start again."
                : $"Needs you. {reason} Press {AgentClearActionLabel} to start again.",
            AgentProjectStatus.CompletionPending => $"{active} says the work is done, and is finishing.",
            AgentProjectStatus.Completed => "Finished. Enter the next objective to start again.",
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
    /// How many native agent sessions this window is watching. This is not what is running: a Claude
    /// background session outlives its turn, so it leaves this list long before it stops existing.
    /// Use <see cref="CountLiveAgentSessionsAsync"/> to ask the providers themselves.
    /// </summary>
    public int WatchedAgentSessionCount => _agentRun?.LiveSessions().Count ?? 0;

    /// <summary>
    /// Asks each provider what it still has open, for every project Filekin knows about. A window that
    /// is closing needs the truth, not its own bookkeeping.
    /// </summary>
    public async Task<AgentLiveSessionCount> CountLiveAgentSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_agentRun is null && !File.Exists(SqliteAgentProjectStore.DefaultDatabasePath))
        {
            // No saved agent project means no earlier window could have left a provider session.
            // Avoid creating coordination state merely because an otherwise unused app is closing.
            return new AgentLiveSessionCount(0, Unknown: false);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CountLiveAgentSessionsBudget);
        try
        {
            if (_agentRun is null)
            {
                IReadOnlyList<AgentProjectState> projects;
                if (_agentStore is { } existingStore)
                {
                    projects = await existingStore.LoadAllAsync(budget.Token).ConfigureAwait(true);
                }
                else
                {
                    using var readOnlyCloseStore = new SqliteAgentProjectStore();
                    projects = await readOnlyCloseStore.LoadAllAsync(budget.Token).ConfigureAwait(true);
                }

                if (!projects.Any(project =>
                        project.Participant(AgentProvider.ClaudeCode).ConnectionState !=
                            AgentConnectionState.Offline ||
                        project.Lease?.Owner == AgentProvider.ClaudeCode))
                {
                    // A saved project is not evidence of a process. Codex inspection processes
                    // cannot outlive Filekin, and an offline Claude participant has no session to
                    // ask about. Avoid constructing the runtime solely because the window closed.
                    return new AgentLiveSessionCount(0, Unknown: false);
                }
            }

            // A reopened Filekin may not have opened /agents yet, while a Claude background session
            // from the previous window is still live. Build the read/inspection service from the
            // existing database so close never mistakes "not opened in this window" for zero.
            var run = _agentRun ?? await AgentRunAsync(budget.Token).ConfigureAwait(true);
            return await run.CountLiveProviderSessionsAsync(budget.Token).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A closing window must never crash on this question.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Not knowing is not the same as nothing running, and closing must not pretend otherwise.
            return new AgentLiveSessionCount(0, Unknown: true);
        }
    }

    /// <summary>How long a closing window waits for the providers to say what is still running.</summary>
    private static readonly TimeSpan CountLiveAgentSessionsBudget = TimeSpan.FromSeconds(2);

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
