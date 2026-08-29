namespace Filekin.Core.Agents;

public enum AgentProvider
{
    Codex,
    ClaudeCode,
}

public enum AgentConnectionState
{
    Offline,
    UsagePending,
    Ready,
    Unavailable,
}

public enum AgentTurnState
{
    ClockedOut,
    Waiting,
    Active,
    HandoffRequested,
    Blocked,
    NeedsAttention,
    CompletionReported,
    Completed,
}

public enum AgentProjectStatus
{
    ClockingIn,
    Ready,
    Working,
    HandoffPending,
    Paused,
    NeedsAttention,
    CompletionPending,
    Completed,
}

public enum AgentHandoffReason
{
    WorkCompleted,
    UsageThreshold,
    UserRequested,
}

/// <summary>One provider-reported quota window. Windows remain separate by design.</summary>
public sealed record AgentUsageWindow(
    string Name,
    double UsedPercent,
    TimeSpan? WindowDuration,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => 100 - UsedPercent;
}

/// <summary>A non-secret observation from one provider's supported local interface.</summary>
public sealed record AgentUsageSnapshot(
    AgentProvider Provider,
    DateTimeOffset ObservedAt,
    IReadOnlyList<AgentUsageWindow> Windows)
{
    public bool IsKnown => Windows.Count > 0;

    /// <summary>The most constrained reported window determines whether starting more work is safe.</summary>
    public double? MinimumRemainingPercent =>
        IsKnown ? Windows.Min(window => window.RemainingPercent) : null;

    public bool IsFresh(DateTimeOffset now, TimeSpan maximumAge) =>
        IsKnown && ObservedAt <= now && now - ObservedAt <= maximumAge;
}

public sealed record AgentParticipant(
    AgentProvider Provider,
    string? NativeSessionId,
    AgentConnectionState ConnectionState,
    AgentTurnState TurnState,
    AgentUsageSnapshot? Usage);

public sealed record WorkingTreeLease(Guid Id, AgentProvider Owner, DateTimeOffset AcquiredAt);

public sealed record AgentMessage(
    Guid Id,
    AgentProvider From,
    AgentProvider To,
    DateTimeOffset SentAt,
    string Text);

public sealed record AgentHandoff(
    Guid Id,
    AgentProvider From,
    AgentProvider To,
    DateTimeOffset CreatedAt,
    AgentHandoffReason Reason,
    string Summary,
    string CompletedWork,
    string RemainingWork,
    string Verification,
    string Blockers,
    DateTimeOffset? AcceptedAt = null);

/// <summary>Safety settings used by the provider-neutral coordinator.</summary>
public sealed record AgentCoordinationPolicy(double MinimumRemainingPercent, TimeSpan MaximumUsageAge)
;
