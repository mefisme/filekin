using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// Builds the project states the control room has to describe, through the coordinator's own public
/// transitions rather than by writing fields.
/// </summary>
/// <remarks>
/// A row is only as honest as the state behind it, so a hand-made state proves nothing: a test that
/// invented "Working" could pass while no real sequence of events ever produced it. Every state here
/// is reached the way the app reaches it, and no database or provider is involved.
/// </remarks>
internal static class AgentProjects
{
    internal const string Folder = @"C:\work\demo";

    internal const string Approval = "Agents may work in this folder itself.";

    internal static readonly DateTimeOffset Now = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    private static readonly AgentCoordinationPolicy Policy = new(
        MinimumRemainingPercent: 10,
        HandoffRequestRemainingPercent: 30,
        MaximumUsageAge: TimeSpan.FromMinutes(5));

    private static readonly AgentProjectCoordinator Coordinator = new(Policy);

    /// <summary>A folder somebody typed <c>/agents</c> in and has not approved yet.</summary>
    internal static AgentProjectState NotSetUp(string objective = "Tidy the build.") =>
        AgentProjectCoordinator.Create(Folder, objective);

    /// <summary>Approved, with nobody started.</summary>
    internal static AgentProjectState Approved(string objective = "Tidy the build.") =>
        AgentProjectCoordinator.GrantSharedCheckoutConsent(NotSetUp(objective), Now, Approval);

    /// <summary>Both agents have reported in and nobody holds the turn.</summary>
    internal static AgentProjectState Ready(string objective = "Tidy the build.")
    {
        var approved = Approved(objective);
        var first = AgentProjectCoordinator.ClockIn(approved, AgentProvider.Codex, usage: null);
        return AgentProjectCoordinator.ClockIn(first, AgentProvider.ClaudeCode, usage: null);
    }

    /// <summary>The turn is reserved for <paramref name="provider"/> and it has reported in.</summary>
    internal static AgentProjectState Working(AgentProvider provider, string objective = "Tidy the build.")
    {
        var reserved = Coordinator.ReserveInitialAgent(Approved(objective), provider, Now);
        return AgentProjectCoordinator.ClockIn(reserved, provider, usage: null);
    }

    /// <summary>Both agents are present; <paramref name="provider"/> holds the turn.</summary>
    internal static AgentProjectState BothPresent(AgentProvider provider) =>
        AgentProjectCoordinator.ClockIn(Working(provider), Other(provider), usage: null);

    internal static AgentProjectState HandingOver(AgentProvider provider) =>
        AgentProjectCoordinator.SubmitHandoff(Working(provider), Handoff(provider));

    internal static AgentProjectState Stopping(AgentProvider provider) =>
        AgentProjectCoordinator.RequestStop(Working(provider), provider);

    internal static AgentProjectState Finishing(AgentProvider provider) =>
        AgentProjectCoordinator.ReportCompleted(Working(provider), provider);

    internal static AgentProjectState NeedsYou(AgentProvider provider) =>
        AgentProjectCoordinator.MarkBlocked(Working(provider), provider, "It cannot read the folder.");

    internal static AgentProjectState Done(AgentProvider provider) =>
        AgentProjectCoordinator.CompleteProject(Finishing(provider), provider);

    /// <summary>A turn that ran and ended, so the agent that took it says Stopped rather than Done.</summary>
    internal static AgentProjectState Stopped(AgentProvider provider)
    {
        var stopping = Stopping(provider);
        return Coordinator.CompleteActiveTurn(stopping, provider, Now);
    }

    /// <summary>A turn that ended with nothing written down, which is the state that asks for a person.</summary>
    internal static AgentProjectState NeedsSomebody(
        AgentProvider provider,
        string reason = "It stopped without saying what it did.") =>
        AgentProjectCoordinator.MarkStoppedWithoutHandoff(Approved(), provider, reason);

    /// <summary>A reading Filekin can show, so the agent's connection reads Ready rather than pending.</summary>
    internal static AgentUsageWindow Window(double usedPercent = 40) =>
        new("primary", usedPercent, WindowDuration: null, ResetsAt: null);

    internal static AgentProjectState WithSession(
        AgentProjectState state,
        AgentProvider provider,
        string nativeSessionId) =>
        AgentProjectCoordinator.RecordNativeSession(state, provider, nativeSessionId);

    /// <summary>
    /// Records a reading for one agent, by whichever route matches where that agent is: an agent that
    /// has clocked in reports its own numbers, and one that has not is read before it is started.
    /// </summary>
    internal static AgentProjectState WithUsage(
        AgentProjectState state,
        AgentProvider provider,
        params AgentUsageWindow[] windows)
    {
        var usage = new AgentUsageSnapshot(provider, Now, windows);
        return state.Participant(provider).ConnectionState == AgentConnectionState.Offline
            ? AgentProjectCoordinator.RecordAllowanceBeforeStart(state, provider, usage)
            : AgentProjectCoordinator.UpdateUsage(state, provider, usage);
    }

    internal static AgentProvider Other(AgentProvider provider) =>
        provider == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex;

    private static AgentHandoff Handoff(AgentProvider from) => new(
        Guid.NewGuid(),
        from,
        Other(from),
        Now,
        AgentHandoffReason.WorkCompleted,
        Summary: "Half of it is done.",
        CompletedWork: "The first half.",
        RemainingWork: "The second half.",
        Verification: "The build is green.",
        Blockers: "None.");
}
