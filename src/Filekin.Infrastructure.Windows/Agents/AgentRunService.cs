using System.Collections.Concurrent;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>One native agent session a Filekin window currently has open.</summary>
public sealed record AgentLiveSession(Guid ProjectId, AgentProvider Provider);

/// <summary>
/// What the providers say is still running. <paramref name="Unknown"/> is set when a provider could
/// not be asked, so a closing window can say "something may still be running" instead of "nothing is".
/// </summary>
public sealed record AgentLiveSessionCount(int Sessions, bool Unknown)
{
    public bool AnythingRunning => Sessions > 0 || Unknown;
}

/// <summary>What a provider could establish about one session Filekin is not watching.</summary>
public enum AgentSessionLiveness
{
    NotRunning,
    Running,
    Unknown,
}

/// <summary>The visible stages of an explicit Start work request.</summary>
public enum AgentStartStage
{
    CheckingUsage,
    StartingAgent,
    WaitingForConnection,
    GivingTurn,
}

/// <summary>
/// A provider-neutral progress report for the potentially slow work between pressing Start work and
/// an agent receiving the lease. It is deliberately transient and does not become project history.
/// </summary>
public sealed record AgentStartProgress(AgentStartStage Stage, AgentProvider? Provider = null);

/// <summary>
/// Runs one agent for a Filekin project: it starts the native session, waits for that agent to clock
/// in, and only then asks the runtime to grant the single working-tree turn.
/// </summary>
/// <remarks>
/// The coordination runtime deliberately never dispatches a native turn, so this is the one place
/// where a provider process is started, and it is reachable only from the explicit <c>/agents</c>
/// surface. Nothing here writes a file into the project folder. Stopping stays cooperative: Filekin
/// records the request, asks the provider to stop, and waits for the provider's own report that the
/// session ended before it releases the turn.
/// </remarks>
public sealed class AgentRunService : IAsyncDisposable
{
    private readonly TimeSpan _clockInPollInterval;
    private readonly TimeSpan _clockInTimeout;
    private readonly AgentProjectCoordinator _coordinator;
    // A cooperative stop is a request, not an instruction, so the provider is given a short while to
    // actually stop listing the session before anything is started beside it.
    private static readonly TimeSpan LostSessionStopCheckDelay = TimeSpan.FromSeconds(1);
    private const int LostSessionStopChecks = 10;

    /// <summary>
    /// What Filekin says to an agent that did its work and then ended the turn without handing over.
    /// It names the one missing step and nothing else: the agent still has its own conversation, so
    /// it needs no context, and Filekin must not describe work it did not watch.
    /// </summary>
    private const string HandoffReminderPrompt =
        "You ended your turn without handing over, so this project stopped and the other agent was "
        + "never given the turn. Read the state, then finish the turn properly: call "
        + "filekin_submit_handoff if work is left, or filekin_report_completed if the objective is "
        + "done. Do not redo work you already finished.";

    private readonly IAgentSessionLauncher _launcher;
    private readonly AgentCoordinationRuntime _runtime;

    /// <summary>
    /// Which agents have already been reminded to hand over since their last real handoff. One
    /// reminder is a mistake corrected; a second would be Filekin arguing with a model that is not
    /// going to comply, so the project stops and says so instead.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid ProjectId, AgentProvider Provider), byte> _handoffReminders = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, AgentProvider Provider), AgentSessionObservation> _sessionObservations = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, AgentProvider Provider), IAgentSessionHandle> _sessions = new();
    private readonly IAgentProjectStore _store;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public AgentRunService(
        AgentCoordinationRuntime runtime,
        IAgentProjectStore store,
        AgentProjectCoordinator coordinator,
        IAgentSessionLauncher launcher,
        TimeProvider? timeProvider = null,
        TimeSpan? clockInTimeout = null,
        TimeSpan? clockInPollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(launcher);

        _runtime = runtime;
        _store = store;
        _coordinator = coordinator;
        _launcher = launcher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clockInTimeout = clockInTimeout ?? TimeSpan.FromMinutes(3);
        _clockInPollInterval = clockInPollInterval ?? TimeSpan.FromSeconds(1);

        if (_clockInTimeout <= TimeSpan.Zero || _clockInPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(clockInTimeout), "Waiting periods must be positive.");
        }
    }

    /// <summary>
    /// Whether one provider has a session running right now which this window is not watching.
    /// </summary>
    /// <remarks>
    /// A Claude background session outlives the Filekin that started it, so a reopened Filekin has a
    /// saved conversation and no handle for a session that is still very much alive. Only the
    /// provider knows, so the provider is asked. Codex answers nothing here and that is correct, not
    /// a gap: Filekin runs its own App Server, so a Codex thread ends when Filekin does.
    /// </remarks>
    public async Task<AgentSessionLiveness> UnwatchedSessionLivenessAsync(
        AgentProjectState project,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(project);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        if (provider != AgentProvider.ClaudeCode)
        {
            return AgentSessionLiveness.NotRunning;
        }

        var participant = project.Participant(provider);
        if (participant.NativeSessionId is not { Length: > 0 } conversation ||
            _sessions.ContainsKey((project.Id, provider)))
        {
            return AgentSessionLiveness.NotRunning;
        }

        try
        {
            var running = await _launcher
                .ListClaudeBackgroundAgentsAsync(project.FolderPath, cancellationToken)
                .ConfigureAwait(false);
            return running.Any(agent =>
                agent.IsLiveBackgroundSession &&
                string.Equals(agent.SessionId, conversation, StringComparison.OrdinalIgnoreCase))
                    ? AgentSessionLiveness.Running
                    : AgentSessionLiveness.NotRunning;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed check is an honest unknown result, not stale success.
        catch (Exception)
#pragma warning restore CA1031
        {
            return AgentSessionLiveness.Unknown;
        }
    }

    /// <summary>
    /// The handle <c>claude attach</c> takes for the conversation Filekin stores, or
    /// <see langword="null"/> when Claude no longer reports a live background session for it.
    /// </summary>
    /// <remarks>
    /// A background session has two identities and they are not interchangeable: Filekin records the
    /// conversation, because that is what a handoff resumes, while <c>attach</c>, <c>logs</c> and
    /// <c>stop</c> take a short handle. Asking Claude to match them is the supported way round it, and
    /// it also answers whether the session is still there at all.
    /// </remarks>
    public async Task<string?> ResolveClaudeAttachIdAsync(
        string folderPath,
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        var agents = await _launcher.ListClaudeBackgroundAgentsAsync(folderPath, cancellationToken)
            .ConfigureAwait(false);
        return agents
            .FirstOrDefault(agent =>
                agent.IsLiveBackgroundSession &&
                string.Equals(agent.SessionId, nativeSessionId, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    /// <summary>This project's fixed Filekin MCP identity for one provider. It starts nothing.</summary>
    public AgentMcpLaunchConfiguration McpLaunch(AgentProjectState project, AgentProvider provider) =>
        _runtime.McpLaunch(project, provider);

    public IReadOnlyList<AgentLiveSession> LiveSessions() =>
        _sessions.Keys
            .Select(key => new AgentLiveSession(key.ProjectId, key.Provider))
            .OrderBy(session => session.ProjectId)
            .ThenBy(session => session.Provider)
            .ToArray();

    /// <summary>The agents this service currently has a live native session for.</summary>
    public IReadOnlyList<AgentProvider> RunningAgents(Guid projectId) =>
        _sessions.Keys
            .Where(key => key.ProjectId == projectId)
            .Select(key => key.Provider)
            .OrderBy(provider => provider)
            .ToArray();

    /// <summary>
    /// Registers a resumed Codex CLI hosted by a Filekin terminal as the live process for its saved
    /// conversation. The registration prevents a second launch and uses terminal closure as the
    /// provider-stop evidence needed to reconcile presence and any lease it acquired through MCP.
    /// </summary>
    public async Task<AgentTerminalSessionRegistration> RegisterTerminalSessionAsync(
        Guid projectId,
        AgentProvider provider,
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        if (provider != AgentProvider.Codex)
        {
            throw new InvalidOperationException(
                "Only Codex starts a new provider process in its session terminal. Claude attaches "
                + "to a background session whose own handle remains authoritative.");
        }

        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
        var savedSessionId = project.Participant(provider).NativeSessionId;
        if (!string.Equals(savedSessionId, nativeSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "That Codex conversation is not the session saved for this agent project.");
        }

        var handle = new AgentTerminalSessionRegistration.TerminalSessionHandle(provider, nativeSessionId);
        if (!_sessions.TryAdd((projectId, provider), handle))
        {
            await handle.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Codex already has a live session for this project. Open the existing session instead.");
        }

        WatchForStop(projectId, provider, handle);
        WatchForTurnEnd(projectId, provider, handle);
        return new AgentTerminalSessionRegistration(handle);
    }

    /// <summary>
    /// Returns the replayable read-only observation for the exact native session this service most
    /// recently started for an agent. A completed session remains observable until the service is
    /// disposed so an already-open task does not lose its history when the provider exits.
    /// </summary>
    public AgentSessionObservation? Session(Guid projectId, AgentProvider provider) =>
        _sessionObservations.GetValueOrDefault((projectId, provider));

    /// <summary>
    /// Starts one agent and gives it the turn. The owner's shared-checkout approval is required, and
    /// travels with the launch request, so no path reaches a provider without it.
    /// </summary>
    /// <param name="preferred">
    /// The agent the user chose, or <see langword="null"/> to start the one with more allowance left.
    /// </param>
    public async Task<AgentProjectState> StartAsync(
        Guid projectId,
        AgentProvider? preferred = null,
        CancellationToken cancellationToken = default) =>
        await StartCoreAsync(
                projectId,
                preferred,
                prompt: null,
                resumeExistingConversation: false,
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Starts one agent while reporting the slow, user-visible stages of the request.</summary>
    public async Task<AgentProjectState> StartAsync(
        Guid projectId,
        AgentProvider? preferred,
        IProgress<AgentStartProgress> progress,
        CancellationToken cancellationToken = default) =>
        await StartCoreAsync(
                projectId,
                preferred,
                prompt: null,
                resumeExistingConversation: false,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Ends a live provider session for <paramref name="conversation"/> that this window is not
    /// watching, and waits for the provider to stop listing it. Does nothing when there is no such
    /// session, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// The wait matters. Stopping is a cooperative request, and resuming a conversation whose session
    /// has not finished ending is exactly how a second copy gets made. If it will not go, say so
    /// rather than starting beside it.
    /// </remarks>
    private async Task EndSessionFilekinLostTrackOfAsync(
        string folderPath,
        string conversation,
        CancellationToken cancellationToken)
    {
        if (!await IsRunningUnwatchedOrRefuseAsync(
                folderPath,
                conversation,
                "Filekin could not ask Claude Code whether it is already running a session for this "
                + "project, so nothing was started. Starting without that answer risks a second agent "
                + "on the same files. Try again, or end the session in Claude Agent View.",
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        await _launcher
            .StopSessionsAsync(AgentProvider.ClaudeCode, folderPath, cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 0; attempt < LostSessionStopChecks; attempt++)
        {
            await Task.Delay(LostSessionStopCheckDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            if (!await IsRunningUnwatchedOrRefuseAsync(
                    folderPath,
                    conversation,
                    "Filekin asked Claude Code to end an earlier session for this project but could "
                    + "not check whether it ended, so nothing was started.",
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "Claude is still running an earlier session for this project and would not end it. "
            + "Starting now would put two agents on the same work, so nothing was started.");
    }

    private async Task<bool> IsRunningUnwatchedAsync(
        string folderPath,
        string conversation,
        CancellationToken cancellationToken)
    {
        var running = await _launcher
            .ListClaudeBackgroundAgentsAsync(folderPath, cancellationToken)
            .ConfigureAwait(false);
        return running.Any(agent =>
            agent.IsLiveBackgroundSession &&
            string.Equals(agent.SessionId, conversation, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <see cref="IsRunningUnwatchedAsync"/>, refusing with <paramref name="refusal"/> when Claude
    /// could not be asked at all.
    /// </summary>
    /// <remarks>
    /// Every caller of this asks in order to decide whether it is safe to act on a checkout, and for
    /// all of them a question that went unanswered has to stop the action rather than continue it.
    /// Reading a failed check as "nothing is running" is how two agents end up on one working tree,
    /// and how a turn gets released to the next agent while the last one is still writing.
    /// </remarks>
    private async Task<bool> IsRunningUnwatchedOrRefuseAsync(
        string folderPath,
        string conversation,
        string refusal,
        CancellationToken cancellationToken)
    {
        try
        {
            return await IsRunningUnwatchedAsync(folderPath, conversation, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any provider failure is the same answer here: Filekin does not know.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(refusal, exception);
        }
    }

    private async Task<AgentProjectState> StartCoreAsync(
        Guid projectId,
        AgentProvider? preferred,
        string? prompt,
        bool resumeExistingConversation,
        IProgress<AgentStartProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (preferred is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(preferred));
        }

        var prepared = await _runtime.PrepareProjectAsync(projectId, cancellationToken).ConfigureAwait(false);

        // An agent with no objective has nothing to do. It still clocks in, still spends a turn, and
        // still ends by asking a person what the job is, which is exactly what happened the first time
        // this was missing. Refuse before anything is spent.
        if (string.IsNullOrWhiteSpace(prepared.Project.Objective))
        {
            throw new InvalidOperationException(
                "This project has no objective yet, so an agent would have nothing to do. "
                + "Write what finished looks like, then start.");
        }

        // Nobody has clocked in yet, so the project itself holds no allowance numbers. Starting is an
        // explicit user action, which is the one moment Filekin may ask the provider tools directly.
        progress?.Report(new AgentStartProgress(AgentStartStage.CheckingUsage, preferred));
        var project = await _runtime
            .RefreshAllowanceForStartAsync(projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project.SharedCheckoutConsent is not { } consent)
        {
            throw new InvalidOperationException(
                "Filekin cannot start an agent until the owner approves working in this folder itself.");
        }

        if (project.Lease is not null)
        {
            throw new InvalidOperationException("This project already has an agent working.");
        }

        var now = _timeProvider.GetUtcNow();
        var running = RunningAgents(projectId);

        // Written work nobody has read decides this before allowance does. A handoff still waiting
        // names the agent whose turn this is, so an unattended start continues the relay instead of
        // picking whoever has more usage left and leaving the handoff behind.
        var provider = preferred
            ?? project.PendingHandoff?.To
            ?? (running.Count == 1 ? (AgentProvider?)running[0] : null)
            ?? _coordinator.ChooseAgentToStart(project, now)
            ?? throw new InvalidOperationException(
                "Neither agent has usage left right now. Wait for a usage window to reset.");

        if (!_coordinator.HasStartableAllowance(project, provider, now))
        {
            throw new InvalidOperationException(
                $"{DisplayName(provider)} has no usage left right now.");
        }

        if (_sessions.TryGetValue((projectId, provider), out var live))
        {
            // "Waiting" is a live provider session with no lease. Starting work continues that
            // exact session; only "Not here" launches a new provider conversation.
            progress?.Report(new AgentStartProgress(AgentStartStage.GivingTurn, provider));

            // Read this before the turn is granted: granting it clears the handoff, and the opening
            // text has to say whether this agent is picking work up or starting it.
            var pickingUpAHandoff = project.PendingHandoff?.To == provider;
            var given = await _runtime.GiveInitialTurnAsync(projectId, provider, cancellationToken)
                .ConfigureAwait(false);

            // Granting a turn changes Filekin's state and says nothing to the agent. A session that
            // can be given its next turn in place is told here, so it never has to be stopped and
            // started again to be handed work — and stopping it is what used to close the CLI
            // somebody was reading between turns. Claude has no such command: there is nothing that
            // sends a prompt to a live background session, so its next turn is still a stop and a
            // resume, and this only moves the turn for it.
            if (live is IInteractiveAgentSessionHandle steerable)
            {
                await steerable
                    .SendPromptAsync(
                        AgentRunPrompt.Create(given.Objective, pickingUpAHandoff),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return given;
        }

        progress?.Report(new AgentStartProgress(AgentStartStage.StartingAgent, provider));

        // Start work is one button, so it works out what starting means here rather than asking the
        // person to. A saved conversation is carried on, because throwing an agent's memory away must
        // be something somebody chooses, not what happens because a session ended. A clean slate is
        // still available and still deliberate: it is a new objective.
        var savedConversation = project.Participant(provider).NativeSessionId;
        var carryOn = resumeExistingConversation || savedConversation is { Length: > 0 };

        // A Claude background session outlives the Filekin that started it, so a reopened Filekin
        // has a saved conversation and no handle for a session that is still running. Launching
        // beside it would make two agents on one job, each holding its own Filekin MCP writer, and
        // Claude would not even refuse — asked to resume a session that is already running it starts
        // a copy and says so. Nobody should have to close the window to get out of that. Filekin ends
        // the session it lost track of, using the provider's own cooperative stop, and then carries
        // that same conversation on. One session, its memory kept, and no question asked of a person
        // who only pressed the obvious button.
        if (provider == AgentProvider.ClaudeCode && savedConversation is { Length: > 0 } conversation)
        {
            await EndSessionFilekinLostTrackOfAsync(
                    project.FolderPath,
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        var mcpServer = prepared.McpServers.Single(server => server.Provider == provider);

        // Nothing is running for this provider here — a live one took the branch above — so a stored
        // "connected" is a memory of a session from a window that has since closed. Clock-in is what
        // Filekin waits for to know the new session arrived, and that wait is satisfied the instant
        // the stored flag says connected: it would return before the launch had connected at all,
        // report a start that worked, and leave the reserved lease with a session that never came
        // back. Nothing threw, so no reservation is abandoned either. Say it is offline first, and
        // let the new session prove otherwise for itself.
        if (project.Participant(provider).ConnectionState != AgentConnectionState.Offline)
        {
            project = await _runtime
                .RecordSessionEndedAsync(projectId, provider, cancellationToken)
                .ConfigureAwait(false);
        }

        // Being started with a handoff already addressed to you is not the same as starting fresh:
        // the opening text has to say so, or the agent reads the objective as its next task and redoes
        // work the handoff says is already finished.
        var acceptingHandoff = project.PendingHandoff?.To == provider;

        // The work-capable prompt begins inside the provider launch. Reserve its one writer lease
        // first, so even a model that calls read_state immediately can never observe an unowned
        // checkout. Clock-in turns this reservation into Working atomically.
        await _runtime.ReserveInitialTurnAsync(projectId, provider, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            try
            {
                await LaunchAndWaitForClockInAsync(
                        project,
                        provider,
                        consent,
                        mcpServer,
                        acceptingHandoff,
                        carryOn,
                        prompt,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (carryOn && !resumeExistingConversation && exception is not OperationCanceledException)
            {
                // The saved conversation could not be carried on — the provider no longer has it, or will
                // not reopen it. Start work still has to start something, so it begins a new one and the
                // project keeps going. A caller that asked for a carry-on explicitly is not second-guessed.
                await StopQuietlyAsync(projectId, provider).ConfigureAwait(false);
                await LaunchAndWaitForClockInAsync(
                        project,
                        provider,
                        consent,
                        mcpServer,
                        acceptingHandoff,
                        resumeExistingConversation: false,
                        prompt,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new AgentStartProgress(AgentStartStage.GivingTurn, provider));
            return await _runtime
                .TrackInitialTurnAfterClockInAsync(projectId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await StopQuietlyAsync(projectId, provider).ConfigureAwait(false);
            await _runtime
                .AbandonInitialTurnReservationAsync(projectId, provider, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Starts one agent and waits for it to clock in. A launch that never reports back is asked to
    /// stop rather than left running with Filekin no longer watching it.
    /// </summary>
    private async Task LaunchAndWaitForClockInAsync(
        AgentProjectState project,
        AgentProvider provider,
        SharedCheckoutConsent consent,
        AgentMcpLaunchConfiguration mcpServer,
        bool acceptingHandoff,
        bool resumeExistingConversation,
        string? prompt,
        IProgress<AgentStartProgress>? progress,
        CancellationToken cancellationToken)
    {
        var handle = await _launcher.LaunchAsync(
                new AgentSessionLaunchRequest(
                    provider,
                    project.Id,
                    project.FolderPath,
                    $"Filekin {Path.GetFileName(project.FolderPath.TrimEnd(Path.DirectorySeparatorChar))}",
                    prompt ?? AgentRunPrompt.Create(project.Objective, acceptingHandoff),
                    mcpServer,
                    consent,
                    project.Participant(provider).PreferredModel,
                    project.Participant(provider).PreferredEffort,
                    resumeExistingConversation ? project.Participant(provider).NativeSessionId : null),
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new AgentStartProgress(AgentStartStage.WaitingForConnection, provider));

        try
        {
            if (!_sessions.TryAdd((project.Id, provider), handle))
            {
                throw new InvalidOperationException($"{DisplayName(provider)} is already running for this project.");
            }

            ObserveSession(project.Id, provider, handle);

            // Filekin opened this session, so Filekin records which session it is. The agent's own
            // clock-in reports presence only, and cannot name a different one.
            await _runtime.RecordNativeSessionAsync(
                    project.Id,
                    provider,
                    handle.NativeSessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            WatchForStop(project.Id, provider, handle);
            WatchForTurnEnd(project.Id, provider, handle);
            WatchForQuestions(project.Id, provider, handle);
            await WaitForClockInAsync(project.Id, provider, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The session may already be doing work, so it is asked to stop rather than abandoned.
            await StopQuietlyAsync(project.Id, provider).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sends text to the selected provider conversation. An active Codex turn is steered in place;
    /// an ended turn is resumed through the normal launch path and receives the single project lease.
    /// Claude's background command surface has no supported live-reply operation, so Filekin refuses
    /// that case instead of typing into Agent View.
    /// </summary>
    public async Task<AgentProjectState> SendPromptAsync(
        Guid projectId,
        AgentProvider provider,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");

        if (_sessions.TryGetValue((projectId, provider), out var handle))
        {
            if (project.ActiveAgent != provider)
            {
                throw new InvalidOperationException(
                    $"{DisplayName(provider)} is waiting and cannot receive a work prompt until it holds the turn.");
            }

            if (handle is not IInteractiveAgentSessionHandle interactive)
            {
                throw new InvalidOperationException(
                    "Claude Code does not expose a supported command for replying to a live background session. "
                    + "Use Claude Agent View for the current question, or wait for this turn to finish and send a follow-up here.");
            }

            await interactive.SendPromptAsync(prompt.Trim(), cancellationToken).ConfigureAwait(false);
            handle.Events.Publish(new AgentSessionEvent(
                $"filekin:prompt:{Guid.NewGuid():N}",
                _timeProvider.GetUtcNow(),
                AgentSessionEventKind.Message,
                AgentSessionEventStatus.Completed,
                "You",
                prompt.Trim()));
            return project;
        }

        if (project.Lease is not null)
        {
            throw new InvalidOperationException(
                $"{DisplayName(project.Lease.Owner)} still owns this project's turn. Prompt that agent or wait for the turn to move.");
        }

        if (project.Status == AgentProjectStatus.Completed)
        {
            throw new InvalidOperationException(
                "This project is complete. Start a new objective before sending more work.");
        }

        var resumed = await StartCoreAsync(
                projectId,
                provider,
                prompt.Trim(),
                resumeExistingConversation: true,
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (_sessionObservations.TryGetValue((projectId, provider), out var observation))
        {
            observation.Events.Publish(new AgentSessionEvent(
                $"filekin:prompt:{Guid.NewGuid():N}",
                _timeProvider.GetUtcNow(),
                AgentSessionEventKind.Message,
                AgentSessionEventStatus.Completed,
                "You",
                prompt.Trim()));
        }

        return resumed;
    }

    public async Task<AgentProjectState> RespondAsync(
        Guid projectId,
        AgentProvider provider,
        AgentSessionRequestResponse response,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(response);
        if (!_sessions.TryGetValue((projectId, provider), out var handle) ||
            handle is not IInteractiveAgentSessionHandle interactive)
        {
            throw new InvalidOperationException("That provider request is no longer attached to Filekin.");
        }

        await interactive.RespondAsync(response, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var current = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
            if (current.Status == AgentProjectStatus.NeedsAttention && current.Lease?.Owner == provider)
            {
                return await _runtime.ResolveBlockedAsync(projectId, provider, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (current.Lease?.Owner != provider)
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
    }

    /// <summary>Clears exactly one provider conversation; handoffs and new objectives never call it.</summary>
    public async Task<AgentProjectState> ClearSessionAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sessions.ContainsKey((projectId, provider)))
        {
            throw new InvalidOperationException(
                "Stop or finish this agent's current response before using /clear.");
        }

        await StopSessionsAsync(projectId, provider, cancellationToken).ConfigureAwait(false);
        var cleared = await _runtime.ClearNativeSessionAsync(projectId, provider, cancellationToken)
            .ConfigureAwait(false);
        _sessionObservations.TryRemove((projectId, provider), out _);
        return cleared;
    }

    /// <summary>
    /// Asks an agent that ended its turn without handing over to finish the turn properly, once.
    /// </summary>
    /// <remarks>
    /// Doing the work is not finishing the turn: an agent that appends its line and stops leaves a
    /// project with no owner, no pending handoff, and nothing able to move. That reads exactly like a
    /// finished job, which is how a relay dies in silence. Filekin does not invent the missing
    /// handoff and does not start the partner on a guess; it starts the same agent, in its own
    /// conversation, and names the one step it skipped. A second miss is not a slip, so the project
    /// stops there and says what happened rather than asking again.
    /// </remarks>
    private async Task AskForTheMissingHandoffAsync(
        Guid projectId,
        AgentProvider provider,
        AgentProjectState stopped)
    {
        // Only a project that is genuinely idle mid-objective is missing a handoff. A stop the user
        // asked for, a reported completion, and a project already waiting on a person are all states
        // somebody chose, and none of them wants an agent started again.
        if (stopped.Status != AgentProjectStatus.Ready || stopped.Lease is not null)
        {
            return;
        }

        if (!_handoffReminders.TryAdd((projectId, provider), 0))
        {
            _handoffReminders.TryRemove((projectId, provider), out _);
            await _runtime
                .MarkStoppedWithoutHandoffAsync(
                    projectId,
                    provider,
                    $"{DisplayName(provider)} ended its turn without handing over, and did it again "
                    + "after Filekin asked. The other agent has not been given the turn, because "
                    + "Filekin never writes a handoff nobody submitted.",
                    CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await StartCoreAsync(
                    projectId,
                    provider,
                    HandoffReminderPrompt,
                    resumeExistingConversation: true,
                    progress: null,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The reminder is the recovery, so a reminder that cannot be delivered is the end of what
            // Filekin can do by itself. Say that, with the reason, instead of leaving a project that
            // looks finished.
            _handoffReminders.TryRemove((projectId, provider), out _);
            StopFault = exception;
            await _runtime
                .MarkStoppedWithoutHandoffAsync(
                    projectId,
                    provider,
                    $"{DisplayName(provider)} ended its turn without handing over, and Filekin could "
                    + $"not start it again to ask: {exception.Message}",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts the agent a submitted handoff is addressed to, if it is not already here. Filekin does
    /// not keep a second agent running and idle to make the relay possible: it starts the partner at
    /// the moment there is actually something to hand over.
    /// </summary>
    /// <remarks>
    /// A partner that cannot be started is not an error here. The turn still ends, and the coordinator
    /// already pauses the project safely when the recipient is not ready, which keeps the written
    /// handoff and asks nobody to guess.
    /// </remarks>
    private async Task EnsureHandoffPartnerIsHereAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false);

        // Whether the recipient is really here is a question about live sessions, not about what the
        // project last recorded. An agent that clocked in and whose session has since ended still
        // reads as connected, and handing the turn to it would leave the work sitting still.
        if (project?.PendingHandoff is not { } handoff ||
            project.SharedCheckoutConsent is not { } consent ||
            _sessions.ContainsKey((projectId, handoff.To)))
        {
            return;
        }

        // A provider process from the earlier turn has ended even though its conversation survives.
        // Require this resumed process to clock in freshly; otherwise the participant's old Ready
        // flag lets a launch that never reconnects inherit the lease.
        if (project.Participant(handoff.To).ConnectionState != AgentConnectionState.Offline)
        {
            project = await _runtime
                .RecordSessionEndedAsync(projectId, handoff.To, cancellationToken)
                .ConfigureAwait(false);
        }

        // This launch resumes a saved conversation, and Claude asked to resume a session it is still
        // running starts a copy instead. Two agents on one job, each holding its own writer, is the
        // fault this guard exists for; the same one the start path already takes.
        if (handoff.To == AgentProvider.ClaudeCode &&
            project.Participant(handoff.To).NativeSessionId is { Length: > 0 } stale)
        {
            await EndSessionFilekinLostTrackOfAsync(project.FolderPath, stale, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            var prepared = await _runtime.PrepareProjectAsync(projectId, cancellationToken)
                .ConfigureAwait(false);
            await LaunchAndWaitForClockInAsync(
                    prepared.Project,
                    handoff.To,
                    consent,
                    prepared.McpServers.Single(server => server.Provider == handoff.To),
                    acceptingHandoff: true,
                    resumeExistingConversation: true,
                    prompt: null,
                    progress: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StopFault = exception;
        }
    }

    /// <summary>
    /// Asks the agent holding the turn to stop. The project is kept and can be resumed. The turn is
    /// released only when that provider reports its session actually ended.
    /// </summary>
    public async Task<AgentProjectState> RequestStopAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
        var owner = project.ActiveAgent
            ?? throw new InvalidOperationException("No agent currently holds this project's turn.");
        var state = await _runtime.RequestStopAsync(projectId, owner, cancellationToken).ConfigureAwait(false);

        if (_sessions.TryGetValue((projectId, owner), out var handle))
        {
            // Filekin is watching this one, so the stop is proven the usual way: by that session's
            // own report that it ended.
            await handle.RequestStopAsync(cancellationToken).ConfigureAwait(false);
            return state;
        }

        // Nobody here is watching a session for the agent that holds the turn. A turn like that can
        // never be released by a session report, and waiting for one leaves the project stuck. So
        // Filekin asks that tool to end whatever it still has open in this folder, and its answer is
        // the evidence: no session left means the turn belongs to nothing and is released.
        var asked = await _launcher.StopSessionsAsync(owner, project.FolderPath, cancellationToken)
            .ConfigureAwait(false);

        // Asking is not stopping, and the answer above is a request that was sent, not a session that
        // ended. Claude can report a session stopped and go on listing it, so releasing the turn on
        // the request alone hands this checkout to the next agent while the last one may still be
        // writing to it. The provider's own listing is the evidence, and it is read the same way the
        // lost-session path reads it. A provider with no cooperative stop answers null because its
        // sessions cannot outlive the Filekin that started them, so there is nothing left to prove.
        if (asked is not null &&
            owner == AgentProvider.ClaudeCode &&
            project.Participant(owner).NativeSessionId is { Length: > 0 } conversation)
        {
            for (var attempt = 0; attempt < LostSessionStopChecks; attempt++)
            {
                if (!await IsRunningUnwatchedOrRefuseAsync(
                        project.FolderPath,
                        conversation,
                        "Claude Code was asked to stop, but Filekin could not check whether its "
                        + "session ended, so the turn has not moved. A turn released on an unanswered "
                        + "check hands this folder to the next agent while the last one may still be "
                        + "writing to it.",
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return await _runtime
                        .ConfirmProviderStoppedAsync(projectId, owner, cancellationToken)
                        .ConfigureAwait(false);
                }

                await Task.Delay(LostSessionStopCheckDelay, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }

            // The turn stays where it is. A session that will not end still owns this folder, and
            // saying otherwise would be the one mistake this whole lease exists to prevent.
            throw new InvalidOperationException(
                "Claude Code was asked to stop and is still running this project's session, so the "
                + "turn has not moved. Use End to close that session, or end it in Claude Agent View.");
        }

        return await _runtime.ConfirmProviderStoppedAsync(projectId, owner, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ends one agent's sessions in this project, whether or not it holds the turn. A session outlives
    /// the turn and outlives the window that opened it, and each live session keeps its own Filekin MCP
    /// companion alive, so this is how a person clears them without hunting for processes.
    /// </summary>
    /// <returns>
    /// How many sessions were asked to stop, or <see langword="null"/> when this provider has no
    /// cooperative stop and its sessions simply end with their turn.
    /// </returns>
    public async Task<int?> StopSessionsAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");

        // The turn holder keeps the cooperative path: the request is recorded first, and the turn is
        // released only on the session's own report or, when nobody is watching one, on that tool
        // reporting it has nothing left open here.
        if (project.ActiveAgent == provider)
        {
            await RequestStopAsync(projectId, cancellationToken).ConfigureAwait(false);
        }
        else if (_sessions.TryGetValue((projectId, provider), out var handle))
        {
            await handle.RequestStopAsync(cancellationToken).ConfigureAwait(false);
        }

        var stopped = await _launcher
            .StopSessionsAsync(provider, project.FolderPath, cancellationToken)
            .ConfigureAwait(false);

        // An agent that holds no turn is simply no longer here. The lease owner's own stop is proven
        // separately, by the provider, and must not be assumed from this.
        if (project.ActiveAgent != provider &&
            project.Participant(provider).ConnectionState != AgentConnectionState.Offline)
        {
            await _runtime.RecordSessionEndedAsync(projectId, provider, cancellationToken)
                .ConfigureAwait(false);
        }

        return stopped;
    }

    /// <summary>
    /// How many provider sessions are still open across every project Filekin knows about.
    /// </summary>
    /// <remarks>
    /// Current-window handles are known to be open without a provider round trip. Only persisted
    /// Claude presence needs an external check: Claude background sessions can outlive Filekin, while
    /// Codex coordination App Servers cannot. Offline saved projects therefore cost nothing at close.
    /// A provider that needs to be asked but cannot answer is reported as unknown rather than as zero.
    /// </remarks>
    public async Task<AgentLiveSessionCount> CountLiveProviderSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var watched = _sessions.Keys.ToHashSet();
        var projects = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        // The filter is resolved to a plain array before any check starts. Interleaving it with
        // CountOneAsync, as one combined Where/Select pipeline used to, let a predicate failure on a
        // later project abandon an earlier project's check mid-flight: already started, holding or
        // waiting on the semaphore below, but never reaching the checks array Task.WhenAll awaits.
        // Nothing here awaited that orphaned check, so the semaphore was disposed out from under it,
        // and it threw ObjectDisposedException on a task nobody was watching. A plain array cannot
        // fail while it is being read, so once this line returns, every check the loop below starts is
        // guaranteed a place in checks.
        var toCheck = projects
            .Where(project =>
                !watched.Contains((project.Id, AgentProvider.ClaudeCode)) &&
                CouldHaveUnwatchedClaudeSession(project))
            .ToArray();

        using var concurrency = new SemaphoreSlim(initialCount: 4);
        var checks = toCheck
            .Select(project => CountOneAsync(project.FolderPath, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        return new AgentLiveSessionCount(
            watched.Count + results.Sum(result => result.Sessions),
            results.Any(result => result.Unknown));

        async Task<AgentLiveSessionCount> CountOneAsync(
            string folderPath,
            CancellationToken token)
        {
            await concurrency.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var open = await _launcher
                    .CountLiveSessionsAsync(AgentProvider.ClaudeCode, folderPath, token)
                    .ConfigureAwait(false);
                return new AgentLiveSessionCount(open ?? 0, Unknown: false);
            }
#pragma warning disable CA1031 // One provider that will not answer must not hide the rest.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                return new AgentLiveSessionCount(0, Unknown: true);
            }
            finally
            {
                concurrency.Release();
            }
        }
    }

    /// <summary>
    /// Ends every session this window has open, each through its own provider's stop. It is the
    /// closing window's cleanup, so one provider that will not stop must not prevent the rest from
    /// being asked.
    /// </summary>
    /// <returns>
    /// The reason the first agent could not be ended, or <see langword="null"/> when every session was
    /// asked to stop. A caller that is closing needs to know whether it is leaving anything behind.
    /// </returns>
    public async Task<string?> StopAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? firstFailure = null;

        // Current-window handles identify everything this process owns. Persisted Claude presence
        // adds sessions left by an earlier window; offline projects and unwatched Codex records do
        // not represent a process that can still be alive.
        var projects = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var projectsById = projects.ToDictionary(project => project.Id);
        var targets = _sessions.Keys.ToHashSet();
        foreach (var project in projects.Where(CouldHaveUnwatchedClaudeSession))
        {
            targets.Add((project.Id, AgentProvider.ClaudeCode));
        }

        foreach (var target in targets)
        {
            if (!projectsById.ContainsKey(target.ProjectId))
            {
                continue;
            }

            try
            {
                await StopSessionsAsync(target.ProjectId, target.Provider, cancellationToken)
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Every remaining session must still be asked to stop.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                StopFault = exception;
                firstFailure ??= $"{DisplayName(target.Provider)} could not be ended: {exception.Message}";
            }
        }

        return firstFailure;
    }

    /// <summary>
    /// Asks the agent holding the turn to hand over early. This is the same cooperative request
    /// Filekin makes when allowance runs low, made because the user asked for it instead.
    /// </summary>
    public async Task<AgentProjectState> PassTheTurnAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var owner = await ActiveAgentAsync(projectId, cancellationToken).ConfigureAwait(false);
        return await _runtime.RequestHandoffAsync(
                projectId,
                owner,
                AgentHandoffReason.UserRequested,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Disposal releases Filekin's grip on the native sessions. It does not stop the agents: the
        // owner's projects and their running work outlive this window.
        var disposals = new List<Task>();
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(key, out var handle))
            {
                disposals.Add(handle.DisposeAsync().AsTask());
            }
        }

        await Task.WhenAll(disposals).ConfigureAwait(false);

        _sessionObservations.Clear();
    }

    internal static bool CouldHaveUnwatchedClaudeSession(AgentProjectState project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var claude = project.Participant(AgentProvider.ClaudeCode);
        return claude.ConnectionState != AgentConnectionState.Offline ||
            project.Lease?.Owner == AgentProvider.ClaudeCode;
    }

    internal static string DisplayName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => provider.ToString(),
    };

    private async Task<AgentProvider> ActiveAgentAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
        return project.ActiveAgent
            ?? throw new InvalidOperationException("No agent currently holds this project's turn.");
    }

    private async Task WaitForClockInAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + _clockInTimeout;
        while (true)
        {
            var project = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");
            if (project.Participant(provider).ConnectionState != AgentConnectionState.Offline)
            {
                return;
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                throw new TimeoutException(
                    $"{DisplayName(provider)} started but did not report back to Filekin in time.");
            }

            await Task.Delay(_clockInPollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns the provider's own report that a session ended into the proof Filekin needs before it
    /// releases the turn. A session that ends without Filekin asking is still a stop, so it is applied
    /// the same way, and the coordinator decides what that means for the project.
    /// </summary>
    /// <summary>
    /// Moves the turn when a provider says the turn is finished and its session is still alive. The
    /// stop watcher below still handles a session that really ends; this is the other way a turn can
    /// be over, and it is the one that leaves a CLI somebody opened open.
    /// </summary>
    private void WatchForTurnEnd(Guid projectId, AgentProvider provider, IAgentSessionHandle handle)
    {
        if (handle is not ITurnScopedAgentSessionHandle scoped)
        {
            return;
        }

        _ = ObserveTurnEndAsync();
        return;

        async Task ObserveTurnEndAsync()
        {
            try
            {
                await scoped.TurnFinished.ConfigureAwait(false);

                // Only the turn holder's finished turn moves a lease. Filekin also starts a second
                // agent to receive a handoff, and that session can finish a turn it never held.
                var project = await _store.LoadAsync(projectId).ConfigureAwait(false);
                if (project?.Lease?.Owner != provider)
                {
                    return;
                }

                // An agent that finished without handing over has to be asked again, and asking means
                // starting it, which cannot happen while this session is still registered. So that
                // case keeps the path it always had: the session is asked to stop, and the stop
                // watcher takes it from there. A real handoff needs none of that, and a handoff is
                // exactly the moment somebody is reading the CLI this used to close.
                if (project.PendingHandoff is null)
                {
                    await handle.RequestStopAsync(CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                // The turn is about to move. If it is going to somebody who is not here, this is the
                // moment they are needed, so this is the moment they are started. One that is already
                // here is not started, and so is never given an opening prompt by the launch.
                var recipient = project.PendingHandoff.To;
                var recipientWasAlreadyHere = _sessions.ContainsKey((projectId, recipient));

                // A recipient that cannot be given a turn in place is no use sitting here: Claude has
                // no command that prompts a live background session, so a turn handed to one would
                // never be read. Its own way of taking a turn is a stop and a resume, which keeps its
                // memory, so the stale session goes now and the launch below brings it back.
                if (recipientWasAlreadyHere &&
                    _sessions.TryGetValue((projectId, recipient), out var stuck) &&
                    stuck is not IInteractiveAgentSessionHandle)
                {
                    await StopQuietlyAsync(projectId, recipient).ConfigureAwait(false);
                    recipientWasAlreadyHere = false;
                }

                await EnsureHandoffPartnerIsHereAsync(projectId, CancellationToken.None)
                    .ConfigureAwait(false);
                var handedOver = await _runtime
                    .ConfirmTurnFinishedAsync(projectId, provider)
                    .ConfigureAwait(false);

                // Granting the lease is Filekin's own bookkeeping and reaches no agent. Before this
                // change the recipient was always a fresh process that opened with its instructions,
                // so there was nothing to say. Now that a session survives its turn, the one already
                // sitting here has to be told the turn is its own, or the relay stops with both
                // agents waiting on each other.
                if (recipientWasAlreadyHere &&
                    handedOver.Lease?.Owner == recipient &&
                    _sessions.TryGetValue((projectId, recipient), out var waiting) &&
                    waiting is IInteractiveAgentSessionHandle steerable)
                {
                    await steerable
                        .SendPromptAsync(
                            AgentRunPrompt.Create(handedOver.Objective, acceptingHandoff: true),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // A real handoff ends the argument, so the next turn starts with its own reminder.
                _handoffReminders.TryRemove((projectId, provider), out _);
            }
            catch (OperationCanceledException)
            {
                // Filekin stopped watching. That is not a fault, and not a finished turn either.
            }
            catch (Exception exception)
            {
                StopFault = exception;
            }
        }
    }

    private void WatchForStop(Guid projectId, AgentProvider provider, IAgentSessionHandle handle)
    {
        _ = ObserveStopAsync();
        return;

        async Task ObserveStopAsync()
        {
            // Asking for a forgotten handoff starts this agent again, and starting it has to happen
            // after its finished session has been let go below. While that handle is still registered
            // a start reads as "this agent is already here" and tries to give the turn to the session
            // that just ended, which cannot take it.
            AgentProjectState? owesAHandoff = null;
            try
            {
                await handle.Stopped.ConfigureAwait(false);

                // Only the turn holder's stop moves a lease. Filekin also starts a second agent to
                // receive a handoff, and that session can end while it holds nothing, which changes
                // only whether that agent is still here.
                var project = await _store.LoadAsync(projectId).ConfigureAwait(false);
                if (project?.Lease?.Owner != provider)
                {
                    if (project?.Participant(provider).ConnectionState != AgentConnectionState.Offline)
                    {
                        await _runtime.RecordSessionEndedAsync(projectId, provider).ConfigureAwait(false);
                    }

                    return;
                }

                // The turn is about to move. If it is going to somebody who is not here, this is the
                // moment they are needed, so this is the moment they are started.
                await EnsureHandoffPartnerIsHereAsync(projectId, CancellationToken.None)
                    .ConfigureAwait(false);

                // Read this before the stop is applied: afterwards the pending handoff is cleared
                // either way, and the difference between a finished relay turn and an abandoned one
                // is exactly whether a handoff was ever submitted.
                var endedWithoutHandingOver = project.PendingHandoff is null;
                var stopped = await _runtime.ConfirmProviderStoppedAsync(projectId, provider)
                    .ConfigureAwait(false);
                if (stopped.Participant(provider).ConnectionState != AgentConnectionState.Offline)
                {
                    await _runtime.RecordSessionEndedAsync(projectId, provider).ConfigureAwait(false);
                }

                if (endedWithoutHandingOver)
                {
                    owesAHandoff = stopped;
                }
                else
                {
                    // A real handoff ends the argument, so the next turn starts with its own reminder.
                    _handoffReminders.TryRemove((projectId, provider), out _);
                }
            }
            catch (OperationCanceledException)
            {
                // Filekin stopped watching. That is not a fault, and not a proven stop either.
            }
            catch (Exception exception)
            {
                StopFault = exception;
            }
            finally
            {
                if (_sessions.TryRemove((projectId, provider), out var finished))
                {
                    await finished.DisposeAsync().ConfigureAwait(false);
                }

                if (handle is AgentTerminalSessionRegistration.TerminalSessionHandle terminal)
                {
                    terminal.ReportReconciled();
                }
            }

            // The finished session is gone now, so starting this agent again means what it says.
            if (owesAHandoff is { } stoppedProject)
            {
                try
                {
                    await AskForTheMissingHandoffAsync(projectId, provider, stoppedProject)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Filekin stopped watching before the reminder went out.
                }
            }
        }
    }

    /// <summary>
    /// The provider's own latest word about the session Filekin started, or <see langword="null"/>
    /// when there is no live session or it has said nothing. Filekin passes it through unchanged.
    /// </summary>
    public string? LastReport(Guid projectId, AgentProvider provider) =>
        _sessions.TryGetValue((projectId, provider), out var handle) ? handle.LastReport : null;

    /// <summary>
    /// Keeps one stable presentation feed for the provider conversation across every resumed turn.
    /// A session task that is already open therefore receives later turns instead of remaining bound
    /// to the short-lived process handle that happened to open it.
    /// </summary>
    private void ObserveSession(Guid projectId, AgentProvider provider, IAgentSessionHandle handle)
    {
        var observation = _sessionObservations.AddOrUpdate(
            (projectId, provider),
            _ => new AgentSessionObservation(
                handle.NativeSessionId,
                new AgentSessionEventFeed(),
                DateTimeOffset.Now),
            (_, current) => string.Equals(
                current.NativeSessionId,
                handle.NativeSessionId,
                StringComparison.Ordinal)
                    ? current
                    : new AgentSessionObservation(
                        handle.NativeSessionId,
                        new AgentSessionEventFeed(),
                        DateTimeOffset.Now));

        handle.Events.EventReceived += (_, sessionEvent) => observation.Events.Publish(sessionEvent);
        foreach (var sessionEvent in handle.Events.Snapshot())
        {
            observation.Events.Publish(sessionEvent);
        }
    }

    /// <summary>
    /// Records that a session cannot go on without a person. The turn is kept, because a question is
    /// not proof that the session stopped, and Filekin never answers one on the user's behalf.
    /// </summary>
    private void WatchForQuestions(Guid projectId, AgentProvider provider, IAgentSessionHandle handle)
    {
        _ = ObserveAsync();
        return;

        async Task ObserveAsync()
        {
            try
            {
                var reason = await handle.NeedsPerson.ConfigureAwait(false);
                var project = await _store.LoadAsync(projectId).ConfigureAwait(false);

                // Only the agent holding the turn can be marked blocked. One that has not been given
                // the turn yet is already visible as a start that has not finished.
                if (project?.Lease?.Owner == provider)
                {
                    await _runtime.MarkBlockedAsync(projectId, provider, reason).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Filekin stopped watching. That is not a question.
            }
            catch (Exception exception)
            {
                StopFault = exception;
            }
        }
    }

    /// <summary>
    /// The last failure while applying a provider's reported stop. It is surfaced rather than retried:
    /// a lease that could not be released is exactly the state a person must look at.
    /// </summary>
    public Exception? StopFault { get; private set; }

    private async Task StopQuietlyAsync(Guid projectId, AgentProvider provider)
    {
        if (!_sessions.TryRemove((projectId, provider), out var handle))
        {
            return;
        }

        try
        {
            await handle.RequestStopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            StopFault = exception;
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }
}
