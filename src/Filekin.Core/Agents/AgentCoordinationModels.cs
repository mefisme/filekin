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

    /// <summary>The user asked this agent to stop. It is finishing at a safe point.</summary>
    StopRequested,
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

    /// <summary>
    /// The user asked the active agent to stop. The project is kept and can be resumed; this is not
    /// a failure and never becomes <see cref="NeedsAttention"/>.
    /// </summary>
    StopPending,
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

/// <summary>How far the owner's approval goes when Filekin starts an agent.</summary>
public enum SharedFolderTrust
{
    /// <summary>
    /// Filekin sends no permission or sandbox setting of its own. Each tool uses the settings the
    /// owner already chose for it, and an agent that needs permission waits for them.
    /// </summary>
    UseMyOwnSettings,

    /// <summary>
    /// The owner has said this folder is safe to work in. Filekin scopes each run to that folder:
    /// work inside it needs no prompting, and work outside it fails. Filekin still never approves
    /// anything on the owner's behalf and never bypasses a tool's permission system wholesale.
    /// </summary>
    TrustThisFolder,
}

/// <summary>
/// The owner's approval, for one project, that coordinated agent sessions may work in this folder
/// itself instead of a private copy, and that Filekin's own status-line helper may run. It records
/// what was approved and when. It never holds a credential and never applies to another project.
/// </summary>
/// <param name="ApprovalDescription">
/// The exact words the owner approved. Keeping them means a later Filekin that asks for something
/// wider can tell that the stored approval no longer covers it.
/// </param>
/// <param name="Trust">How far that approval goes when an agent is actually started.</param>
public sealed record SharedCheckoutConsent(
    DateTimeOffset GrantedAt,
    string ApprovalDescription,
    SharedFolderTrust Trust = SharedFolderTrust.UseMyOwnSettings);

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

/// <summary>
/// Safety settings used by the provider-neutral coordinator. <paramref name="MinimumRemainingPercent"/>
/// is the hard cutoff below which a lease can never be granted or transferred.
/// <paramref name="HandoffRequestRemainingPercent"/> is the earlier, more conservative cutoff at which
/// Filekin proactively asks the active agent to wrap up and hand off while it still has real allowance
/// left, rather than waiting for a native usage-limit callback to interrupt it mid-turn.
/// </summary>
public sealed record AgentCoordinationPolicy(
    double MinimumRemainingPercent,
    double HandoffRequestRemainingPercent,
    TimeSpan MaximumUsageAge);
