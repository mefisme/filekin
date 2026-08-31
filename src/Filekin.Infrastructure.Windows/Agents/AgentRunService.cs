using System.Collections.Concurrent;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

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

    /// <summary>The agents this service currently has a live native session for.</summary>
    public IReadOnlyList<AgentProvider> RunningAgents(Guid projectId) =>
        _sessions.Keys
            .Where(key => key.ProjectId == projectId)
            .Select(key => key.Provider)
            .OrderBy(provider => provider)
            .ToArray();

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
                "Neither agent has allowance left right now. Wait for an allowance window to reset.");

        if (!_coordinator.HasStartableAllowance(project, provider, now))
        {
            throw new InvalidOperationException(
                $"{DisplayName(provider)} has no allowance left right now.");
        }

        if (_sessions.ContainsKey((projectId, provider)))
        {
            throw new InvalidOperationException($"{DisplayName(provider)} is already running for this project.");
        }

        var mcpServer = prepared.McpServers.Single(server => server.Provider == provider);
        var handle = await _launcher.LaunchAsync(
                new AgentSessionLaunchRequest(
                    provider,
                    projectId,
                    project.FolderPath,
                    $"Filekin {Path.GetFileName(project.FolderPath.TrimEnd(Path.DirectorySeparatorChar))}",
                    AgentRunPrompt.Create(project.Objective),
                    mcpServer,
                    consent),
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!_sessions.TryAdd((projectId, provider), handle))
            {
                throw new InvalidOperationException($"{DisplayName(provider)} is already running for this project.");
            }

            WatchForStop(projectId, provider, handle);
            await WaitForClockInAsync(projectId, provider, cancellationToken).ConfigureAwait(false);
            var selected = await _runtime.SelectInitialAgentAsync(projectId, provider, cancellationToken)
                .ConfigureAwait(false);
            return selected.Project;
        }
        catch
        {
            // The session may already be doing work, so it is asked to stop rather than abandoned.
            await StopQuietlyAsync(projectId, provider).ConfigureAwait(false);
            throw;
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
        var owner = await ActiveAgentAsync(projectId, cancellationToken).ConfigureAwait(false);
        var state = await _runtime.RequestStopAsync(projectId, owner, cancellationToken).ConfigureAwait(false);

        if (_sessions.TryGetValue((projectId, owner), out var handle))
        {
            await handle.RequestStopAsync(cancellationToken).ConfigureAwait(false);
        }

        return state;
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
