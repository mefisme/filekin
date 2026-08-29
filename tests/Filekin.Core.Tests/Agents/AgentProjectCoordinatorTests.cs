using Filekin.Core.Agents;

namespace Filekin.Core.Tests.Agents;

[TestClass]
public sealed class AgentProjectCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CreateStartsWithBothProvidersClockedOutAndNoLease()
    {
        var state = AgentProjectCoordinator.Create(".");

        Assert.AreEqual(AgentProjectStatus.ClockingIn, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.ClockedOut, state.Participant(AgentProvider.Codex).TurnState);
        Assert.AreEqual(AgentTurnState.ClockedOut, state.Participant(AgentProvider.ClaudeCode).TurnState);
    }

    [TestMethod]
    public void ClockInKeepsUsagePendingDistinctFromKnownUsage()
    {
        var state = AgentProjectCoordinator.Create(".");

        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, "codex-session", Usage(AgentProvider.Codex, 30));
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, "claude-session", usage: null);

        Assert.AreEqual(AgentProjectStatus.Ready, state.Status);
        Assert.AreEqual(AgentConnectionState.Ready, state.Participant(AgentProvider.Codex).ConnectionState);
        Assert.AreEqual(AgentConnectionState.UsagePending, state.Participant(AgentProvider.ClaudeCode).ConnectionState);
    }

    [TestMethod]
    public void InitialSelectionUsesTheMostConstrainedWindowAndGrantsOneLease()
    {
        var coordinator = Coordinator();
        var state = ClockInBoth(
            Usage(AgentProvider.Codex, ("five-hour", 20), ("weekly", 45)),
            Usage(AgentProvider.ClaudeCode, ("five-hour", 35), ("weekly", 10)));

        state = coordinator.SelectInitialAgent(state, Now);

        Assert.AreEqual(AgentProvider.ClaudeCode, state.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, state.Status);
        Assert.AreEqual(AgentTurnState.Active, state.Participant(AgentProvider.ClaudeCode).TurnState);
        Assert.AreEqual(AgentTurnState.Waiting, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void UnknownStaleAndLowUsageFailClosedWithoutALease()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(".");
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, "codex", Usage(AgentProvider.Codex, 90));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            "claude",
            Usage(AgentProvider.ClaudeCode, Now.AddMinutes(-6), ("five-hour", 10)));

        state = coordinator.SelectInitialAgent(state, Now);

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        StringAssert.Contains(state.AttentionReason, "fresh, known usage");
    }

    [TestMethod]
    public void InitialSelectionWaitsForBothAgentsToClockIn()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(".");
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            "codex-session",
            Usage(AgentProvider.Codex, 10));

        Assert.Throws<InvalidOperationException>(() => coordinator.SelectInitialAgent(state, Now));
        Assert.IsNull(state.Lease);
    }

    [TestMethod]
    public void CleanHandoffTransfersTheLeaseOnlyAfterTheActiveTurnStops()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);

        state = AgentProjectCoordinator.RequestHandoff(
            state,
            AgentProvider.Codex,
            AgentHandoffReason.UsageThreshold);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.UsageThreshold));

        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent, "Submitting text alone must not release the writer.");

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(5));

        Assert.AreEqual(AgentProvider.ClaudeCode, state.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, state.Status);
        Assert.IsNull(state.PendingHandoff);
        Assert.IsNotNull(state.LastHandoff);
        Assert.AreEqual(AgentTurnState.Waiting, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void StoppingWithoutAHandoffNeedsAttentionAndReleasesTheStaleLease()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(5));

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.NeedsAttention, state.Participant(AgentProvider.Codex).TurnState);
        StringAssert.Contains(state.AttentionReason, "without submitting a handoff");
    }

    [TestMethod]
    public void UnsafeRecipientLeavesTheCompletedHandoffVisibleButPauses()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 85)),
            Now);
        state = AgentProjectCoordinator.RequestHandoff(
            state,
            AgentProvider.Codex,
            AgentHandoffReason.WorkCompleted);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.WorkCompleted));

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(5));

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        Assert.IsNull(state.PendingHandoff);
        Assert.IsNotNull(state.LastHandoff);
    }

    [TestMethod]
    public void BlockedAgentRetainsLeaseUntilItsNativeSessionIsProvenStopped()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = AgentProjectCoordinator.MarkBlocked(state, AgentProvider.Codex, "Approval required");

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);
        Assert.AreEqual(AgentTurnState.Blocked, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void RestartReconciliationNeverRetainsAnUnverifiedWriterLease()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = AgentProjectCoordinator.ReconcileAfterRestart(state);

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.NeedsAttention, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void RestartReconciliationLeavesAnIdleProjectUnchanged()
    {
        var state = ClockInBoth(
            Usage(AgentProvider.Codex, 10),
            Usage(AgentProvider.ClaudeCode, 20));

        var reconciled = AgentProjectCoordinator.ReconcileAfterRestart(state);

        Assert.AreSame(state, reconciled);
        Assert.AreEqual(AgentProjectStatus.Ready, reconciled.Status);
    }

    [TestMethod]
    public void UnavailableActiveProviderRetainsLeaseWhileIdleProjectFailsClosed()
    {
        var coordinator = Coordinator();
        var ready = ClockInBoth(
            Usage(AgentProvider.Codex, 10),
            Usage(AgentProvider.ClaudeCode, 20));
        var active = coordinator.SelectInitialAgent(ready, Now);

        active = AgentProjectCoordinator.MarkProviderUnavailable(
            active,
            active.ActiveAgent!.Value,
            "Provider facts unavailable.");
        var idle = AgentProjectCoordinator.MarkProviderUnavailable(
            ready,
            AgentProvider.ClaudeCode,
            "Provider facts unavailable.");
        var afterRestart = AgentProjectCoordinator.MarkProviderUnavailable(
            AgentProjectCoordinator.ReconcileAfterRestart(
                coordinator.SelectInitialAgent(ready, Now)),
            AgentProvider.Codex,
            "Provider facts unavailable.");

        Assert.IsNotNull(active.Lease, "Inspection failure is not proof that the native writer stopped.");
        Assert.AreEqual(AgentProjectStatus.NeedsAttention, active.Status);
        Assert.AreEqual(AgentProjectStatus.Paused, idle.Status);
        Assert.AreEqual(
            AgentConnectionState.Unavailable,
            idle.Participant(AgentProvider.ClaudeCode).ConnectionState);
        Assert.IsNull(idle.Participant(AgentProvider.ClaudeCode).Usage);
        Assert.AreEqual(AgentProjectStatus.NeedsAttention, afterRestart.Status);
        StringAssert.Contains(afterRestart.AttentionReason, "reconciled");
    }

    [TestMethod]
    public void TargetedMessageDoesNotWakeOrActivateTheWaitingAgent()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = AgentProjectCoordinator.QueueMessage(
            state,
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            "Please check the serialization boundary when your turn begins.",
            Now.AddSeconds(1));

        Assert.HasCount(1, state.Messages);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);
        Assert.AreEqual(AgentTurnState.Waiting, state.Participant(AgentProvider.ClaudeCode).TurnState);
    }

    [TestMethod]
    public void ProviderConfirmedCompletionReleasesTheLeaseAndCompletesTheProject()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = AgentProjectCoordinator.CompleteProject(state, AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.Completed, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.Completed, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void CompletionReportRetainsLeaseUntilProviderConfirmsStop()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);

        state = AgentProjectCoordinator.ReportCompleted(state, AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.CompletionPending, state.Status);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);
        Assert.AreEqual(
            AgentTurnState.CompletionReported,
            state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void HandoffRecipientCanAcceptOnlyAfterItOwnsTheLease()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now);
        state = AgentProjectCoordinator.RequestHandoff(
            state,
            AgentProvider.Codex,
            AgentHandoffReason.UserRequested);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.UserRequested));

        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.AcceptHandoff(state, AgentProvider.ClaudeCode, Now));

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(5));
        state = AgentProjectCoordinator.AcceptHandoff(
            state,
            AgentProvider.ClaudeCode,
            Now.AddSeconds(6));

        Assert.AreEqual(Now.AddSeconds(6), state.LastHandoff?.AcceptedAt);
    }

    private static AgentProjectState ClockInBoth(
        AgentUsageSnapshot codex,
        AgentUsageSnapshot claude)
    {
        var state = AgentProjectCoordinator.Create(".");
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, "codex-session", codex);
        return AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, "claude-session", claude);
    }

    private static AgentUsageSnapshot Usage(AgentProvider provider, double usedPercent) =>
        Usage(provider, Now, ("primary", usedPercent));

    private static AgentUsageSnapshot Usage(
        AgentProvider provider,
        params (string Name, double UsedPercent)[] windows) =>
        Usage(provider, Now, windows);

    private static AgentUsageSnapshot Usage(
        AgentProvider provider,
        DateTimeOffset observedAt,
        params (string Name, double UsedPercent)[] windows) =>
        new(
            provider,
            observedAt,
            windows.Select(window => new AgentUsageWindow(
                window.Name,
                window.UsedPercent,
                TimeSpan.FromMinutes(300),
                Now.AddHours(1))).ToArray());

    private static AgentHandoff Handoff(
        AgentProvider from,
        AgentProvider to,
        AgentHandoffReason reason) =>
        new(
            Guid.NewGuid(),
            from,
            to,
            Now.AddSeconds(2),
            reason,
            "Coordinator core is implemented.",
            "Added models and state transitions.",
            "Build provider adapter.",
            "Core tests pass.",
            string.Empty);

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(20, TimeSpan.FromMinutes(5)));
}
