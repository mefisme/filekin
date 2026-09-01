using Filekin.Core.Agents;

namespace Filekin.Core.Tests.Agents;

[TestClass]
public sealed class AgentProjectCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PolicyRequiresTheHandoffRequestPercentAboveTheHardCutoffAndAtMostOneHundred()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentProjectCoordinator(new AgentCoordinationPolicy(20, 20, TimeSpan.FromMinutes(5))));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentProjectCoordinator(new AgentCoordinationPolicy(20, 10, TimeSpan.FromMinutes(5))));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentProjectCoordinator(new AgentCoordinationPolicy(20, 101, TimeSpan.FromMinutes(5))));
        _ = new AgentProjectCoordinator(new AgentCoordinationPolicy(20, 100, TimeSpan.FromMinutes(5)));
    }

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

        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 30));
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, usage: null);

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
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 90));
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, Now.AddMinutes(-6), ("five-hour", 10)));

        state = coordinator.SelectInitialAgent(state, Now);

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        StringAssert.Contains(state.AttentionReason, "fresh, known usage");
    }

    [TestMethod]
    public void OneClockedInAgentIsEnoughToStartWork()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(".");
        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 10));

        state = coordinator.SelectInitialAgent(state, Now);

        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, state.Status);
        Assert.AreEqual(
            AgentTurnState.ClockedOut,
            state.Participant(AgentProvider.ClaudeCode).TurnState,
            "The relay begins only when the second agent clocks in.");
    }

    [TestMethod]
    public void InitialSelectionStillNeedsSomebodyClockedIn()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(".");

        Assert.Throws<InvalidOperationException>(() => coordinator.SelectInitialAgent(state, Now));
        Assert.IsNull(state.Lease);
    }

    [TestMethod]
    public void TheUsersChoiceBeatsTheAgentWithMoreAllowanceLeft()
    {
        var coordinator = Coordinator();
        var state = ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 50));

        state = coordinator.SelectInitialAgent(state, Now, AgentProvider.ClaudeCode);

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            state.ActiveAgent,
            "Codex has more allowance left, but the user chose Claude Code.");
    }

    [TestMethod]
    public void AChosenAgentThatCannotStartPausesInsteadOfStartingTheOtherOne()
    {
        var coordinator = Coordinator();
        var state = ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 95));

        state = coordinator.SelectInitialAgent(state, Now, AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        StringAssert.Contains(state.AttentionReason, "Claude Code");
    }

    [TestMethod]
    public void AStopRequestIsCooperativeAndKeepsTheLease()
    {
        var state = Working();

        state = AgentProjectCoordinator.RequestStop(state, AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.StopPending, state.Status);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent, "A stop request must not release the lease.");
        Assert.AreEqual(AgentTurnState.StopRequested, state.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void OnlyTheAgentHoldingTheTurnCanBeAskedToStop()
    {
        var state = Working();

        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.RequestStop(state, AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public void AStopTheUserAskedForEndsInAResumablePauseNotAFailure()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RequestStop(Working(), AgentProvider.Codex);

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddMinutes(1));

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.Waiting, state.Participant(AgentProvider.Codex).TurnState);
        StringAssert.Contains(state.AttentionReason, "resumed");
    }

    [TestMethod]
    public void AHandoffSubmittedBeforeAStopIsKeptAsHistoryWithoutStartingThePartner()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RequestHandoff(
            Working(),
            AgentProvider.Codex,
            AgentHandoffReason.UserRequested);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.UserRequested));
        state = AgentProjectCoordinator.RequestStop(state, AgentProvider.Codex);

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddMinutes(1));

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNotNull(state.LastHandoff, "The written handoff is still the project's history.");
        Assert.IsNull(state.PendingHandoff);
        Assert.AreEqual(
            AgentTurnState.Waiting,
            state.Participant(AgentProvider.ClaudeCode).TurnState,
            "Stopping is what was asked for, so the partner does not take over.");
    }

    [TestMethod]
    public void ResumeReturnsAStoppedProjectToReady()
    {
        var coordinator = Coordinator();
        var state = coordinator.CompleteActiveTurn(
            AgentProjectCoordinator.RequestStop(Working(), AgentProvider.Codex),
            AgentProvider.Codex,
            Now.AddMinutes(1));

        var resumed = AgentProjectCoordinator.Resume(state);

        Assert.AreEqual(AgentProjectStatus.Ready, resumed.Status);
        Assert.IsNull(resumed.AttentionReason);
        Assert.IsNull(resumed.Lease, "Resuming only clears the pause; selection grants the next turn.");
    }

    [TestMethod]
    public void OnlyAPausedProjectCanBeResumed()
    {
        Assert.Throws<InvalidOperationException>(() => AgentProjectCoordinator.Resume(Working()));
    }

    [TestMethod]
    public void ARestartDuringAStopRequestStillNeedsAttention()
    {
        var state = AgentProjectCoordinator.RequestStop(Working(), AgentProvider.Codex);

        var reconciled = AgentProjectCoordinator.ReconcileAfterRestart(state);

        Assert.AreEqual(
            AgentTurnState.NeedsAttention,
            reconciled.Participant(AgentProvider.Codex).TurnState,
            "Filekin did not see the stop finish, so it must not assume it did.");
        Assert.IsNull(reconciled.Lease);
    }

    [TestMethod]
    public void ActiveAgentBelowTheHandoffThresholdWithASafePartnerIsAskedToHandOff()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 65), Usage(AgentProvider.ClaudeCode, 75)),
            Now);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent, "Codex has more remaining headroom (35 vs 25).");

        var evaluated = coordinator.EvaluateUsageHandoff(state, Now);

        Assert.AreEqual(AgentProjectStatus.HandoffPending, evaluated.Status);
        Assert.AreEqual(AgentHandoffReason.UsageThreshold, evaluated.RequestedHandoffReason);
        Assert.AreEqual(AgentProvider.Codex, evaluated.ActiveAgent, "A request must not release the lease.");
        Assert.AreEqual(
            AgentTurnState.HandoffRequested,
            evaluated.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void ActiveAgentAboveTheHandoffThresholdIsLeftWorking()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 55), Usage(AgentProvider.ClaudeCode, 75)),
            Now);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent, "Codex remaining is 45, still above the 40 threshold.");

        var evaluated = coordinator.EvaluateUsageHandoff(state, Now);

        Assert.AreSame(state, evaluated, "45% remaining is above the handoff threshold, so nothing changes.");
    }

    [TestMethod]
    public void AStaleActiveAgentObservationNeverTriggersAGuessedHandoffRequest()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 65), Usage(AgentProvider.ClaudeCode, 75)),
            Now);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent);

        // Codex remaining (35) is below the 40 threshold, but the observation is now older than the
        // 5-minute freshness window, so Filekin must not act on it.
        var evaluated = coordinator.EvaluateUsageHandoff(state, Now.AddMinutes(6));

        Assert.AreSame(state, evaluated);
    }

    [TestMethod]
    public void BothParticipantsLowDefersTheAutomaticRequestInsteadOfAskingForADoomedHandoff()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 65), Usage(AgentProvider.ClaudeCode, 85)),
            Now);
        Assert.AreEqual(AgentProvider.Codex, state.ActiveAgent, "Codex has more remaining headroom (35 vs 15).");

        var evaluated = coordinator.EvaluateUsageHandoff(state, Now);

        Assert.AreSame(
            state,
            evaluated,
            "Claude cannot safely receive the lease yet, so Codex keeps working rather than being asked " +
            "for a handoff nobody could complete. If Codex genuinely stops later, CompleteActiveTurn's " +
            "existing safeguard pauses the project instead.");
    }

    [TestMethod]
    public void EvaluatingUsageHandoffWithoutAnActiveLeaseIsANoOp()
    {
        var coordinator = Coordinator();
        var state = ClockInBoth(Usage(AgentProvider.Codex, 65), Usage(AgentProvider.ClaudeCode, 75));

        var evaluated = coordinator.EvaluateUsageHandoff(state, Now);

        Assert.AreSame(state, evaluated);
    }

    [TestMethod]
    public void EvaluatingUsageHandoffAfterAlreadyRequestingOneIsIdempotent()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 65), Usage(AgentProvider.ClaudeCode, 75)),
            Now);
        var requested = coordinator.EvaluateUsageHandoff(state, Now);
        Assert.AreEqual(AgentProjectStatus.HandoffPending, requested.Status);

        var evaluatedAgain = coordinator.EvaluateUsageHandoff(requested, Now);

        Assert.AreSame(requested, evaluatedAgain);
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
    public void IgnoringAHandoffRequestNeedsAttentionAndReleasesTheStaleLease()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RequestHandoff(
            Working(),
            AgentProvider.Codex,
            AgentHandoffReason.UsageThreshold);

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(5));

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.NeedsAttention, state.Participant(AgentProvider.Codex).TurnState);
        StringAssert.Contains(state.AttentionReason, "asked to hand over");
    }

    [TestMethod]
    public void AnAgentThatSimplyFinishesItsTurnGivesItBackWithoutAskingForHelp()
    {
        var coordinator = Coordinator();

        var state = coordinator.CompleteActiveTurn(Working(), AgentProvider.Codex, Now.AddSeconds(5));

        Assert.AreEqual(
            AgentProjectStatus.Ready,
            state.Status,
            "Ending its own turn is what an agent does when it is done talking, not a failure.");
        Assert.IsNull(state.Lease);
        Assert.AreEqual(AgentTurnState.Waiting, state.Participant(AgentProvider.Codex).TurnState);
        StringAssert.Contains(state.AttentionReason, "finished its turn");
    }

    [TestMethod]
    public void AProjectThatNeedsAttentionCanBeClearedOnceSomebodyHasLooked()
    {
        var coordinator = Coordinator();
        var state = coordinator.CompleteActiveTurn(
            AgentProjectCoordinator.RequestHandoff(Working(), AgentProvider.Codex, AgentHandoffReason.UsageThreshold),
            AgentProvider.Codex,
            Now.AddSeconds(5));

        var cleared = AgentProjectCoordinator.ClearAttention(state);

        Assert.AreEqual(AgentProjectStatus.Ready, cleared.Status);
        Assert.IsNull(cleared.AttentionReason);
        Assert.AreEqual(AgentTurnState.Waiting, cleared.Participant(AgentProvider.Codex).TurnState);
    }

    [TestMethod]
    public void AProjectWhoseTurnIsStillHeldCannotBeCleared()
    {
        var blocked = AgentProjectCoordinator.MarkBlocked(Working(), AgentProvider.Codex, "Needs a password.");

        Assert.Throws<InvalidOperationException>(() => AgentProjectCoordinator.ClearAttention(blocked));
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
    public void UsageLimitCallbackCanArriveBeforeClockInAndFailsClosed()
    {
        var state = AgentProjectCoordinator.Create(".");

        state = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
            "claude-session",
            "Claude Code reported a usage limit.");

        var claude = state.Participant(AgentProvider.ClaudeCode);
        Assert.AreEqual("claude-session", claude.NativeSessionId);
        Assert.AreEqual(AgentConnectionState.Unavailable, claude.ConnectionState);
        Assert.AreEqual(AgentTurnState.Waiting, claude.TurnState);
        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        Assert.IsNull(state.Lease);
        StringAssert.Contains(state.AttentionReason, "usage limit");
    }

    [TestMethod]
    public void UsageLimitCallbackRetainsAnActiveWriterLeaseAndKeepsTheKnownSession()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 20), Usage(AgentProvider.ClaudeCode, 10)),
            Now);
        Assert.AreEqual(AgentProvider.ClaudeCode, state.ActiveAgent);

        state = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
            "claude-session",
            "Claude Code reported a usage limit.");

        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);
        Assert.AreEqual(AgentProvider.ClaudeCode, state.ActiveAgent);
        Assert.AreEqual(
            AgentTurnState.Blocked,
            state.Participant(AgentProvider.ClaudeCode).TurnState);
        Assert.AreEqual(
            AgentConnectionState.Unavailable,
            state.Participant(AgentProvider.ClaudeCode).ConnectionState);
        var reportedAgain = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
            "another-identifier",
            "Claude Code reported a usage limit.");

        Assert.AreEqual(
            "claude-session",
            reportedAgain.Participant(AgentProvider.ClaudeCode).NativeSessionId,
            "A callback never replaces the session identity already known for this agent.");
        Assert.AreEqual(AgentProvider.ClaudeCode, reportedAgain.ActiveAgent);
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
    public void OnlyACompletedProjectCanBeOpenedForAnotherJob()
    {
        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.StartNewObjective(
                AgentProjectCoordinator.Create(".", "Tidy the build."),
                "Write the release notes."));
        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.StartNewObjective(Working(), "Write the release notes."));
    }

    [TestMethod]
    public void ANewJobKeepsWhatTheProjectIsAndClearsWhatTheLastRunWas()
    {
        var completed = CompletedProject();

        var reopened = AgentProjectCoordinator.StartNewObjective(completed, "  Write the release notes.  ");

        Assert.AreEqual(AgentProjectStatus.Ready, reopened.Status);
        Assert.AreEqual("Write the release notes.", reopened.Objective);
        Assert.IsNull(reopened.Lease);
        Assert.IsNull(reopened.AttentionReason);

        // What the folder is stays true, and what already happened is still readable.
        Assert.AreEqual(completed.SharedCheckoutConsent, reopened.SharedCheckoutConsent);
        Assert.IsTrue(reopened.WorkOnLowAllowance);
        Assert.AreEqual(completed.LastHandoff?.Id, reopened.LastHandoff?.Id);
        Assert.HasCount(completed.Messages.Count, reopened.Messages);

        // The sessions of the finished job are gone; nothing may be reused as if it were live.
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            var participant = reopened.Participant(provider);
            Assert.IsNull(participant.NativeSessionId);
            Assert.AreEqual(AgentConnectionState.Offline, participant.ConnectionState);
            Assert.AreEqual(AgentTurnState.ClockedOut, participant.TurnState);
            Assert.IsNotNull(participant.Usage, "Allowance is a quota fact, not a session fact.");
        }
    }

    [TestMethod]
    public void ANewJobMayKeepTheSameObjectiveAndStillReturnsTheProjectToReady()
    {
        var completed = CompletedProject();

        var reopened = AgentProjectCoordinator.StartNewObjective(completed, completed.Objective);

        Assert.AreEqual(completed.Objective, reopened.Objective);
        Assert.AreEqual(AgentProjectStatus.Ready, reopened.Status);
    }

    [TestMethod]
    public void FilekinOwnsTheNativeSessionIdentityAndClockingInOnlyReportsPresence()
    {
        var state = AgentProjectCoordinator.RecordNativeSession(
            AgentProjectCoordinator.Create("."),
            AgentProvider.Codex,
            "codex-session-filekin-opened");

        var codex = state.Participant(AgentProvider.Codex);
        Assert.AreEqual("codex-session-filekin-opened", codex.NativeSessionId);
        Assert.AreEqual(AgentConnectionState.Offline, codex.ConnectionState, "Recording an identity is not presence.");
        Assert.AreEqual(AgentTurnState.ClockedOut, codex.TurnState);
        Assert.IsNull(state.Lease);

        var clockedIn = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 10));

        Assert.AreEqual(
            "codex-session-filekin-opened",
            clockedIn.Participant(AgentProvider.Codex).NativeSessionId,
            "Clocking in cannot name, invent, or replace the session it is speaking for.");
        Assert.AreEqual(AgentConnectionState.Ready, clockedIn.Participant(AgentProvider.Codex).ConnectionState);
    }

    [TestMethod]
    public void AUsageLimitCallbackFailsClosedWithoutReplacingTheRecordedSession()
    {
        var state = AgentProjectCoordinator.RecordNativeSession(
            AgentProjectCoordinator.Create("."),
            AgentProvider.ClaudeCode,
            "claude-background-session");

        // A provider names its own identifier for the session, which need not be the one Filekin
        // drives it by. The report still counts; the recorded identity does not move.
        var limited = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
            "claude-conversation-session",
            "Claude Code reported that its subscription usage limit is reached.");

        Assert.AreEqual(
            "claude-background-session",
            limited.Participant(AgentProvider.ClaudeCode).NativeSessionId);
        Assert.AreEqual(
            AgentConnectionState.Unavailable,
            limited.Participant(AgentProvider.ClaudeCode).ConnectionState);
        Assert.AreEqual(AgentProjectStatus.Paused, limited.Status);
        Assert.IsNull(limited.Lease);
    }

    [TestMethod]
    public void ASessionThatEndsWithoutTheTurnOnlyMeansThatAgentIsNoLongerHere()
    {
        var state = Working();
        var owner = state.ActiveAgent!.Value;
        var partner = owner == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex;

        var ended = AgentProjectCoordinator.RecordSessionEnded(state, partner);

        Assert.AreEqual(AgentConnectionState.Offline, ended.Participant(partner).ConnectionState);
        Assert.AreEqual(AgentTurnState.ClockedOut, ended.Participant(partner).TurnState);
        Assert.AreEqual(owner, ended.ActiveAgent, "The turn belongs to somebody else and does not move.");
        Assert.AreEqual(AgentProjectStatus.Working, ended.Status);

        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.RecordSessionEnded(ended, owner),
            "The lease owner's stop releases a turn and belongs to CompleteActiveTurn.");
    }

    [TestMethod]
    public void EachAgentCarriesItsOwnModelAndEffort()
    {
        var state = AgentProjectCoordinator.Create(".");

        state = AgentProjectCoordinator.ChooseModel(state, AgentProvider.ClaudeCode, " opus ", " high ");
        state = AgentProjectCoordinator.ChooseModel(state, AgentProvider.Codex, "gpt-5.6-sol");

        Assert.AreEqual("opus", state.Participant(AgentProvider.ClaudeCode).PreferredModel);
        Assert.AreEqual("high", state.Participant(AgentProvider.ClaudeCode).PreferredEffort);
        Assert.AreEqual("gpt-5.6-sol", state.Participant(AgentProvider.Codex).PreferredModel);
        Assert.IsNull(
            state.Participant(AgentProvider.Codex).PreferredEffort,
            "An unspoken effort is the tool's own, not a value Filekin invents.");
        Assert.AreEqual(
            AgentConnectionState.Offline,
            state.Participant(AgentProvider.Codex).ConnectionState,
            "Choosing a model starts nothing.");

        var cleared = AgentProjectCoordinator.ChooseModel(state, AgentProvider.ClaudeCode, "  ", "high");

        Assert.IsNull(cleared.Participant(AgentProvider.ClaudeCode).PreferredModel);
        Assert.AreEqual("gpt-5.6-sol", cleared.Participant(AgentProvider.Codex).PreferredModel);
    }

    [TestMethod]
    public void TheWorkingAgentCanHandOverWithoutBeingAsked()
    {
        var state = Working();
        var owner = state.ActiveAgent!.Value;
        var partner = owner == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex;

        var submitted = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(owner, partner, AgentHandoffReason.WorkCompleted));

        Assert.AreEqual(AgentProjectStatus.HandoffPending, submitted.Status);
        Assert.AreEqual(AgentHandoffReason.WorkCompleted, submitted.PendingHandoff?.Reason);
        Assert.AreEqual(owner, submitted.ActiveAgent, "Writing a handoff never releases the turn.");
        Assert.AreEqual(AgentTurnState.HandoffRequested, submitted.Participant(owner).TurnState);
    }

    [TestMethod]
    public void AnAgentCannotClaimTheAllowanceOrTheUserAsItsReasonForHandingOver()
    {
        var state = Working();
        var owner = state.ActiveAgent!.Value;
        var partner = owner == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex;

        var submitted = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(owner, partner, AgentHandoffReason.UsageThreshold));

        Assert.AreEqual(
            AgentHandoffReason.WorkCompleted,
            submitted.PendingHandoff?.Reason,
            "Allowance is Filekin's own reading, so an agent cannot give it as the reason.");
    }

    [TestMethod]
    public void AStopTheUserAskedForIsNotTurnedIntoAHandOverByTheAgent()
    {
        var state = Working();
        var owner = state.ActiveAgent!.Value;
        var partner = owner == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex;
        state = AgentProjectCoordinator.RequestStop(state, owner);

        var submitted = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(owner, partner, AgentHandoffReason.WorkCompleted));

        Assert.AreEqual(AgentProjectStatus.StopPending, submitted.Status);
        Assert.IsNull(submitted.RequestedHandoffReason);
        Assert.IsNotNull(submitted.PendingHandoff, "What the agent wrote is still kept.");

        var stopped = Coordinator().CompleteActiveTurn(submitted, owner, Now.AddMinutes(1));

        Assert.AreEqual(AgentProjectStatus.Paused, stopped.Status);
        Assert.IsNull(stopped.ActiveAgent, "Stopping is what was asked for; the partner is not started.");
        Assert.IsNotNull(stopped.LastHandoff);
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
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, codex);
        return AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, claude);
    }

    [TestMethod]
    public void ApprovingTheSharedFolderIsRecordedWithoutTouchingTheTurn()
    {
        var state = AgentProjectCoordinator.Create(".");
        Assert.IsNull(state.SharedCheckoutConsent, "A new project has approved nothing.");

        var approved = AgentProjectCoordinator.GrantSharedCheckoutConsent(state, Now, "Work in this folder.");

        Assert.AreEqual(Now, approved.SharedCheckoutConsent?.GrantedAt);
        Assert.AreEqual("Work in this folder.", approved.SharedCheckoutConsent?.ApprovalDescription);
        Assert.AreEqual(state.Status, approved.Status);
        Assert.IsNull(approved.Lease);
        Assert.AreEqual(
            AgentTurnState.ClockedOut,
            approved.Participant(AgentProvider.Codex).TurnState,
            "Approving is not starting.");
    }

    [TestMethod]
    public void AllowanceCanBeRecordedBeforeAnAgentIsHereWithoutMakingItLookPresent()
    {
        var state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            AgentProjectCoordinator.Create("."),
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 40));

        var participant = state.Participant(AgentProvider.Codex);
        Assert.AreEqual(60, participant.Usage?.MinimumRemainingPercent);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            participant.ConnectionState,
            "Knowing an account's allowance is not the same as the agent being here.");
        Assert.AreEqual(AgentTurnState.ClockedOut, participant.TurnState);
    }

    [TestMethod]
    public void AnAgentThatHasClockedInReportsItsOwnUsageInstead()
    {
        var state = ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 10));

        Assert.Throws<InvalidOperationException>(() => AgentProjectCoordinator.RecordAllowanceBeforeStart(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 20)));
    }

    [TestMethod]
    public void TheAgentWithMoreAllowanceLeftIsTheOneFilekinWouldStart()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            AgentProjectCoordinator.RecordAllowanceBeforeStart(
                AgentProjectCoordinator.Create("."),
                AgentProvider.Codex,
                Usage(AgentProvider.Codex, 70)),
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, 25));

        Assert.AreEqual(AgentProvider.ClaudeCode, coordinator.ChooseAgentToStart(state, Now));
    }

    [TestMethod]
    public void AnAgentWhoseAllowanceIsUnknownRanksBelowOneKnownToBeSafe()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            AgentProjectCoordinator.Create("."),
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, 25));

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            coordinator.ChooseAgentToStart(state, Now),
            "Codex's allowance is unknown, so the agent Filekin can vouch for goes first.");
    }

    [TestMethod]
    public void AnAgentWithNoAllowanceLeftIsNeverStartedAndIsSaidSo()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            AgentProjectCoordinator.RecordAllowanceBeforeStart(
                AgentProjectCoordinator.Create("."),
                AgentProvider.Codex,
                Usage(AgentProvider.Codex, 95)),
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, 95));

        Assert.IsNull(coordinator.ChooseAgentToStart(state, Now));
        Assert.IsFalse(coordinator.HasStartableAllowance(state, AgentProvider.Codex, Now));
    }

    [TestMethod]
    public void AnAllowanceReadingTooOldToTrustDoesNotBlockAStart()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            AgentProjectCoordinator.Create("."),
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, Now.AddMinutes(-30), ("five-hour", 95)));

        Assert.IsTrue(
            coordinator.HasStartableAllowance(state, AgentProvider.Codex, Now),
            "That window may have reset since; only fresh evidence of being out refuses a start.");
        Assert.AreEqual(AgentProvider.Codex, coordinator.ChooseAgentToStart(state, Now));
    }

    [TestMethod]
    public void APartnerMayClockInWhileTheOtherAgentIsStillWorking()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.ClockIn(
            AgentProjectCoordinator.Create("."),
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 10));
        state = coordinator.SelectInitialAgent(state, Now);

        var joined = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.ClaudeCode,
            Usage(AgentProvider.ClaudeCode, 20));

        Assert.AreEqual(
            AgentProjectStatus.Working,
            joined.Status,
            "Somebody arriving does not change what the project is doing.");
        Assert.AreEqual(AgentProvider.Codex, joined.ActiveAgent);
        Assert.AreEqual(AgentTurnState.Waiting, joined.Participant(AgentProvider.ClaudeCode).TurnState);
    }

    [TestMethod]
    public void TheAgentHoldingTheTurnMayClockInAgainWithoutLosingIt()
    {
        var working = Working();
        var owner = working.ActiveAgent!.Value;
        Assert.AreEqual(AgentTurnState.Active, working.Participant(owner).TurnState);

        // Filekin starts a new session for an agent that still owns a lease from a session that has
        // gone. That session must be able to report in; what it must not do is reset its own turn.
        var again = AgentProjectCoordinator.ClockIn(working, owner, Usage(owner, 10));

        Assert.AreEqual(owner, again.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, again.Status);
        Assert.AreEqual(AgentTurnState.Active, again.Participant(owner).TurnState);
        Assert.AreEqual(AgentConnectionState.Ready, again.Participant(owner).ConnectionState);
    }

    [TestMethod]
    public void AnAgentThatIsBlockedCanStillHandOverWhatItKnows()
    {
        var state = AgentProjectCoordinator.MarkBlocked(
            AgentProjectCoordinator.RequestHandoff(
                Working(),
                AgentProvider.Codex,
                AgentHandoffReason.UserRequested),
            AgentProvider.Codex,
            "The sandbox refused every write.");
        Assert.AreEqual(AgentProjectStatus.NeedsAttention, state.Status);

        var submitted = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.UserRequested));

        Assert.IsNotNull(
            submitted.PendingHandoff,
            "Hitting a wall is exactly when what the agent learned is worth the most.");
    }

    [TestMethod]
    public void TheReasonTheTurnMovesIsFilekinsOwnFactNotTheAgentsGuess()
    {
        var state = AgentProjectCoordinator.RequestHandoff(
            Working(),
            AgentProvider.Codex,
            AgentHandoffReason.UserRequested);

        var submitted = AgentProjectCoordinator.SubmitHandoff(
            state,
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.WorkCompleted));

        Assert.AreEqual(
            AgentHandoffReason.UserRequested,
            submitted.PendingHandoff?.Reason,
            "Filekin asked for this handoff, so Filekin already knows why it is happening.");
        Assert.AreEqual(
            "Coordinator core is implemented.",
            submitted.PendingHandoff?.Summary,
            "What the agent wrote down is kept exactly as written.");
    }

    [TestMethod]
    public void LowAllowanceStopsTheWorkUntilTheUserSaysCarryOn()
    {
        var coordinator = Coordinator();
        var lowOnBoth = ClockInBoth(
            Usage(AgentProvider.Codex, 92),
            Usage(AgentProvider.ClaudeCode, 95));

        var refused = coordinator.SelectInitialAgent(lowOnBoth, Now);
        Assert.AreEqual(AgentProjectStatus.Paused, refused.Status);
        Assert.IsNull(refused.Lease, "The safety limit refuses the turn by default.");

        var allowed = coordinator.SelectInitialAgent(
            AgentProjectCoordinator.SetWorkOnLowAllowance(lowOnBoth, allowed: true),
            Now);

        Assert.AreEqual(AgentProjectStatus.Working, allowed.Status);
        Assert.AreEqual(
            AgentProvider.Codex,
            allowed.ActiveAgent,
            "Codex has 8 percent against Claude's 5, so it is still the better of the two.");
    }

    [TestMethod]
    public void CarryingOnStillNeedsTheAgentToActuallyBeHere()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.SetWorkOnLowAllowance(
            AgentProjectCoordinator.Create("."),
            allowed: true);

        Assert.Throws<InvalidOperationException>(() => coordinator.SelectInitialAgent(state, Now));
    }

    [TestMethod]
    public void CarryingOnLetsAHandoffReachAPartnerWhoIsAlmostOut()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.SetWorkOnLowAllowance(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 92)),
            allowed: true);
        state = coordinator.SelectInitialAgent(state, Now, AgentProvider.Codex);
        state = AgentProjectCoordinator.SubmitHandoff(
            AgentProjectCoordinator.RequestHandoff(state, AgentProvider.Codex, AgentHandoffReason.UserRequested),
            Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode, AgentHandoffReason.UserRequested));

        var transferred = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddMinutes(1));

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            transferred.ActiveAgent,
            "Refusing the handoff here is exactly the wall the user asked to be able to pass.");
    }

    /// <summary>Both agents clocked in and Codex holding the one turn.</summary>
    /// <summary>A finished job that still carries everything the folder itself is.</summary>
    private static AgentProjectState CompletedProject()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.GrantSharedCheckoutConsent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now,
            "Work in this folder.");
        state = AgentProjectCoordinator.SetWorkOnLowAllowance(state, allowed: true);
        state = AgentProjectCoordinator.RecordNativeSession(state, AgentProvider.Codex, "codex-session");
        state = AgentProjectCoordinator.RecordNativeSession(state, AgentProvider.ClaudeCode, "claude-session");
        state = coordinator.SelectInitialAgent(state, Now);
        state = AgentProjectCoordinator.QueueMessage(
            state,
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            "The build is green.",
            Now.AddSeconds(1));
        state = AgentProjectCoordinator.RequestHandoff(
            state,
            AgentProvider.Codex,
            AgentHandoffReason.WorkCompleted);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            new AgentHandoff(
                Guid.NewGuid(),
                AgentProvider.Codex,
                AgentProvider.ClaudeCode,
                Now.AddSeconds(2),
                AgentHandoffReason.WorkCompleted,
                "Build fixed.",
                "Fixed the build.",
                string.Empty,
                "Tests passed.",
                string.Empty));
        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddSeconds(3));
        state = AgentProjectCoordinator.AcceptHandoff(state, AgentProvider.ClaudeCode, Now.AddSeconds(4));
        state = AgentProjectCoordinator.CompleteProject(state, AgentProvider.ClaudeCode);
        Assert.AreEqual(AgentProjectStatus.Completed, state.Status);
        Assert.IsNotNull(state.LastHandoff);
        return state;
    }

    private static AgentProjectState Working() =>
        Coordinator().SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 50)),
            Now);

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
        new(new AgentCoordinationPolicy(20, 40, TimeSpan.FromMinutes(5)));
}
