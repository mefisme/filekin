using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Project-scoped application service behind Filekin's MCP tools. The caller identity is fixed when
/// the local server process starts, so a tool call cannot impersonate the partner agent or select a
/// different project.
/// </summary>
public sealed class AgentCoordinationToolService
{
    private const int MaximumSessionIdLength = 512;
    private const int MaximumMessageLength = 32 * 1024;
    private const int MaximumHandoffFieldLength = 64 * 1024;
    private static readonly TimeSpan HandoffAssignmentTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HandoffAssignmentPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly IAgentProjectStore _store;
    private readonly TimeProvider _timeProvider;

    public AgentCoordinationToolService(
        IAgentProjectStore store,
        AgentToolIdentity identity,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.ProjectId == Guid.Empty || !Enum.IsDefined(identity.Provider))
        {
            throw new ArgumentException("The MCP process requires a valid project and provider identity.", nameof(identity));
        }

        _store = store;
        Identity = identity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AgentToolIdentity Identity { get; }

    /// <summary>
    /// Reports that this agent is here. Which native session it is speaking for is Filekin's own
    /// record, made when Filekin opened that session, so this call carries no identity to invent.
    /// </summary>
    public async Task<AgentToolProjectState> ClockInAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.ClockIn(current, Identity.Provider, usage: null),
                cancellationToken)
            .ConfigureAwait(false);

        // Filekin starts a handoff recipient before it releases the sender's lease so the recipient
        // can prove it connected. Do not let the model continue from clock-in during that narrow
        // interval: it would immediately try to accept the handoff while the sender still owns the
        // lease. The app observes the persisted connection, proves the sender stopped, and assigns
        // the recipient before this first tool call returns.
        state = await WaitForHandoffAssignmentIfNeededAsync(state, cancellationToken)
            .ConfigureAwait(false);

        return Project(state);
    }

    private async Task<AgentProjectState> WaitForHandoffAssignmentIfNeededAsync(
        AgentProjectState state,
        CancellationToken cancellationToken) =>
        state.PendingHandoff?.To == Identity.Provider && state.Lease?.Owner != Identity.Provider
            ? await WaitForHandoffAssignmentAsync(cancellationToken).ConfigureAwait(false)
            : state;

    private async Task<AgentProjectState> WaitForHandoffAssignmentAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetTimestamp();
        while (_timeProvider.GetElapsedTime(startedAt) < HandoffAssignmentTimeout)
        {
            var state = await _store.LoadAsync(Identity.ProjectId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Agent project '{Identity.ProjectId:D}' does not exist.");
            if (state.Lease?.Owner == Identity.Provider)
            {
                return state;
            }

            if (state.PendingHandoff?.To != Identity.Provider)
            {
                throw new InvalidOperationException(
                    "Filekin could not assign this handoff after the previous agent stopped.");
            }

            // Measured and slept on the same clock. A real-clock delay against a test clock's deadline
            // never reaches it, so the wait would run without end.
            await Task.Delay(HandoffAssignmentPollInterval, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException("Filekin did not assign this handoff in time.");
    }

    /// <summary>
    /// Reads the coordination state as it is right now.
    /// </summary>
    /// <remarks>
    /// This never waits. Clock-in waits through one narrow interval because a recipient that runs
    /// ahead of its own assignment would try to accept a handoff the sender still owns, but a read is
    /// the call every agent is told to repeat while it works. Waiting here blocked exactly the agent
    /// that was doing as it was told — an agent holding a handoff it had submitted, still working —
    /// for the full timeout, and then answered a routine question with an error instead of the state.
    /// An agent that cannot ask what is happening cannot do the right thing about it.
    /// </remarks>
    public async Task<AgentToolProjectState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(Identity.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{Identity.ProjectId:D}' does not exist.");
        return Project(state);
    }

    public async Task<AgentToolProjectState> SendMessageAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ValidateText(text, MaximumMessageLength, nameof(text));
        var recipient = OtherProvider(Identity.Provider);
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.QueueMessage(
                    current,
                    Identity.Provider,
                    recipient,
                    text,
                    _timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    public async Task<AgentToolProjectState> SubmitHandoffAsync(
        AgentHandoffReason reason,
        string summary,
        string completedWork,
        string remainingWork,
        string verification,
        string blockers,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        ValidateText(summary, MaximumHandoffFieldLength, nameof(summary));
        ValidateOptionalText(completedWork, MaximumHandoffFieldLength, nameof(completedWork));
        ValidateOptionalText(remainingWork, MaximumHandoffFieldLength, nameof(remainingWork));
        ValidateOptionalText(verification, MaximumHandoffFieldLength, nameof(verification));
        ValidateOptionalText(blockers, MaximumHandoffFieldLength, nameof(blockers));

        var handoff = new AgentHandoff(
            Guid.NewGuid(),
            Identity.Provider,
            OtherProvider(Identity.Provider),
            _timeProvider.GetUtcNow(),
            reason,
            summary,
            completedWork,
            remainingWork,
            verification,
            blockers);
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.SubmitHandoff(current, handoff),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    public async Task<AgentToolProjectState> AcceptHandoffAsync(
        CancellationToken cancellationToken = default)
    {
        var before = await _store.LoadAsync(Identity.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent project '{Identity.ProjectId:D}' does not exist.");
        await WaitForHandoffAssignmentIfNeededAsync(before, cancellationToken).ConfigureAwait(false);

        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.AcceptHandoff(
                    current,
                    Identity.Provider,
                    _timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    public async Task<AgentToolProjectState> ReportBlockedAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateText(reason, MaximumMessageLength, nameof(reason));
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.MarkBlocked(current, Identity.Provider, reason),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    public async Task<AgentToolProjectState> ReportUsageLimitAsync(
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateText(nativeSessionId, MaximumSessionIdLength, nameof(nativeSessionId));
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.ReportUsageLimit(
                    current,
                    Identity.Provider,
                    nativeSessionId,
                    $"{ProviderName(Identity.Provider)} reported that its subscription usage limit is reached."),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    public async Task<AgentToolProjectState> ReportCompletedAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _store.UpdateAsync(
                Identity.ProjectId,
                current => AgentProjectCoordinator.ReportCompleted(current, Identity.Provider),
                cancellationToken)
            .ConfigureAwait(false);
        return Project(state);
    }

    private AgentToolProjectState Project(AgentProjectState state) =>
        new(
            state.Id,
            state.FolderPath,
            state.Status,
            Identity.Provider,
            state.ActiveAgent,
            state.AttentionReason,
            state.Participants.Values
                .OrderBy(participant => participant.Provider)
                .Select(participant => new AgentToolParticipantState(
                    participant.Provider,
                    participant.ConnectionState,
                    participant.TurnState,
                    participant.Usage?.MinimumRemainingPercent,
                    participant.Usage?.ObservedAt))
                .ToArray(),
            state.Messages
                .Where(message => message.From == Identity.Provider || message.To == Identity.Provider)
                .Select(message => new AgentToolMessage(
                    message.Id,
                    message.From,
                    message.To,
                    message.SentAt,
                    message.Text))
                .ToArray(),
            Project(state.PendingHandoff),
            Project(state.LastHandoff));

    private static AgentToolHandoff? Project(AgentHandoff? handoff) =>
        handoff is null
            ? null
            : new AgentToolHandoff(
                handoff.Id,
                handoff.From,
                handoff.To,
                handoff.CreatedAt,
                handoff.Reason,
                handoff.Summary,
                handoff.CompletedWork,
                handoff.RemainingWork,
                handoff.Verification,
                handoff.Blockers,
                handoff.AcceptedAt);

    private static AgentProvider OtherProvider(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => AgentProvider.ClaudeCode,
        AgentProvider.ClaudeCode => AgentProvider.Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static string ProviderName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }

    private static void ValidateOptionalText(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }
}
