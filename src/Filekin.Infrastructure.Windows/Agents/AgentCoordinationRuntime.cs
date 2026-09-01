using System.Collections.Concurrent;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

public enum AgentProviderRefreshStatus
{
    NotClockedIn,
    Updated,
    Unavailable,
}

public sealed record AgentProviderRefreshResult(
    AgentProvider Provider,
    AgentProviderRefreshStatus Status);

public sealed record AgentProjectRuntimeState(
    AgentProjectState Project,
    IReadOnlyList<AgentProviderRefreshResult> ProviderRefreshes,
    IReadOnlyList<AgentMcpLaunchConfiguration> McpServers);

/// <summary>
/// App-owned sequencing boundary for persisted coordination. It reconciles stale leases before any
/// project operation, refreshes non-secret provider facts, prepares fixed MCP identities, and applies
/// coordinator transitions. While one agent actually holds the working-tree lease it repeats that
/// refresh on a timer, so a long-running turn's budget is watched instead of only being read when
/// something else happens to ask. It deliberately does not dispatch native agent turns.
/// </summary>
public sealed class AgentCoordinationRuntime : IAsyncDisposable
{
    private static readonly AgentProvider[] SupportedProviders =
        [AgentProvider.Codex, AgentProvider.ClaudeCode];

    private readonly AgentProjectCoordinator _coordinator;
    private readonly ConcurrentDictionary<Guid, Exception> _inTurnRefreshFaults = new();
    private readonly TimeSpan _inTurnRefreshInterval;
    private readonly Dictionary<Guid, ITimer> _inTurnRefreshTimers = [];
    private readonly IAgentMcpLaunchConfigurationFactory _mcpLaunchFactory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<(Guid ProjectId, AgentProvider Provider), IAgentUsageSource> _usageSources = [];
    private readonly IAgentUsageSourceFactory _usageSourceFactory;
    private readonly IAgentProjectStore _store;
    private readonly TimeProvider _timeProvider;
    private volatile Task _inTurnRefreshActivity = Task.CompletedTask;
    private bool _disposed;
    private bool _started;

    public AgentCoordinationRuntime(
        SqliteAgentProjectStore store,
        AgentCoordinationPolicy policy,
        string mcpExecutablePath,
        TimeProvider? timeProvider = null,
        TimeSpan? inTurnRefreshInterval = null)
        : this(
            RequireStore(store),
            new AgentProjectCoordinator(policy),
            new NativeAgentUsageSourceFactory(RequireStore(store)),
            new AgentMcpLaunchConfigurationFactory(mcpExecutablePath, store.DatabasePath),
            timeProvider ?? TimeProvider.System,
            inTurnRefreshInterval ?? DefaultInTurnRefreshInterval(RequirePolicy(policy)))
    {
        if (_inTurnRefreshInterval > policy.MaximumUsageAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inTurnRefreshInterval),
                "An in-turn refresh slower than the maximum usage age would always read stale usage.");
        }
    }

    internal AgentCoordinationRuntime(
        IAgentProjectStore store,
        AgentProjectCoordinator coordinator,
        IAgentUsageSourceFactory usageSourceFactory,
        IAgentMcpLaunchConfigurationFactory mcpLaunchFactory,
        TimeProvider timeProvider,
        TimeSpan inTurnRefreshInterval)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(usageSourceFactory);
        ArgumentNullException.ThrowIfNull(mcpLaunchFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (inTurnRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inTurnRefreshInterval),
                "The in-turn refresh interval must be positive.");
        }

        _store = store;
        _coordinator = coordinator;
        _usageSourceFactory = usageSourceFactory;
        _mcpLaunchFactory = mcpLaunchFactory;
        _timeProvider = timeProvider;
        _inTurnRefreshInterval = inTurnRefreshInterval;
    }

    /// <summary>
    /// The unexpected failure that stopped one project's periodic in-turn refresh, if one happened.
    /// Faults are per project: one project's healthy refresh never clears another project's stopped
    /// watcher. Provider inspection failures are not reported here; those already become
    /// provider-neutral <see cref="AgentConnectionState.Unavailable"/> state. That project's next
    /// explicit operation clears its fault and restarts its periodic refresh.
    /// </summary>
    public Exception? InTurnRefreshFault(Guid projectId) =>
        _inTurnRefreshFaults.TryGetValue(projectId, out var fault) ? fault : null;

    /// <summary>
    /// The most recent periodic in-turn refresh. Disposal drains it, and a test can await one
    /// deterministic tick through it.
    /// </summary>
    internal Task InTurnRefreshActivity => _inTurnRefreshActivity;

    /// <summary>
    /// Performs the one app-start reconciliation pass. Project operations are refused until this
    /// succeeds, so an MCP configuration or new lease cannot race a stale persisted writer.
    /// </summary>
    public async Task<IReadOnlyList<AgentProjectState>> StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return Array.Empty<AgentProjectState>();
            }

            var reconciled = await _store.ReconcileAfterRestartAsync(cancellationToken)
                .ConfigureAwait(false);
            _started = true;
            return reconciled;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// The agent project bound to this folder, or <c>null</c> when the folder has not opted in.
    /// Reading is not opting in: this creates no project, probes no provider, and starts no process.
    /// </summary>
    public async Task<AgentProjectState?> FindProjectAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return await WithOperationGateAsync(
                () => _store.LoadByFolderAsync(folderPath, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Binds a new agent project to one folder. This is the explicit opt-in, so it is the only method
    /// that creates coordination state. It still starts no provider and grants no lease: both agents
    /// remain clocked out until something else connects them.
    /// </summary>
    public async Task<AgentProjectState> CreateProjectAsync(
        string folderPath,
        string objective,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(objective);
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"'{folderPath}' is not an existing folder.");
        }

        return await WithOperationGateAsync(
                async () =>
                {
                    var existing = await _store.LoadByFolderAsync(folderPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing is not null)
                    {
                        throw new InvalidOperationException("This folder is already an agent project.");
                    }

                    var created = AgentProjectCoordinator.Create(folderPath, objective);
                    await _store.SaveAsync(created, cancellationToken).ConfigureAwait(false);
                    return created;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Records what the user wants the agents to do. It changes no turn, lease, or provider state.</summary>
    public async Task<AgentProjectState> SetObjectiveAsync(
        Guid projectId,
        string objective,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        ArgumentNullException.ThrowIfNull(objective);
        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.SetObjective(current, objective),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records which model one agent should be started with, or clears it back to that tool's own
    /// default. It starts nothing, and a session already running keeps the model it started with.
    /// </summary>
    public async Task<AgentProjectState> ChooseModelAsync(
        Guid projectId,
        AgentProvider provider,
        string? model,
        string? effort = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.ChooseModel(current, provider, model, effort),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records that a session holding no turn has ended, so the control room stops showing an agent as
    /// here when it is not. It never touches the lease.
    /// </summary>
    public async Task<AgentProjectState> RecordSessionEndedAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.RecordSessionEnded(current, provider),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records the native session Filekin itself opened for an agent. This is the only way a session
    /// identity is established, so nothing an agent says through its coordination tools can name a
    /// different session. It grants no turn and changes no connection state.
    /// </summary>
    public async Task<AgentProjectState> RecordNativeSessionAsync(
        Guid projectId,
        AgentProvider provider,
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.RecordNativeSession(current, provider, nativeSessionId),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a completed folder project to Ready with a new objective and no stale native session
    /// identities. It starts no provider; the owner's separate Start action still grants the turn.
    /// </summary>
    public async Task<AgentProjectState> StartNewObjectiveAsync(
        Guid projectId,
        string objective,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        ArgumentNullException.ThrowIfNull(objective);
        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            state => AgentProjectCoordinator.StartNewObjective(state, objective),
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records the owner's approval to let coordinated sessions work in the project folder itself.
    /// It starts nothing and writes nothing into the folder; it only makes a later start possible.
    /// </summary>
    public async Task<AgentProjectState> GrantSharedCheckoutConsentAsync(
        Guid projectId,
        string approvalDescription,
        SharedFolderTrust trust = SharedFolderTrust.UseMyOwnSettings,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalDescription);
        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                        current,
                        _timeProvider.GetUtcNow(),
                        approvalDescription,
                        trust),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes all clocked-in provider facts before producing MCP launch identities. It does not
    /// start either MCP server or native provider.
    /// </summary>
    public async Task<AgentProjectRuntimeState> PrepareProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        return await WithOperationGateAsync(
                () => PrepareProjectCoreAsync(projectId, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Refreshes provider facts, then transactionally selects at most one initial writer.</summary>
    /// <param name="preferred">
    /// The agent the user chose, or <see langword="null"/> to let Filekin pick the one with more
    /// allowance left. A chosen agent that cannot safely start pauses instead of starting the other.
    /// </param>
    public async Task<AgentProjectRuntimeState> SelectInitialAgentAsync(
        Guid projectId,
        AgentProvider? preferred = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (preferred is { } chosen && !Enum.IsDefined(chosen))
        {
            throw new ArgumentOutOfRangeException(nameof(preferred));
        }

        return await WithOperationGateAsync(
                async () =>
                {
                    var prepared = await PrepareProjectCoreAsync(projectId, cancellationToken)
                        .ConfigureAwait(false);
                    var selected = await _store.UpdateAsync(
                            projectId,
                            current => _coordinator.SelectInitialAgent(
                                current,
                                _timeProvider.GetUtcNow(),
                                preferred),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return prepared with { Project = TrackTurn(selected) };
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lets this project work even when an agent is low on allowance, or its allowance is unknown.
    /// Filekin still reads and shows every number; a low one simply stops refusing the turn outright.
    /// It never buys usage, never enables metered overage, and never spends a reset credit.
    /// </summary>
    public async Task<AgentProjectState> SetWorkOnLowAllowanceAsync(
        Guid projectId,
        bool allowed,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        return await WithOperationGateAsync(
                () => _store.UpdateAsync(
                    projectId,
                    current => AgentProjectCoordinator.SetWorkOnLowAllowance(current, allowed),
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads both agents' allowance for a project that may not be running yet, so a person can see
    /// real numbers before anything starts and Filekin can choose which agent to start. Reading
    /// allowance means asking the provider tools, so nothing automatic calls this: it is reached only
    /// from an explicit action on the agents surface. An allowance that cannot be read stays unknown
    /// rather than being guessed, and an agent that has not clocked in stays clocked out.
    /// </summary>
    public async Task<AgentProjectState> RefreshAllowanceAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        return await WithOperationGateAsync(
                async () =>
                {
                    var state = await LoadProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
                    foreach (var provider in SupportedProviders)
                    {
                        AgentUsageSnapshot usage;
                        try
                        {
                            usage = await GetUsageSource(state, provider)
                                .ReadAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            continue;
                        }

                        if (usage.Provider != provider || !usage.IsKnown)
                        {
                            continue;
                        }

                        state = await _store.UpdateAsync(
                                projectId,
                                current => current.Participant(provider).ConnectionState
                                    == AgentConnectionState.Offline
                                    ? AgentProjectCoordinator.RecordAllowanceBeforeStart(current, provider, usage)
                                    : AgentProjectCoordinator.UpdateUsage(current, provider, usage),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return state;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records that the agent holding the turn cannot go on without a person. The lease is kept,
    /// because a question is not proof that the session stopped.
    /// </summary>
    public async Task<AgentProjectState> MarkBlockedAsync(
        Guid projectId,
        AgentProvider provider,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            state => AgentProjectCoordinator.MarkBlocked(state, provider, reason),
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Clears an attention state once the person has seen it, so the project can be used again.
    /// </summary>
    public async Task<AgentProjectState> ClearAttentionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            AgentProjectCoordinator.ClearAttention,
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the agent holding the turn to stop. Like a handoff request this only records the request:
    /// no process is killed and the lease is kept until that provider's stop is confirmed.
    /// </summary>
    public async Task<AgentProjectState> RequestStopAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            state => AgentProjectCoordinator.RequestStop(state, provider),
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a stopped project to work. It only clears the pause; the next turn is granted by
    /// <see cref="SelectInitialAgentAsync"/> against usage read at that moment.
    /// </summary>
    public async Task<AgentProjectState> ResumeAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            AgentProjectCoordinator.Resume,
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentProjectState> RequestHandoffAsync(
        Guid projectId,
        AgentProvider provider,
        AgentHandoffReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return await WithOperationGateAsync(
                async () => TrackTurn(
                    await _store.UpdateAsync(
                            projectId,
                            state => AgentProjectCoordinator.RequestHandoff(state, provider, reason),
                            cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a provider-confirmed stop. A handoff recipient's usage is refreshed first; if that
    /// refresh fails, the handoff is recorded but no recipient lease is granted.
    /// </summary>
    public async Task<AgentProjectState> ConfirmProviderStoppedAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return await WithOperationGateAsync(
                async () =>
                {
                    var state = await LoadProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
                    if (state.Lease?.Owner != provider)
                    {
                        throw new InvalidOperationException("Only the active lease owner's proven stop can release its lease.");
                    }

                    if (state.Status == AgentProjectStatus.CompletionPending)
                    {
                        return TrackTurn(
                            await _store.UpdateAsync(
                                    projectId,
                                    current => AgentProjectCoordinator.CompleteProject(current, provider),
                                    cancellationToken)
                                .ConfigureAwait(false));
                    }

                    // A stop the user asked for never activates the partner, so there is nothing to
                    // check that agent's allowance for.
                    if (state.Status != AgentProjectStatus.StopPending &&
                        state.PendingHandoff is { } handoff)
                    {
                        await RefreshProviderAsync(state, handoff.To, cancellationToken).ConfigureAwait(false);
                    }

                    return TrackTurn(
                        await _store.UpdateAsync(
                                projectId,
                                current => _coordinator.CompleteActiveTurn(
                                    current,
                                    provider,
                                    _timeProvider.GetUtcNow()),
                                cancellationToken)
                            .ConfigureAwait(false));
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // Cancel and drain the periodic refresh before taking the gate: a tick waiting for it must not
        // deadlock against a disposal that waits for the tick.
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _inTurnRefreshActivity.ConfigureAwait(false);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var projectId in _inTurnRefreshTimers.Keys.ToArray())
            {
                StopInTurnRefresh(projectId);
            }

            foreach (var source in _usageSources.Values)
            {
                switch (source)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }

            _usageSources.Clear();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _shutdown.Dispose();
        }
    }

    private async Task<AgentProjectRuntimeState> PrepareProjectCoreAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var state = await LoadProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var refreshes = await RefreshProvidersAsync(state, cancellationToken).ConfigureAwait(false);

        // A fresh refresh is exactly when Filekin can tell whether the active agent's own usage has
        // dropped to the conservative handoff-request threshold. This never interrupts a turn or moves
        // the lease; it only asks the active agent to wrap up while it still safely can.
        state = await _store.UpdateAsync(
                projectId,
                current => _coordinator.EvaluateUsageHandoff(current, _timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);

        var mcpServers = SupportedProviders
            .Select(provider => _mcpLaunchFactory.Create(state, provider))
            .ToArray();
        return new AgentProjectRuntimeState(TrackTurn(state), refreshes, mcpServers);
    }

    /// <summary>
    /// Keeps the periodic in-turn refresh aligned with the project's own state. It runs only while a
    /// lease owner is actually working, because that is exactly when
    /// <see cref="AgentProjectCoordinator.EvaluateUsageHandoff"/> can act: a requested handoff, a
    /// released lease, an attention state, or a finished project all stop it again. Always called
    /// while the operation gate is held.
    /// </summary>
    private AgentProjectState TrackTurn(AgentProjectState state)
    {
        if (_disposed)
        {
            return state;
        }

        // This project's own operation just succeeded, so only its fault is cleared.
        _inTurnRefreshFaults.TryRemove(state.Id, out _);
        if (state.Lease is null || state.Status != AgentProjectStatus.Working)
        {
            StopInTurnRefresh(state.Id);
            return state;
        }

        if (!_inTurnRefreshTimers.TryGetValue(state.Id, out var timer))
        {
            timer = _timeProvider.CreateTimer(
                OnInTurnRefreshTick,
                state.Id,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _inTurnRefreshTimers.Add(state.Id, timer);
        }

        // One-shot rearming, so a slow refresh can never overlap the next tick.
        timer.Change(_inTurnRefreshInterval, Timeout.InfiniteTimeSpan);
        return state;
    }

    private void StopInTurnRefresh(Guid projectId)
    {
        if (_inTurnRefreshTimers.Remove(projectId, out var timer))
        {
            timer.Dispose();
        }
    }

    private void OnInTurnRefreshTick(object? state)
    {
        if (_disposed || state is not Guid projectId)
        {
            return;
        }

        _inTurnRefreshActivity = RunInTurnRefreshAsync(projectId);
    }

    /// <summary>
    /// One periodic refresh of a long-running turn. It is the same gated preparation an explicit call
    /// performs, so a provider inspection failure still only records provider-neutral state and the
    /// active writer keeps its lease. An unexpected failure stops this project's periodic refresh
    /// instead of retrying silently forever; this project's next explicit operation restarts it.
    /// </summary>
    private async Task RunInTurnRefreshAsync(Guid projectId)
    {
        try
        {
            await _operationGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed || !_started || !_inTurnRefreshTimers.ContainsKey(projectId))
            {
                return;
            }

            await PrepareProjectCoreAsync(projectId, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _inTurnRefreshFaults[projectId] = exception;
            StopInTurnRefresh(projectId);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<IReadOnlyList<AgentProviderRefreshResult>> RefreshProvidersAsync(
        AgentProjectState state,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentProviderRefreshResult>(SupportedProviders.Length);
        foreach (var provider in SupportedProviders)
        {
            results.Add(await RefreshProviderAsync(state, provider, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<AgentProviderRefreshResult> RefreshProviderAsync(
        AgentProjectState state,
        AgentProvider provider,
        CancellationToken cancellationToken)
    {
        if (state.Status == AgentProjectStatus.Completed ||
            state.Participant(provider).ConnectionState == AgentConnectionState.Offline)
        {
            return new AgentProviderRefreshResult(provider, AgentProviderRefreshStatus.NotClockedIn);
        }

        AgentUsageSnapshot usage;
        try
        {
            var source = GetUsageSource(state, provider);
            usage = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (usage.Provider != provider)
            {
                throw new InvalidOperationException("The provider usage source returned another provider's facts.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _store.UpdateAsync(
                    state.Id,
                    current => AgentProjectCoordinator.MarkProviderUnavailable(
                        current,
                        provider,
                        $"{ProviderName(provider)} usage and authentication could not be refreshed safely."),
                    cancellationToken)
                .ConfigureAwait(false);
            return new AgentProviderRefreshResult(provider, AgentProviderRefreshStatus.Unavailable);
        }

        await _store.UpdateAsync(
                state.Id,
                current => AgentProjectCoordinator.UpdateUsage(current, provider, usage),
                cancellationToken)
            .ConfigureAwait(false);
        return new AgentProviderRefreshResult(provider, AgentProviderRefreshStatus.Updated);
    }

    private IAgentUsageSource GetUsageSource(AgentProjectState state, AgentProvider provider)
    {
        var key = (state.Id, provider);
        if (_usageSources.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var source = _usageSourceFactory.Create(provider, state.Id, state.FolderPath);
        if (source.Provider != provider)
        {
            DisposeSource(source);
            throw new InvalidOperationException("The usage source factory returned another provider.");
        }

        _usageSources.Add(key, source);
        return source;
    }

    private async Task<AgentProjectState> LoadProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Agent project '{projectId:D}' does not exist.");

    private async Task<T> WithOperationGateAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStarted();
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "Agent coordination runtime startup reconciliation must complete before project operations.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateProjectId(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("The agent project id cannot be empty.", nameof(projectId));
        }
    }

    private static string ProviderName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static SqliteAgentProjectStore RequireStore(SqliteAgentProjectStore? store) =>
        store ?? throw new ArgumentNullException(nameof(store));

    private static AgentCoordinationPolicy RequirePolicy(AgentCoordinationPolicy? policy) =>
        policy ?? throw new ArgumentNullException(nameof(policy));

    /// <summary>
    /// Half the policy's maximum usage age, so an observation taken by one in-turn refresh is still
    /// fresh when the next one evaluates it. The cadence is an implementation default; the
    /// conservative handoff percentage it feeds remains an open product question.
    /// </summary>
    private static TimeSpan DefaultInTurnRefreshInterval(AgentCoordinationPolicy policy) =>
        TimeSpan.FromTicks(policy.MaximumUsageAge.Ticks / 2);

    private static void DisposeSource(IAgentUsageSource source)
    {
        if (source is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
