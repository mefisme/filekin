using Filekin.Core.Agents;

namespace Filekin.Core.Tests.Agents;

[TestClass]
public sealed class AgentProjectCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ClockingInDoesNotEraseTheAllowanceFilekinAlreadyRead()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 7));

        // An agent cannot read its own quota, so it always clocks in carrying nothing. Writing that
        // nothing over the reading left the allowance visible only while the agent happened to be
        // working, which is exactly what an owner deciding whether to start cannot use.
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, usage: null);

        var participant = state.Participant(AgentProvider.Codex);
        Assert.IsTrue(participant.Usage is { IsKnown: true }, "The reading Filekin took is still true.");
        Assert.AreEqual(93, participant.Usage!.Windows[0].RemainingPercent);
        Assert.AreEqual(AgentConnectionState.Ready, participant.ConnectionState);
    }

    [TestMethod]
    public void ClockingInWithAFresherReadingReplacesTheOlderOne()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        state = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 7));

        state = AgentProjectCoordinator.ClockIn(
            state,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 40));

        Assert.AreEqual(60, state.Participant(AgentProvider.Codex).Usage!.Windows[0].RemainingPercent);
    }

    [TestMethod]
    public void ClockingInWithNothingKnownAtAllStillWaitsForAReading()
    {
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");

        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, usage: null);

        var participant = state.Participant(AgentProvider.Codex);
        Assert.IsNull(participant.Usage);
        Assert.AreEqual(AgentConnectionState.UsagePending, participant.ConnectionState);
    }

    [TestMethod]
    public void AHandoffWrittenAfterTheTurnMovedOnIsKeptRatherThanRefused()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var coordinator = new AgentProjectCoordinator(
            new AgentCoordinationPolicy(5, 25, TimeSpan.FromMinutes(5)));
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 10));
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, Usage(AgentProvider.ClaudeCode, 10));
        state = coordinator.SelectInitialAgent(state, now, AgentProvider.Codex);
        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, now);

        // The provider reported the turn complete on one channel while the agent's own handoff was
        // still travelling on another. Refusing it lost the account of the work and stalled a relay.
        Assert.IsNull(state.Lease, "The turn has already moved on.");

        var late = new AgentHandoff(
            Guid.NewGuid(),
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            now,
            AgentHandoffReason.WorkCompleted,
            "Wrote entry 05.",
            "Appended entry 05.",
            "Entries 06 to 10 remain.",
            "Read the file back.",
            string.Empty);

        var kept = AgentProjectCoordinator.SubmitHandoff(state, late);

        Assert.AreEqual("Wrote entry 05.", kept.LastHandoff?.Summary, "The written handoff is history now.");
        Assert.IsNull(kept.Lease, "Keeping it must not move the turn a second time.");
        Assert.AreEqual(state.Status, kept.Status);
    }

    [TestMethod]
    public void SubmittingTheSameHandoffTwiceInOneTurnIsNotAnError()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var coordinator = new AgentProjectCoordinator(
            new AgentCoordinationPolicy(5, 25, TimeSpan.FromMinutes(5)));
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 10));
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.ClaudeCode, Usage(AgentProvider.ClaudeCode, 10));
        state = coordinator.SelectInitialAgent(state, now, AgentProvider.Codex);

        var handoff = new AgentHandoff(
            Guid.NewGuid(),
            AgentProvider.Codex,
            AgentProvider.ClaudeCode,
            now,
            AgentHandoffReason.WorkCompleted,
            "Wrote entry 05.",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        var once = AgentProjectCoordinator.SubmitHandoff(state, handoff);
        var twice = AgentProjectCoordinator.SubmitHandoff(once, handoff);

        Assert.AreEqual(once.PendingHandoff?.Summary, twice.PendingHandoff?.Summary);
        Assert.AreEqual(once.Status, twice.Status, "A retry must not be an error the agent works around.");
    }

    [TestMethod]
    public void AnAgentThatIsNotInThisProjectStillCannotHandWorkOver()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        var handoff = new AgentHandoff(
            Guid.NewGuid(),
            AgentProvider.Codex,
            AgentProvider.Codex,
            now,
            AgentHandoffReason.WorkCompleted,
            "Nonsense.",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AgentProjectCoordinator.SubmitHandoff(state, handoff));
    }

    [TestMethod]
    public void AWindowPastItsResetTimeIsFullAgain()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var window = new AgentUsageWindow("codex:primary", 100, TimeSpan.FromHours(5), now.AddMinutes(-1));

        Assert.IsTrue(window.HasResetBy(now));
        Assert.AreEqual(100, window.RemainingPercentAt(now));
        Assert.AreEqual(0, window.RemainingPercent, "The reading itself is left as it was reported.");
    }

    [TestMethod]
    public void AWindowBeforeItsResetTimeStillCountsWhatWasUsed()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var window = new AgentUsageWindow("codex:primary", 100, TimeSpan.FromHours(5), now.AddMinutes(1));

        Assert.IsFalse(window.HasResetBy(now));
        Assert.AreEqual(0, window.RemainingPercentAt(now));
    }

    [TestMethod]
    public void AnOldReadingWhoseWindowsHaveAllResetStillSaysHowMuchIsLeft()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var snapshot = new AgentUsageSnapshot(
            AgentProvider.Codex,
            now.AddHours(-6),
            [
                new AgentUsageWindow("codex:primary", 100, TimeSpan.FromHours(5), now.AddHours(-1)),
                new AgentUsageWindow("codex:secondary", 40, TimeSpan.FromDays(7), now.AddHours(-2)),
            ]);

        Assert.IsFalse(snapshot.IsFresh(now, TimeSpan.FromMinutes(5)));
        Assert.IsTrue(
            snapshot.IsUsable(now, TimeSpan.FromMinutes(5)),
            "Every window is past its own reset time, so what is left is known without asking again.");
        Assert.AreEqual(100, snapshot.MinimumRemainingPercentAt(now));
    }

    [TestMethod]
    public void AnOldReadingWithOneWindowStillRunningAnswersNothing()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var snapshot = new AgentUsageSnapshot(
            AgentProvider.Codex,
            now.AddHours(-6),
            [
                new AgentUsageWindow("codex:primary", 100, TimeSpan.FromHours(5), now.AddHours(-1)),
                new AgentUsageWindow("codex:secondary", 40, TimeSpan.FromDays(7), now.AddDays(3)),
            ]);

        Assert.IsFalse(
            snapshot.IsUsable(now, TimeSpan.FromMinutes(5)),
            "Work Filekin never saw may have spent the window that has not reset.");
    }

    [TestMethod]
    public void AnAgentWhoseWindowHasResetCanStartAgainWithoutAFreshReading()
    {
        var now = new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero);
        var coordinator = new AgentProjectCoordinator(
            new AgentCoordinationPolicy(5, 25, TimeSpan.FromMinutes(5)));
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Work.");
        var spent = new AgentUsageSnapshot(
            AgentProvider.Codex,
            now.AddHours(-6),
            [new AgentUsageWindow("codex:primary", 100, TimeSpan.FromHours(5), now.AddHours(-1))]);

        var beforeReset = AgentProjectCoordinator.RecordAllowanceBeforeStart(
            state,
            AgentProvider.Codex,
            spent);

        // A minute after the reading, it is still fresh and the window has hours left to run.
        Assert.IsFalse(
            coordinator.HasStartableAllowance(beforeReset, AgentProvider.Codex, now.AddHours(-6).AddMinutes(1)),
            "While the window was still running, being out meant being out.");
        Assert.IsTrue(
            coordinator.HasStartableAllowance(beforeReset, AgentProvider.Codex, now),
            "The window it ran out of has since reset, and the provider said when.");
    }

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
    public void InitialLaunchOwnsAReservedLeaseBeforeClockInMakesItWorking()
    {
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(".");

        var reserved = coordinator.ReserveInitialAgent(state, AgentProvider.Codex, Now);

        Assert.AreEqual(AgentProjectStatus.ClockingIn, reserved.Status);
        Assert.AreEqual(AgentProvider.Codex, reserved.Lease?.Owner);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            reserved.Participant(AgentProvider.Codex).ConnectionState,
            "A reservation is ownership, not fake evidence that the process connected.");
        Assert.IsFalse(reserved.Participant(AgentProvider.Codex).HasWorkedOnObjective);

        var working = AgentProjectCoordinator.ClockIn(
            reserved,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 10));

        Assert.AreEqual(AgentProjectStatus.Working, working.Status);
        Assert.AreEqual(AgentProvider.Codex, working.ActiveAgent);
        Assert.AreEqual(AgentTurnState.Active, working.Participant(AgentProvider.Codex).TurnState);
        Assert.IsTrue(working.Participant(AgentProvider.Codex).HasWorkedOnObjective);
    }

    [TestMethod]
    public void AnInitialReservationCanBeAbandonedOnlyBeforeClockIn()
    {
        var coordinator = Coordinator();
        var reserved = coordinator.ReserveInitialAgent(
            AgentProjectCoordinator.Create("."),
            AgentProvider.Codex,
            Now);

        var abandoned = AgentProjectCoordinator.AbandonInitialReservation(
            reserved,
            AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.Ready, abandoned.Status);
        Assert.IsNull(abandoned.Lease);

        var working = AgentProjectCoordinator.ClockIn(
            reserved,
            AgentProvider.Codex,
            Usage(AgentProvider.Codex, 10));
        var refused = AgentProjectCoordinator.AbandonInitialReservation(
            working,
            AgentProvider.Codex);

        Assert.AreSame(working, refused);
        Assert.AreEqual(AgentProvider.Codex, refused.Lease?.Owner);
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

    /// <summary>
    /// A recipient that never reported in is told exactly that, and never sent to a usage screen.
    /// </summary>
    /// <remarks>
    /// This is what a person meets after opening the recipient's own CLI: its tool is plainly running,
    /// but it has clocked in with nobody, so Filekin has no agent to give the turn to. Presence and
    /// allowance are separate facts. Reporting the wrong one sent people to a quota page over a
    /// terminal tab they had opened themselves, where the allowance was usually fine.
    /// </remarks>
    [TestMethod]
    public void AHandoffToAnAgentThatNeverReportedInSaysSoRatherThanBlamingUsage()
    {
        var now = new DateTimeOffset(2026, 9, 2, 22, 0, 0, TimeSpan.Zero);
        var coordinator = Coordinator();
        var state = AgentProjectCoordinator.Create(Path.GetFullPath("."), "Take turns.");
        state = AgentProjectCoordinator.ClockIn(state, AgentProvider.Codex, Usage(AgentProvider.Codex, 90));
        state = coordinator.SelectInitialAgent(state, now, AgentProvider.Codex);
        state = AgentProjectCoordinator.SubmitHandoff(
            state,
            new AgentHandoff(
                Guid.NewGuid(),
                AgentProvider.Codex,
                AgentProvider.ClaudeCode,
                now,
                AgentHandoffReason.WorkCompleted,
                "Wrote entry 02.",
                "Appended entry 02.",
                "Entries 03 to 10 remain.",
                "Read the file back.",
                string.Empty));

        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, now);

        Assert.AreEqual(AgentProjectStatus.Paused, state.Status);
        StringAssert.Contains(state.AttentionReason, "has not reported in");
        Assert.IsFalse(
            state.AttentionReason!.Contains("usage", StringComparison.OrdinalIgnoreCase),
            "Nothing about allowance failed here; only the recipient's absence did.");
        Assert.AreEqual(
            "Wrote entry 02.",
            state.LastHandoff?.Summary,
            "The written handoff is kept, not thrown away because nobody was there to take it.");
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
            "Claude Code reported a usage limit.");

        var claude = state.Participant(AgentProvider.ClaudeCode);
        Assert.IsNull(
            claude.NativeSessionId,
            "A callback carries an identifier Filekin cannot check, so it never becomes the identity.");
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
        state = AgentProjectCoordinator.RecordNativeSession(
            state,
            AgentProvider.ClaudeCode,
            "claude-session");

        state = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
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
            "Claude Code reported a usage limit.");

        Assert.AreEqual(
            "claude-session",
            reportedAgain.Participant(AgentProvider.ClaudeCode).NativeSessionId,
            "A callback never disturbs the session identity Filekin recorded for this agent.");
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
        var completed = CompletedProject();
        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.StartNewObjective(
                AgentProjectCoordinator.Create(".", "Tidy the build."),
                "Write the release notes."));
        Assert.Throws<InvalidOperationException>(
            () => AgentProjectCoordinator.StartNewObjective(Working(), "Write the release notes."));
        Assert.Throws<ArgumentException>(
            () => AgentProjectCoordinator.StartNewObjective(completed, string.Empty));
        Assert.Throws<ArgumentException>(
            () => AgentProjectCoordinator.StartNewObjective(completed, "   "));
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

        // Nobody is here and nobody has a turn. A saved conversation is memory rather than a live
        // session, so it stays: Start work carries it on, and only /clear throws it away.
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            var participant = reopened.Participant(provider);
            Assert.AreEqual(completed.Participant(provider).NativeSessionId, participant.NativeSessionId);
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
    public void OnlyTheAgentThatTookATurnHasWorkedOnThisObjective()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now,
            AgentProvider.Codex);

        Assert.IsTrue(state.Participant(AgentProvider.Codex).HasWorkedOnObjective);
        Assert.IsFalse(
            state.Participant(AgentProvider.ClaudeCode).HasWorkedOnObjective,
            "Being here is not working on it. Claude Code never took a turn.");

        var completed = AgentProjectCoordinator.CompleteProject(state, AgentProvider.Codex);
        Assert.IsTrue(
            completed.Participant(AgentProvider.Codex).HasWorkedOnObjective,
            "Finishing the job does not undo having worked on it.");

        var reopened = AgentProjectCoordinator.StartNewObjective(completed, "Write the release notes.");
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            Assert.IsFalse(
                reopened.Participant(provider).HasWorkedOnObjective,
                "Nobody has worked on a job that has only just been written.");
        }
    }

    [TestMethod]
    public void RewritingTheObjectiveIsANewJobNobodyHasWorkedOnYet()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now,
            AgentProvider.Codex);
        state = coordinator.CompleteActiveTurn(state, AgentProvider.Codex, Now.AddMinutes(1));

        Assert.IsNull(state.Lease, "Codex finished its turn, so nobody is working.");
        Assert.IsTrue(
            state.Participant(AgentProvider.Codex).HasWorkedOnObjective,
            "Codex really did take a turn on the objective being replaced.");

        var rewritten = AgentProjectCoordinator.SetObjective(state, "Write the release notes.");

        Assert.IsFalse(
            rewritten.Participant(AgentProvider.Codex).HasWorkedOnObjective,
            "Nobody has worked on a job that has only just been written, so the row cannot say Stopped.");
        Assert.AreEqual("Write the release notes.", rewritten.Objective);
    }

    [TestMethod]
    public void SavingTheSameObjectiveAgainForgetsNothing()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now,
            AgentProvider.Codex);

        var saved = AgentProjectCoordinator.SetObjective(state, $"  {state.Objective}  ");

        Assert.IsTrue(
            saved.Participant(AgentProvider.Codex).HasWorkedOnObjective,
            "The job did not change, so what happened on it did not either.");
    }

    [TestMethod]
    public void TheAgentHoldingTheTurnIsWorkingOnTheObjectiveThatReplacesItsOwn()
    {
        var coordinator = Coordinator();
        var state = coordinator.SelectInitialAgent(
            ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20)),
            Now,
            AgentProvider.Codex);

        var rewritten = AgentProjectCoordinator.SetObjective(state, "Write the release notes.");

        Assert.AreEqual(AgentProvider.Codex, rewritten.Lease?.Owner, "Rewriting the words moves no turn.");
        Assert.IsTrue(
            rewritten.Participant(AgentProvider.Codex).HasWorkedOnObjective,
            "The agent holding the turn is working on this text from now on.");
        Assert.IsFalse(
            rewritten.Participant(AgentProvider.ClaudeCode).HasWorkedOnObjective,
            "The agent that is not working still has not worked on it.");
    }

    [TestMethod]
    public void ClearForgetsOnlyTheExplicitlySelectedProviderConversation()
    {
        var state = AgentProjectCoordinator.RecordNativeSession(
            AgentProjectCoordinator.RecordNativeSession(
                AgentProjectCoordinator.Create("."),
                AgentProvider.Codex,
                "codex-thread"),
            AgentProvider.ClaudeCode,
            "claude-conversation");

        var cleared = AgentProjectCoordinator.ClearNativeSession(state, AgentProvider.Codex);

        Assert.IsNull(cleared.Participant(AgentProvider.Codex).NativeSessionId);
        Assert.AreEqual(
            "claude-conversation",
            cleared.Participant(AgentProvider.ClaudeCode).NativeSessionId,
            "Clearing Codex must not clear Claude Code.");
    }

    [TestMethod]
    public void AnsweringALiveRequestReturnsTheLeaseOwnerToWorking()
    {
        var coordinator = Coordinator();
        var state = ClockInBoth(Usage(AgentProvider.Codex, 10), Usage(AgentProvider.ClaudeCode, 20));
        state = AgentProjectCoordinator.SetWorkOnLowAllowance(state, allowed: true);
        state = coordinator.SelectInitialAgent(state, Now, AgentProvider.Codex);
        state = AgentProjectCoordinator.MarkBlocked(state, AgentProvider.Codex, "Approval needed.");

        var resumed = AgentProjectCoordinator.ResolveBlocked(state, AgentProvider.Codex);

        Assert.AreEqual(AgentProjectStatus.Working, resumed.Status);
        Assert.AreEqual(AgentTurnState.Active, resumed.Participant(AgentProvider.Codex).TurnState);
        Assert.AreEqual(AgentProvider.Codex, resumed.Lease!.Owner);
        Assert.IsNull(resumed.AttentionReason);
    }

    [TestMethod]
    public void AUsageLimitCallbackFailsClosedWithoutReplacingTheRecordedSession()
    {
        var state = AgentProjectCoordinator.RecordNativeSession(
            AgentProjectCoordinator.Create("."),
            AgentProvider.ClaudeCode,
            "claude-background-session");

        // The report still counts, and the identity Filekin recorded when it opened the session does
        // not move.
        var limited = AgentProjectCoordinator.ReportUsageLimit(
            state,
            AgentProvider.ClaudeCode,
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
    public void ChoosingAModelForAProviderThatIsNotOneIsRefused()
    {
        var state = AgentProjectCoordinator.Create(".");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AgentProjectCoordinator.ChooseModel(state, (AgentProvider)42, "opus"),
            "A model preference is stored against a known agent or not at all.");
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
        state = AgentProjectCoordinator.SetObjective(state, "Tidy the build.");
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
