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

    /// <summary>Whether the provider's own reset time for this window has already passed.</summary>
    public bool HasResetBy(DateTimeOffset now) => ResetsAt is { } resetsAt && resetsAt <= now;

    /// <summary>
    /// What is left in this window now. A window whose reset time has passed is full again, and both
    /// providers say when that is, so this is a fact rather than a guess.
    /// </summary>
    public double RemainingPercentAt(DateTimeOffset now) =>
        HasResetBy(now) ? 100 : RemainingPercent;
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

    /// <summary>
    /// The most constrained window as it stands now, counting a window whose reset time has passed as
    /// full again.
    /// </summary>
    public double? MinimumRemainingPercentAt(DateTimeOffset now) =>
        IsKnown ? Windows.Min(window => window.RemainingPercentAt(now)) : null;

    public bool IsFresh(DateTimeOffset now, TimeSpan maximumAge) =>
        IsKnown && ObservedAt <= now && now - ObservedAt <= maximumAge;

    /// <summary>
    /// Whether this reading still answers "how much is left?" honestly.
    /// </summary>
    /// <remarks>
    /// A recent reading does. So does an old reading whose every window has since reset, because a
    /// window past its reset time is full whatever happened in between — and that is the case that
    /// lets Filekin answer before starting anything. A reading with one stale window that has not
    /// reset answers nothing: work Filekin never saw may have spent it.
    /// </remarks>
    public bool IsUsable(DateTimeOffset now, TimeSpan maximumAge) =>
        IsFresh(now, maximumAge) ||
        (IsKnown && ObservedAt <= now && Windows.All(window => window.HasResetBy(now)));
}

/// <param name="PreferredModel">
/// The model the user chose for this agent, or <see langword="null"/> to let the tool use whatever
/// its own configuration selects. Filekin passes it at launch and never writes it into the user's
/// own tool settings.
/// </param>
/// <param name="PreferredEffort">
/// How hard the user asked that model to think, in that tool's own words, or <see langword="null"/>
/// for the tool's own default. Effort changes what a turn costs, so it is the user's choice.
/// </param>
/// <param name="HasWorkedOnObjective">
/// Whether this agent has held the turn since the current objective was written. A saved
/// conversation is memory of any job, so it cannot answer this; without it an agent that never
/// started the job in hand reads as one that stopped in the middle of it. A new objective clears it.
/// </param>
public sealed record AgentParticipant(
    AgentProvider Provider,
    string? NativeSessionId,
    AgentConnectionState ConnectionState,
    AgentTurnState TurnState,
    AgentUsageSnapshot? Usage,
    string? PreferredModel = null,
    string? PreferredEffort = null,
    bool HasWorkedOnObjective = false);

public sealed record WorkingTreeLease(Guid Id, AgentProvider Owner, DateTimeOffset AcquiredAt);

/// <summary>
/// How an agent may work in this folder. Filekin starts agents where nobody can answer a permission
/// question — a background session and an App Server turn both have no window — so the answer has to
/// be chosen before the launch. Each value is one setting Filekin sends to each tool, and the stored
/// number is the owner's answer, changeable while nothing is running.
/// </summary>
public enum AgentWorkMode
{
    /// <summary>
    /// Filekin sends no permission or sandbox setting of its own. Each tool uses the settings the
    /// owner already chose for it, and an agent that needs permission waits for them.
    /// </summary>
    UseMyOwnSettings,

    /// <summary>
    /// The owner has trusted the agent to work automatically. Filekin selects each provider's
    /// supported trusted/automatic mode; their exact boundaries differ. Filekin still never approves
    /// anything on the owner's behalf or bypasses a tool's permission system wholesale.
    /// </summary>
    WorkOnItsOwn,

    /// <summary>
    /// The agent may read this folder and think, and may change nothing: no file is written and no
    /// command is run. It is the answer for looking at unfamiliar work, and it is the strictest thing
    /// Filekin can ask for that both tools understand.
    /// </summary>
    LookDontTouch,
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
/// <param name="WorkMode">How the agent may work in the folder once it is actually started.</param>
public sealed record SharedCheckoutConsent(
    DateTimeOffset GrantedAt,
    string ApprovalDescription,
    AgentWorkMode WorkMode = AgentWorkMode.UseMyOwnSettings);

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
