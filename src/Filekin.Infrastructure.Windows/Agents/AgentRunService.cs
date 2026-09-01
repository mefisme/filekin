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
    private readonly IAgentSessionLauncher _launcher;
    private readonly AgentCoordinationRuntime _runtime;
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
    /// Every native agent session this window has open, across every project. Closing Filekin used to
    /// walk away from these, and a Claude session left behind keeps respawning its own Filekin MCP
    /// companion, so a person must be able to see and end them at the moment they close the window.
    /// </summary>
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
        CancellationToken cancellationToken = default)
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
        var project = await _runtime.RefreshAllowanceAsync(projectId, cancellationToken).ConfigureAwait(false);
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
        var provider = preferred
            ?? _coordinator.ChooseAgentToStart(project, now)
            ?? throw new InvalidOperationException(
                "Neither agent has usage left right now. Wait for a usage window to reset.");

        if (!_coordinator.HasStartableAllowance(project, provider, now))
        {
            throw new InvalidOperationException(
                $"{DisplayName(provider)} has no usage left right now.");
        }

        if (_sessions.ContainsKey((projectId, provider)))
        {
            throw new InvalidOperationException($"{DisplayName(provider)} is already running for this project.");
        }

        await LaunchAndWaitForClockInAsync(
                project,
                provider,
                consent,
                prepared.McpServers.Single(server => server.Provider == provider),
                acceptingHandoff: false,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var selected = await _runtime.SelectInitialAgentAsync(projectId, provider, cancellationToken)
                .ConfigureAwait(false);
            return selected.Project;
        }
        catch
        {
            await StopQuietlyAsync(projectId, provider).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var handle = await _launcher.LaunchAsync(
                new AgentSessionLaunchRequest(
                    provider,
                    project.Id,
                    project.FolderPath,
                    $"Filekin {Path.GetFileName(project.FolderPath.TrimEnd(Path.DirectorySeparatorChar))}",
                    AgentRunPrompt.Create(project.Objective, acceptingHandoff),
                    mcpServer,
                    consent,
                    project.Participant(provider).PreferredModel,
                    project.Participant(provider).PreferredEffort),
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!_sessions.TryAdd((project.Id, provider), handle))
            {
                throw new InvalidOperationException($"{DisplayName(provider)} is already running for this project.");
            }

            _sessionObservations[(project.Id, provider)] =
                new AgentSessionObservation(handle.NativeSessionId, handle.Events);

            // Filekin opened this session, so Filekin records which session it is. The agent's own
            // clock-in reports presence only, and cannot name a different one.
            await _runtime.RecordNativeSessionAsync(
                    project.Id,
                    provider,
                    handle.NativeSessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            WatchForStop(project.Id, provider, handle);
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
        await _launcher.StopSessionsAsync(owner, project.FolderPath, cancellationToken)
            .ConfigureAwait(false);
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
    /// This asks the providers themselves. A Claude background session outlives the turn that started
    /// it, so it is no longer in this window's own session list long before it stops existing: closing
    /// on that bookkeeping reported nothing running while two idle sessions and their helper processes
    /// were still there. A provider that cannot be asked is reported as unknown rather than as zero.
    /// </remarks>
    public async Task<AgentLiveSessionCount> CountLiveProviderSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var projects = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var counted = 0;
        var unknown = false;
        foreach (var project in projects)
        {
            foreach (var provider in Enum.GetValues<AgentProvider>())
            {
                try
                {
                    var open = await _launcher
                        .CountLiveSessionsAsync(provider, project.FolderPath, cancellationToken)
                        .ConfigureAwait(false);
                    counted += open ?? 0;
                }
#pragma warning disable CA1031 // One provider that will not answer must not hide the rest.
                catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    unknown = true;
                }
            }
        }

        return new AgentLiveSessionCount(counted, unknown);
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

        // The same reach as the count above: every project, every provider, not only the sessions
        // this window still happens to be watching. A session that finished its turn is exactly the
        // one a person leaves behind.
        var projects = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var project in projects)
        {
            foreach (var provider in Enum.GetValues<AgentProvider>())
            {
                try
                {
                    await StopSessionsAsync(project.Id, provider, cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Every remaining session must still be asked to stop.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    StopFault = exception;
                    firstFailure ??= $"{DisplayName(provider)} could not be ended: {exception.Message}";
                }
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
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(key, out var handle))
            {
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }

        _sessionObservations.Clear();
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
    private void WatchForStop(Guid projectId, AgentProvider provider, IAgentSessionHandle handle)
    {
        _ = ObserveStopAsync();
        return;

        async Task ObserveStopAsync()
        {
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
                await _runtime.ConfirmProviderStoppedAsync(projectId, provider).ConfigureAwait(false);
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
