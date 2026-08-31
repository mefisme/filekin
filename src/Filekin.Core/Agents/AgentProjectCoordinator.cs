namespace Filekin.Core.Agents;

/// <summary>
/// Owns all provider-neutral project transitions. Provider adapters report facts; this type decides
/// whether Filekin may grant or transfer the single cooperative working-tree lease.
/// </summary>
public sealed class AgentProjectCoordinator
{
    private static readonly AgentProvider[] SupportedProviders =
        [AgentProvider.Codex, AgentProvider.ClaudeCode];

    private readonly AgentCoordinationPolicy _policy;

    public AgentProjectCoordinator(AgentCoordinationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        if (_policy.MinimumRemainingPercent is < 0 or >= 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "The minimum remaining percentage must be at least zero and less than 100.");
        }

        if (_policy.HandoffRequestRemainingPercent <= _policy.MinimumRemainingPercent ||
            _policy.HandoffRequestRemainingPercent > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "The handoff request percentage must be greater than the minimum remaining percentage and at most 100.");
        }

        if (_policy.MaximumUsageAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "The maximum usage age must be positive.");
        }
    }

    public static AgentProjectState Create(string folderPath, string objective = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(objective);

        var participants = SupportedProviders.ToDictionary(
            provider => provider,
            provider => new AgentParticipant(
                provider,
                NativeSessionId: null,
                AgentConnectionState.Offline,
                AgentTurnState.ClockedOut,
                Usage: null));

        return State(
            Guid.NewGuid(),
            Path.GetFullPath(folderPath),
            objective.Trim(),
            AgentProjectStatus.ClockingIn,
            participants);
    }

    /// <summary>
    /// Records what the user wants done. The objective is the user's own text, so this changes no
    /// participant, lease, or turn state and is allowed at any time before the project completes.
    /// </summary>
    public static AgentProjectState SetObjective(AgentProjectState state, string objective)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objective);

        if (state.Status == AgentProjectStatus.Completed)
        {
            throw new InvalidOperationException("A completed project's objective cannot be rewritten.");
        }

        return State(
            state,
            objective.Trim(),
            state.SharedCheckoutConsent,
            state.WorkOnLowAllowance,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    /// <summary>
    /// Records the owner's approval to let coordinated sessions work in this folder itself. It is a
    /// project fact, not a turn: no agent is started, no lease is granted, and nothing is written into
    /// the folder. Approving again simply replaces the record, which is what a reworded approval in a
    /// later Filekin version needs.
    /// </summary>
    public static AgentProjectState GrantSharedCheckoutConsent(
        AgentProjectState state,
        DateTimeOffset grantedAt,
        string approvalDescription,
        SharedFolderTrust trust = SharedFolderTrust.UseMyOwnSettings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalDescription);
        if (!Enum.IsDefined(trust))
        {
            throw new ArgumentOutOfRangeException(nameof(trust));
        }

        return State(
            state,
            state.Objective,
            new SharedCheckoutConsent(grantedAt, approvalDescription, trust),
            state.WorkOnLowAllowance,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    /// <summary>
    /// Lets this project work even when an agent is low on allowance, or its allowance is unknown.
    /// Filekin still reads and shows every number, and still asks the working agent to hand over while
    /// it has room; what changes is that a low number no longer refuses the turn outright. It never
    /// buys usage, never enables metered overage, and never spends a reset credit.
    /// </summary>
    public static AgentProjectState SetWorkOnLowAllowance(AgentProjectState state, bool allowed)
    {
        ArgumentNullException.ThrowIfNull(state);

        return State(
            state,
            state.Objective,
            state.SharedCheckoutConsent,
            allowed,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    public static AgentProjectState ClockIn(
        AgentProjectState state,
        AgentProvider provider,
        string nativeSessionId,
        AgentUsageSnapshot? usage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        EnsureUsageProvider(provider, usage);

        // The whole point of the relay is a second agent arriving while the first is still working,
        // so a partner may clock in mid-turn. What is refused is the agent holding the turn clocking
        // in again underneath itself, which would quietly reset the turn it is in the middle of.
        if (state.Lease?.Owner == provider)
        {
            throw new InvalidOperationException(
                "The agent holding the working-tree turn cannot clock in again during it.");
        }

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with
        {
            NativeSessionId = nativeSessionId,
            ConnectionState = usage is { IsKnown: true }
                ? AgentConnectionState.Ready
                : AgentConnectionState.UsagePending,
            TurnState = AgentTurnState.Waiting,
            Usage = usage,
        };

        var allClockedIn = participants.Values.All(
            participant => participant.ConnectionState != AgentConnectionState.Offline);

        // Somebody arriving does not change what the project is doing. While a turn is held, that turn
        // is still the truth, and saying "ready" over the top of it would lose it.
        return State(
            state,
            state.Lease is not null
                ? state.Status
                : allClockedIn ? AgentProjectStatus.Ready : AgentProjectStatus.ClockingIn,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    /// <summary>
    /// Records what an agent's allowance looks like before it has clocked in, so Filekin can choose
    /// which agent to start and show real numbers instead of "unknown". This is a fact about the
    /// account, not about a session: an agent that is not here stays not here, and no turn changes.
    /// </summary>
    public static AgentProjectState RecordAllowanceBeforeStart(
        AgentProjectState state,
        AgentProvider provider,
        AgentUsageSnapshot usage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(usage);
        EnsureUsageProvider(provider, usage);

        var participants = CopyParticipants(state);
        var participant = participants[provider];
        if (participant.ConnectionState != AgentConnectionState.Offline)
        {
            throw new InvalidOperationException(
                "An agent that has clocked in reports its own usage; this is only for one that has not.");
        }

        participants[provider] = participant with { Usage = usage };

        return State(
            state,
            state.Status,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    public static AgentProjectState UpdateUsage(
        AgentProjectState state,
        AgentProvider provider,
        AgentUsageSnapshot usage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(usage);
        EnsureUsageProvider(provider, usage);

        var participants = CopyParticipants(state);
        var participant = participants[provider];
        if (participant.ConnectionState == AgentConnectionState.Offline)
        {
            throw new InvalidOperationException("An agent must clock in before reporting usage.");
        }

        participants[provider] = participant with
        {
            ConnectionState = usage.IsKnown
                ? AgentConnectionState.Ready
                : AgentConnectionState.UsagePending,
            Usage = usage,
        };

        return State(
            state,
            state.Status,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    /// <summary>
    /// Records that Filekin could not establish current provider facts. An active provider keeps its
    /// lease because an inspection failure is not proof that its native turn stopped.
    /// </summary>
    public static AgentProjectState MarkProviderUnavailable(
        AgentProjectState state,
        AgentProvider provider,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var participants = CopyParticipants(state);
        var participant = participants[provider];
        if (participant.ConnectionState == AgentConnectionState.Offline)
        {
            throw new InvalidOperationException("An agent must clock in before it can become unavailable.");
        }

        participants[provider] = participant with
        {
            ConnectionState = AgentConnectionState.Unavailable,
            Usage = null,
        };

        var isLeaseOwner = state.Lease?.Owner == provider;
        var hasNoLease = state.Lease is null;
        var status = isLeaseOwner
            ? AgentProjectStatus.NeedsAttention
            : hasNoLease && state.Status is not (
                AgentProjectStatus.Completed or AgentProjectStatus.NeedsAttention)
                ? AgentProjectStatus.Paused
                : state.Status;
        var attentionReason = isLeaseOwner || hasNoLease
            ? state.AttentionReason ?? reason
            : state.AttentionReason;

        return State(
            state,
            status,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason);
    }

    /// <summary>
    /// Grants the first turn. Work does not wait for both agents: one clocked-in agent with safe
    /// allowance is enough, and the relay begins when the other clocks in (DECISIONS.md, 2026-08-31).
    /// </summary>
    /// <param name="preferred">
    /// The agent the user chose. Nothing chosen means Filekin picks the one with more allowance left.
    /// A chosen agent that cannot safely start pauses with that reason rather than quietly starting
    /// the other one, because starting somebody else is not what the user asked for.
    /// </param>
    public AgentProjectState SelectInitialAgent(
        AgentProjectState state,
        DateTimeOffset now,
        AgentProvider? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureNoLease(state);

        if (state.Status == AgentProjectStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Resolve or reconcile the attention state before granting another lease.");
        }

        if (preferred is { } chosen && !state.Participants.ContainsKey(chosen))
        {
            throw new ArgumentOutOfRangeException(nameof(preferred));
        }

        if (state.Participants.Values.All(
                participant => participant.ConnectionState == AgentConnectionState.Offline))
        {
            throw new InvalidOperationException("At least one agent must clock in before Filekin selects the first turn.");
        }

        if (preferred is { } requested)
        {
            return IsSafeToActivate(state, state.Participant(requested), now)
                ? Activate(state, requested, now, pendingHandoff: state.PendingHandoff)
                : Pause(
                    state,
                    $"{Describe(requested)} was chosen but does not have fresh, known usage above the safety threshold.");
        }

        var candidates = state.Participants.Values
            .Where(participant => IsSafeToActivate(state, participant, now))
            .OrderByDescending(participant => participant.Usage!.MinimumRemainingPercent)
            .ThenBy(participant => participant.Provider)
            .ToArray();

        return candidates.Length == 0
            ? Pause(state, "No clocked-in agent has fresh, known usage above the safety threshold.")
            : Activate(state, candidates[0].Provider, now, pendingHandoff: state.PendingHandoff);
    }

    /// <summary>
    /// Whether Filekin may start this agent at all. Nobody has clocked in before a launch, so
    /// connection state is ignored and unknown allowance is allowed: a first run cannot have reported
    /// any. Only fresh evidence that the agent is actually out of allowance refuses the start, because
    /// a stale low reading may describe an allowance window that has since reset.
    /// </summary>
    public bool HasStartableAllowance(
        AgentProjectState state,
        AgentProvider provider,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !IsKnownExhausted(state, state.Participant(provider), now);
    }

    /// <summary>
    /// Which agent Filekin starts when the user does not choose: the one with more allowance left.
    /// An agent whose allowance is not known yet ranks below one that is known to be safe, and above
    /// one that is known to be out. Returns <see langword="null"/> only when every agent is freshly
    /// known to be out of allowance, which is the one case where starting anybody would be wrong.
    /// </summary>
    public AgentProvider? ChooseAgentToStart(AgentProjectState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Participants.Values
            .Where(participant => !IsKnownExhausted(state, participant, now))
            .OrderByDescending(participant => IsKnownSafe(participant, now))
            .ThenByDescending(participant => IsKnownSafe(participant, now)
                ? participant.Usage!.MinimumRemainingPercent
                : 0)
            .ThenBy(participant => participant.Provider)
            .Select(participant => (AgentProvider?)participant.Provider)
            .FirstOrDefault();
    }

    /// <summary>
    /// The user asked the active agent to stop. Like a handoff request this is cooperative: it never
    /// kills a process and never releases the lease by itself. Only the app-owned provider-confirmed
    /// stop ends the turn, and it ends in a resumable pause rather than an attention state.
    /// </summary>
    public static AgentProjectState RequestStop(AgentProjectState state, AgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with { TurnState = AgentTurnState.StopRequested };

        return State(
            state,
            AgentProjectStatus.StopPending,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    /// <summary>
    /// Clears an attention state once the person has seen it, so the project can be used again. It is
    /// deliberately separate from reading the reason: Filekin never decides on its own that a problem
    /// somebody was asked to look at has been dealt with. It refuses while a turn is still held,
    /// because dropping a live turn would lose track of a running agent.
    /// </summary>
    public static AgentProjectState ClearAttention(AgentProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != AgentProjectStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only a project that needs attention can be cleared.");
        }

        if (state.Lease is not null)
        {
            throw new InvalidOperationException(
                "An agent still holds the turn. Its stop must be settled before the project is cleared.");
        }

        var participants = CopyParticipants(state);
        foreach (var provider in SupportedProviders)
        {
            if (participants[provider].TurnState == AgentTurnState.NeedsAttention)
            {
                participants[provider] = participants[provider] with
                {
                    TurnState = participants[provider].ConnectionState == AgentConnectionState.Offline
                        ? AgentTurnState.ClockedOut
                        : AgentTurnState.Waiting,
                };
            }
        }

        return State(
            state,
            AgentProjectStatus.Ready,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    /// <summary>
    /// Returns a stopped project to work. It only clears the pause; whether anybody may actually take
    /// the turn is decided again by <see cref="SelectInitialAgent"/> against current usage.
    /// </summary>
    public static AgentProjectState Resume(AgentProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != AgentProjectStatus.Paused)
        {
            throw new InvalidOperationException("Only a paused project can be resumed.");
        }

        var participants = CopyParticipants(state);
        foreach (var provider in SupportedProviders)
        {
            if (participants[provider].TurnState == AgentTurnState.StopRequested)
            {
                participants[provider] = participants[provider] with { TurnState = AgentTurnState.Waiting };
            }
        }

        return State(
            state,
            AgentProjectStatus.Ready,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    /// <summary>
    /// Proactively requests a handoff from the active agent while its own usage is still fresh, known,
    /// and above the hard safety cutoff, but has dropped to or below the earlier
    /// <see cref="AgentCoordinationPolicy.HandoffRequestRemainingPercent"/> warning threshold. This is
    /// the cooperative "request a safe stop while allowance remains" path: it never interrupts the
    /// active turn, never releases the lease, and never guesses from stale or unknown usage.
    /// </summary>
    /// <remarks>
    /// When the other participant does not itself have safe headroom, this defers rather than
    /// requesting a handoff nobody could complete: the active agent keeps working, and if it later
    /// genuinely stops, <see cref="CompleteActiveTurn"/> already pauses safely once the recipient still
    /// is not ready.
    /// </remarks>
    public AgentProjectState EvaluateUsageHandoff(AgentProjectState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Lease is not { } lease || state.Status != AgentProjectStatus.Working)
        {
            return state;
        }

        var owner = state.Participant(lease.Owner);
        if (owner.ConnectionState != AgentConnectionState.Ready ||
            owner.Usage is not { } usage ||
            !usage.IsFresh(now, _policy.MaximumUsageAge) ||
            usage.MinimumRemainingPercent > _policy.HandoffRequestRemainingPercent)
        {
            return state;
        }

        var partner = state.Participants.Values.Single(candidate => candidate.Provider != lease.Owner);
        return IsSafeToActivate(state, partner, now)
            ? RequestHandoff(state, lease.Owner, AgentHandoffReason.UsageThreshold)
            : state;
    }

    public static AgentProjectState RequestHandoff(
        AgentProjectState state,
        AgentProvider provider,
        AgentHandoffReason reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with
        {
            TurnState = AgentTurnState.HandoffRequested,
        };

        return State(
            state,
            AgentProjectStatus.HandoffPending,
            participants,
            state.Lease,
            requestedHandoffReason: reason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    public static AgentProjectState SubmitHandoff(AgentProjectState state, AgentHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(handoff);
        EnsureLeaseOwner(state, handoff.From);

        if (handoff.To == handoff.From || !state.Participants.ContainsKey(handoff.To))
        {
            throw new InvalidOperationException("A handoff must target the other project agent.");
        }

        if (string.IsNullOrWhiteSpace(handoff.Summary))
        {
            throw new ArgumentException("A handoff requires a useful summary.", nameof(handoff));
        }

        // What matters is that a handoff was asked for, not what has happened to the project since.
        // An agent that hits a wall and reports it must still be able to hand over what it knows,
        // which is exactly when that knowledge is worth the most.
        if (state.RequestedHandoffReason is null)
        {
            throw new InvalidOperationException("Filekin must request a handoff before one is submitted.");
        }

        if (state.PendingHandoff is not null)
        {
            throw new InvalidOperationException("The active turn already submitted its handoff.");
        }

        // Why the turn is moving is Filekin's own fact: it is the reason Filekin asked. The agent's
        // job is to write down what it did, and a wrong guess at the label must not throw that away.
        var recorded = handoff with { Reason = state.RequestedHandoffReason.Value };

        return State(
            state,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            pendingHandoff: recorded,
            state.LastHandoff,
            state.Messages,
            state.AttentionReason);
    }

    public static AgentProjectState AcceptHandoff(
        AgentProjectState state,
        AgentProvider provider,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        if (state.LastHandoff is not { } handoff || handoff.To != provider)
        {
            throw new InvalidOperationException("The active agent has no handoff to accept.");
        }

        if (handoff.AcceptedAt is not null)
        {
            throw new InvalidOperationException("The active handoff was already accepted.");
        }

        return State(
            state,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            handoff with { AcceptedAt = acceptedAt },
            state.Messages,
            state.AttentionReason);
    }

    /// <summary>
    /// Records the active agent's completion report without trusting it as proof that the native turn
    /// stopped. The lease remains held until the provider adapter confirms that stop.
    /// </summary>
    public static AgentProjectState ReportCompleted(
        AgentProjectState state,
        AgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with
        {
            TurnState = AgentTurnState.CompletionReported,
        };

        return State(
            state,
            AgentProjectStatus.CompletionPending,
            participants,
            state.Lease,
            requestedHandoffReason: null,
            pendingHandoff: null,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    /// <summary>
    /// Applies a provider's proven stop event. The lease is released before the recipient can be
    /// activated; a missing handoff fails closed as NeedsAttention.
    /// </summary>
    public AgentProjectState CompleteActiveTurn(
        AgentProjectState state,
        AgentProvider provider,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);

        // The user asked for this stop, so it is not the stopped-without-a-handoff failure. A handoff
        // the agent still submitted is kept as history, but it does not activate the partner, because
        // stopping is what was asked for.
        if (state.Status == AgentProjectStatus.StopPending)
        {
            participants[provider] = participants[provider] with { TurnState = AgentTurnState.Waiting };
            return State(
                state,
                AgentProjectStatus.Paused,
                participants,
                lease: null,
                requestedHandoffReason: null,
                pendingHandoff: null,
                lastHandoff: state.PendingHandoff ?? state.LastHandoff,
                state.Messages,
                attentionReason: "Stopped at your request. The project is kept, so it can be resumed.");
        }

        if (state.PendingHandoff is null)
        {
            // An agent that was asked to hand over and did not is a real problem: the next agent would
            // start with no idea what happened. An agent that simply finished its own turn is not. The
            // turn goes back, the project stays usable, and nobody is asked to fix anything.
            var wasAsked = state.Status == AgentProjectStatus.HandoffPending;
            participants[provider] = participants[provider] with
            {
                TurnState = wasAsked ? AgentTurnState.NeedsAttention : AgentTurnState.Waiting,
            };

            return State(
                state,
                wasAsked ? AgentProjectStatus.NeedsAttention : AgentProjectStatus.Ready,
                participants,
                lease: null,
                requestedHandoffReason: null,
                pendingHandoff: null,
                state.LastHandoff,
                state.Messages,
                attentionReason: wasAsked
                    ? "The active agent was asked to hand over and stopped without doing it."
                    : $"{Describe(provider)} finished its turn.");
        }

        var handoff = state.PendingHandoff;
        participants[provider] = participants[provider] with { TurnState = AgentTurnState.Waiting };
        participants[handoff.To] = participants[handoff.To] with { TurnState = AgentTurnState.Waiting };

        var stopped = State(
            state,
            AgentProjectStatus.Ready,
            participants,
            lease: null,
            requestedHandoffReason: null,
            pendingHandoff: null,
            lastHandoff: handoff,
            state.Messages,
            attentionReason: null);

        if (!IsSafeToActivate(stopped, stopped.Participant(handoff.To), now))
        {
            return State(
                stopped,
                AgentProjectStatus.Paused,
                CopyParticipants(stopped),
                stopped.Lease,
                stopped.RequestedHandoffReason,
                stopped.PendingHandoff,
                stopped.LastHandoff,
                stopped.Messages,
                attentionReason: "The handoff recipient does not have fresh, known usage above the safety threshold.");
        }

        return Activate(stopped, handoff.To, now, pendingHandoff: null);
    }

    /// <summary>Records a provider-confirmed stop after the active agent reported the objective done.</summary>
    public static AgentProjectState CompleteProject(AgentProjectState state, AgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with { TurnState = AgentTurnState.Completed };
        foreach (var otherProvider in SupportedProviders.Where(candidate => candidate != provider))
        {
            if (participants[otherProvider].TurnState != AgentTurnState.Completed)
            {
                participants[otherProvider] = participants[otherProvider] with
                {
                    TurnState = participants[otherProvider].ConnectionState == AgentConnectionState.Offline
                        ? AgentTurnState.ClockedOut
                        : AgentTurnState.Waiting,
                };
            }
        }

        return State(
            state,
            AgentProjectStatus.Completed,
            participants,
            lease: null,
            requestedHandoffReason: null,
            pendingHandoff: null,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    public static AgentProjectState MarkBlocked(
        AgentProjectState state,
        AgentProvider provider,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureLeaseOwner(state, provider);

        var participants = CopyParticipants(state);
        participants[provider] = participants[provider] with { TurnState = AgentTurnState.Blocked };

        // Keep the lease: a permission prompt or user question does not prove the provider stopped.
        return State(
            state,
            AgentProjectStatus.NeedsAttention,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: reason);
    }

    /// <summary>
    /// Records a provider-native subscription limit callback. The callback may arrive before the
    /// provider can clock in through a model turn, so it establishes the native session identity while
    /// failing the provider closed. An active writer keeps its lease because a failed model request is
    /// not proof that the native session stopped.
    /// </summary>
    public static AgentProjectState ReportUsageLimit(
        AgentProjectState state,
        AgentProvider provider,
        string nativeSessionId,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (state.Status == AgentProjectStatus.Completed)
        {
            return state;
        }

        var participants = CopyParticipants(state);
        var participant = participants[provider];
        if (participant.NativeSessionId is not null &&
            !string.Equals(participant.NativeSessionId, nativeSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A provider lifecycle callback cannot replace another native session identity.");
        }

        var isLeaseOwner = state.Lease?.Owner == provider;
        participants[provider] = participant with
        {
            NativeSessionId = nativeSessionId,
            ConnectionState = AgentConnectionState.Unavailable,
            TurnState = isLeaseOwner
                ? AgentTurnState.Blocked
                : participant.TurnState == AgentTurnState.ClockedOut
                    ? AgentTurnState.Waiting
                    : participant.TurnState,
            Usage = null,
        };

        var hasNoLease = state.Lease is null;
        var status = isLeaseOwner
            ? AgentProjectStatus.NeedsAttention
            : hasNoLease && state.Status != AgentProjectStatus.NeedsAttention
                ? AgentProjectStatus.Paused
                : state.Status;
        var attentionReason = isLeaseOwner || hasNoLease
            ? state.AttentionReason ?? reason
            : state.AttentionReason;

        return State(
            state,
            status,
            participants,
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason);
    }

    public static AgentProjectState QueueMessage(
        AgentProjectState state,
        AgentProvider from,
        AgentProvider to,
        string text,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (from == to || !state.Participants.ContainsKey(from) || !state.Participants.ContainsKey(to))
        {
            throw new InvalidOperationException("A message must target the other project agent.");
        }

        var messages = state.Messages.Append(new AgentMessage(Guid.NewGuid(), from, to, sentAt, text));
        return State(
            state,
            state.Status,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            messages,
            state.AttentionReason);
    }

    public static AgentProjectState ReconcileAfterRestart(AgentProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Lease is null && state.Participants.Values.All(
                participant => participant.TurnState is not (
                    AgentTurnState.Active or
                    AgentTurnState.HandoffRequested or
                    AgentTurnState.StopRequested or
                    AgentTurnState.Blocked or
                    AgentTurnState.CompletionReported)))
        {
            return state;
        }

        var participants = CopyParticipants(state);
        foreach (var provider in SupportedProviders)
        {
            if (participants[provider].TurnState is
                AgentTurnState.Active or
                AgentTurnState.HandoffRequested or
                AgentTurnState.StopRequested or
                AgentTurnState.Blocked or
                AgentTurnState.CompletionReported)
            {
                participants[provider] = participants[provider] with
                {
                    TurnState = AgentTurnState.NeedsAttention,
                };
            }
        }

        return State(
            state,
            AgentProjectStatus.NeedsAttention,
            participants,
            lease: null,
            requestedHandoffReason: null,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: "Native agent sessions must be reconciled before another lease is granted.");
    }

    private static AgentProjectState Pause(AgentProjectState state, string reason) =>
        State(
            state,
            AgentProjectStatus.Paused,
            CopyParticipants(state),
            state.Lease,
            state.RequestedHandoffReason,
            state.PendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: reason);

    private static string Describe(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => provider.ToString(),
    };

    private static AgentProjectState Activate(
        AgentProjectState state,
        AgentProvider provider,
        DateTimeOffset now,
        AgentHandoff? pendingHandoff)
    {
        var participants = CopyParticipants(state);
        foreach (var currentProvider in SupportedProviders)
        {
            var turnState = currentProvider == provider
                ? AgentTurnState.Active
                : participants[currentProvider].ConnectionState == AgentConnectionState.Offline
                    ? AgentTurnState.ClockedOut
                    : AgentTurnState.Waiting;
            participants[currentProvider] = participants[currentProvider] with { TurnState = turnState };
        }

        return State(
            state,
            AgentProjectStatus.Working,
            participants,
            lease: new WorkingTreeLease(Guid.NewGuid(), provider, now),
            requestedHandoffReason: null,
            pendingHandoff: pendingHandoff,
            state.LastHandoff,
            state.Messages,
            attentionReason: null);
    }

    private bool IsKnownSafe(AgentParticipant participant, DateTimeOffset now) =>
        participant.Usage is { } usage &&
        usage.IsFresh(now, _policy.MaximumUsageAge) &&
        usage.MinimumRemainingPercent > _policy.MinimumRemainingPercent;

    private bool IsKnownExhausted(AgentProjectState state, AgentParticipant participant, DateTimeOffset now) =>
        !state.WorkOnLowAllowance &&
        participant.Usage is { } usage &&
        usage.IsFresh(now, _policy.MaximumUsageAge) &&
        usage.MinimumRemainingPercent <= _policy.MinimumRemainingPercent;

    /// <summary>
    /// Whether this agent may be given the turn. It must be here: that part is never waived, because
    /// an agent that has not clocked in cannot work whatever the owner says. The allowance threshold
    /// is waived when the owner has said this project works on low allowance.
    /// </summary>
    private bool IsSafeToActivate(AgentProjectState state, AgentParticipant participant, DateTimeOffset now) =>
        participant.ConnectionState == AgentConnectionState.Ready &&
        (state.WorkOnLowAllowance ||
            (participant.Usage is { } usage &&
             usage.IsFresh(now, _policy.MaximumUsageAge) &&
             usage.MinimumRemainingPercent > _policy.MinimumRemainingPercent));

    private static void EnsureUsageProvider(AgentProvider provider, AgentUsageSnapshot? usage)
    {
        if (usage is not null && usage.Provider != provider)
        {
            throw new ArgumentException("Usage must belong to the participant reporting it.", nameof(usage));
        }

        if (usage?.Windows.Any(window =>
                string.IsNullOrWhiteSpace(window.Name) || window.UsedPercent is < 0 or > 100) == true)
        {
            throw new ArgumentOutOfRangeException(nameof(usage), "Usage windows must be named and between 0 and 100 percent.");
        }
    }

    private static void EnsureNoLease(AgentProjectState state)
    {
        if (state.Lease is not null)
        {
            throw new InvalidOperationException("The project already has an active working-tree lease.");
        }
    }

    private static void EnsureLeaseOwner(AgentProjectState state, AgentProvider provider)
    {
        if (state.Lease?.Owner != provider)
        {
            throw new InvalidOperationException("Only the active lease owner can perform this transition.");
        }
    }

    private static Dictionary<AgentProvider, AgentParticipant> CopyParticipants(AgentProjectState state) =>
        state.Participants.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static AgentProjectState State(
        AgentProjectState existing,
        AgentProjectStatus status,
        IDictionary<AgentProvider, AgentParticipant> participants,
        WorkingTreeLease? lease,
        AgentHandoffReason? requestedHandoffReason,
        AgentHandoff? pendingHandoff,
        AgentHandoff? lastHandoff,
        IEnumerable<AgentMessage> messages,
        string? attentionReason) =>
        State(
            existing,
            existing.Objective,
            existing.SharedCheckoutConsent,
            existing.WorkOnLowAllowance,
            status,
            participants,
            lease,
            requestedHandoffReason,
            pendingHandoff,
            lastHandoff,
            messages,
            attentionReason);

    private static AgentProjectState State(
        AgentProjectState existing,
        string objective,
        SharedCheckoutConsent? sharedCheckoutConsent,
        bool workOnLowAllowance,
        AgentProjectStatus status,
        IDictionary<AgentProvider, AgentParticipant> participants,
        WorkingTreeLease? lease,
        AgentHandoffReason? requestedHandoffReason,
        AgentHandoff? pendingHandoff,
        AgentHandoff? lastHandoff,
        IEnumerable<AgentMessage> messages,
        string? attentionReason) =>
        new(
            existing.Id,
            existing.FolderPath,
            objective,
            sharedCheckoutConsent,
            workOnLowAllowance,
            status,
            participants,
            lease,
            requestedHandoffReason,
            pendingHandoff,
            lastHandoff,
            messages,
            attentionReason);

    private static AgentProjectState State(
        Guid id,
        string folderPath,
        string objective,
        AgentProjectStatus status,
        IDictionary<AgentProvider, AgentParticipant> participants) =>
        new(
            id,
            folderPath,
            objective,
            sharedCheckoutConsent: null,
            workOnLowAllowance: false,
            status,
            participants,
            lease: null,
            requestedHandoffReason: null,
            pendingHandoff: null,
            lastHandoff: null,
            messages: [],
            attentionReason: null);
}
