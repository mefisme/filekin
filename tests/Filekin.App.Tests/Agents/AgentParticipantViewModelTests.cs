using Filekin.App.ViewModels;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.Tests.Agents;

[TestClass]
public sealed class AgentParticipantViewModelTests
{
    private static AgentParticipant Participant(
        AgentProvider provider = AgentProvider.ClaudeCode,
        string? nativeSessionId = null,
        AgentConnectionState connectionState = AgentConnectionState.Offline,
        AgentTurnState turnState = AgentTurnState.ClockedOut,
        bool hasWorkedOnObjective = false) =>
        new(provider, nativeSessionId, connectionState, turnState, Usage: null, HasWorkedOnObjective: hasWorkedOnObjective);

    private static AgentParticipantViewModel ViewModel(
        AgentParticipant participant,
        bool holdsTheTurn = false,
        bool jobIsFinished = false) =>
        new(participant, holdsTheTurn, jobIsFinished);

    // ---- SessionActionLabel ----------------------------------------------------------------

    [TestMethod]
    public void ACliTabAlreadyOpenWinsTheLabelWhateverElseIsTrue()
    {
        // Even a finished job, with nothing else running, still just goes to the tab it already has.
        var vm = ViewModel(
            Participant(nativeSessionId: "some-id"),
            jobIsFinished: true);
        vm.IsCliTabOpenHere = true;

        Assert.AreEqual("Go to CLI tab", vm.SessionActionLabel);
    }

    [TestMethod]
    public void ClaudeRunningUnderFilekinWithASavedSessionOffersOpenNotResume()
    {
        var vm = ViewModel(Participant(AgentProvider.ClaudeCode, nativeSessionId: "claude-session"));
        vm.IsSessionOpenHere = true;

        Assert.AreEqual(
            "Open CLI",
            vm.SessionActionLabel,
            "It is already running, so there is nothing to resume.");
    }

    [TestMethod]
    public void CodexStoppedButHasRunInThisWindowOffersResume()
    {
        var vm = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        vm.HasRunInThisWindow = true;

        Assert.AreEqual("Resume CLI", vm.SessionActionLabel);
    }

    [TestMethod]
    public void CodexStoppedInAFreshWindowOffersOpenRatherThanAPromiseItCannotKeep()
    {
        var vm = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        vm.HasRunInThisWindow = false;

        Assert.AreEqual(
            "Open CLI",
            vm.SessionActionLabel,
            "A freshly opened window has nothing saved here to resume.");
    }

    [TestMethod]
    public void NoSavedSessionAndNothingRunningOffersOpenButItIsDisabled()
    {
        var vm = ViewModel(Participant(nativeSessionId: null));

        Assert.AreEqual("Open CLI", vm.SessionActionLabel);
        Assert.IsFalse(vm.CanOpenSession, "There is nothing saved and nothing running to open.");
    }

    // ---- CanOpenSession ---------------------------------------------------------------------

    [TestMethod]
    public void ClaudeCanBeOpenedOnlyWhileItIsRunning()
    {
        var vm = ViewModel(Participant(AgentProvider.ClaudeCode, nativeSessionId: "claude-session"));

        Assert.IsFalse(vm.CanOpenSession, "Attach opens a running background session; there is none.");

        vm.IsSessionOpenHere = true;

        Assert.IsTrue(vm.CanOpenSession, "Attach is another window on the session Filekin is already running.");
    }

    [TestMethod]
    public void CodexCanBeOpenedOnlyWhenNotRunningAndItHasRunInThisWindow()
    {
        var stoppedFresh = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        Assert.IsFalse(stoppedFresh.CanOpenSession, "A fresh window has nothing here to resume.");

        var stoppedHaving = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        stoppedHaving.HasRunInThisWindow = true;
        Assert.IsTrue(stoppedHaving.CanOpenSession, "It stopped, but this window ran it before.");

        var stillRunning = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        stillRunning.HasRunInThisWindow = true;
        stillRunning.IsSessionOpenHere = true;
        Assert.IsFalse(
            stillRunning.CanOpenSession,
            "Filekin's App Server already holds this thread; there is no second client for it.");
    }

    [TestMethod]
    public void AFinishedJobRefusesToOpenEvenWithASavedSessionButAnOpenTabStillGoesToIt()
    {
        var vm = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"), jobIsFinished: true);
        vm.HasRunInThisWindow = true;

        Assert.IsFalse(vm.CanOpenSession, "The job is over; a new objective is what starts work again.");

        vm.IsCliTabOpenHere = true;

        Assert.IsTrue(vm.CanOpenSession, "A tab that is already open is still somewhere to go.");
    }

    [TestMethod]
    public void HelpTextForNoSessionIdSaysTheAgentHasNotWorkedHereYet()
    {
        var vm = ViewModel(Participant(nativeSessionId: null));

        StringAssert.Contains(vm.SessionActionHelpText, "has not worked in this folder yet");
    }

    [TestMethod]
    public void HelpTextForAFinishedJobSaysANewObjectiveIsNeeded()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"), jobIsFinished: true);

        StringAssert.Contains(vm.SessionActionHelpText, "new objective");
    }

    [TestMethod]
    public void HelpTextForARunningCodexExplainsOneClientAtATime()
    {
        var vm = ViewModel(Participant(AgentProvider.Codex, nativeSessionId: "codex-thread"));
        vm.IsSessionOpenHere = true;

        StringAssert.Contains(vm.SessionActionHelpText, "One Codex at a time");
    }

    // ---- CanEndSession ----------------------------------------------------------------------

    [TestMethod]
    public void CanEndSessionIsFalseWhenNothingIsRunning()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));

        Assert.IsFalse(vm.CanEndSession, "A saved conversation is memory, not a session to end.");
    }

    [TestMethod]
    public void CanEndSessionIsTrueWhenFilekinHoldsTheSessionHere()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.IsSessionOpenHere = true;

        Assert.IsTrue(vm.CanEndSession);
    }

    [TestMethod]
    public void CanEndSessionIsTrueWhenACliTabIsOpenHere()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.IsCliTabOpenHere = true;

        Assert.IsTrue(vm.CanEndSession);
    }

    [TestMethod]
    public void CanEndSessionIsTrueWhenTheToolAnswersThatItIsRunningUnwatched()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.Running;

        Assert.IsTrue(vm.CanEndSession);
    }

    // ---- IsCliTabOpenButNotReportedIn ---------------------------------------------------------

    [TestMethod]
    public void ACliTabNobodyHasClockedInFromIsNotSomethingFilekinCanGiveATurn()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Offline));
        vm.IsCliTabOpenHere = true;

        Assert.IsTrue(
            vm.IsCliTabOpenButNotReportedIn,
            "The process runs, but nobody there has clocked the agent in.");
    }

    [TestMethod]
    public void ClockingInClearsTheUnreportedTabWarning()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Ready));
        vm.IsCliTabOpenHere = true;

        Assert.IsFalse(vm.IsCliTabOpenButNotReportedIn, "It has clocked in, so the tab is accounted for.");
    }

    [TestMethod]
    public void FilekinHoldingTheSessionItselfIsNeverReadAsAnUnwatchedTab()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Offline));
        vm.IsSessionOpenHere = true;
        vm.IsCliTabOpenHere = true;

        Assert.IsFalse(vm.IsCliTabOpenButNotReportedIn, "Filekin is already driving this session.");
    }

    [TestMethod]
    public void NoOpenTabMeansNothingToWarnAbout()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Offline));

        Assert.IsFalse(vm.IsCliTabOpenButNotReportedIn);
    }

    // ---- MightBeRunningUnwatched --------------------------------------------------------------

    [TestMethod]
    public void MightBeRunningUnwatchedIsFalseWhenTheSessionIsAlreadyOpenHere()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.IsSessionOpenHere = true;

        Assert.IsFalse(vm.MightBeRunningUnwatched, "Filekin can already see this session; there is nothing to ask.");
    }

    [TestMethod]
    public void MightBeRunningUnwatchedIsFalseWhenACliTabIsAlreadyOpenHere()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.IsCliTabOpenHere = true;

        Assert.IsFalse(vm.MightBeRunningUnwatched);
    }

    [TestMethod]
    public void MightBeRunningUnwatchedIsFalseWithNoSavedSessionId()
    {
        var vm = ViewModel(Participant(nativeSessionId: null));

        Assert.IsFalse(vm.MightBeRunningUnwatched, "There is nothing saved to go and find.");
    }

    [TestMethod]
    public void MightBeRunningUnwatchedIsTrueForASavedSessionThisWindowIsNotHolding()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));

        Assert.IsTrue(vm.MightBeRunningUnwatched);
    }

    // ---- Connection ---------------------------------------------------------------------------

    [TestMethod]
    public void ConnectionReadsRunningWhenTheSessionIsOpenHere()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Ready));
        vm.IsSessionOpenHere = true;

        Assert.AreEqual("Running", vm.Connection);
    }

    [TestMethod]
    public void ConnectionReadsNotConnectedWhenNothingIsRunning()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.NotRunning;

        Assert.AreEqual("Not connected", vm.Connection);
    }

    [TestMethod]
    public void ConnectionReadsNoAnswerForASavedButUncheckedSession()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.Unknown;

        Assert.AreEqual("No answer", vm.Connection);
        StringAssert.Contains(vm.ConnectionHelpText, "Couldn't check");
    }

    [TestMethod]
    public void ConnectionReadsNoAnswerWhenTheToolReportsUnavailableWhileRunning()
    {
        var vm = ViewModel(Participant(connectionState: AgentConnectionState.Unavailable));
        vm.IsSessionOpenHere = true;

        Assert.AreEqual(
            "No answer",
            vm.Connection,
            "The process is running but the tool itself is not answering.");
    }

    // ---- Work -----------------------------------------------------------------------------------

    [TestMethod]
    public void WorkReadsNotStartedForAnAgentThatNeverTookATurn()
    {
        var vm = ViewModel(Participant(turnState: AgentTurnState.ClockedOut, hasWorkedOnObjective: false));

        Assert.AreEqual("Not started", vm.Work);
    }

    [TestMethod]
    public void WorkReadsStoppedForAnAgentThatDidTakeATurnAndIsNotRunning()
    {
        var vm = ViewModel(Participant(turnState: AgentTurnState.ClockedOut, hasWorkedOnObjective: true));

        Assert.AreEqual("Stopped", vm.Work);
    }

    [TestMethod]
    public void WorkReadsWaitingWhileRunningWithNoTurn()
    {
        var vm = ViewModel(Participant(turnState: AgentTurnState.Waiting));
        vm.IsSessionOpenHere = true;

        Assert.AreEqual("Waiting", vm.Work);
    }

    [TestMethod]
    [DataRow(AgentTurnState.Active, "Working")]
    [DataRow(AgentTurnState.HandoffRequested, "Handing over")]
    [DataRow(AgentTurnState.Blocked, "Needs you")]
    [DataRow(AgentTurnState.NeedsAttention, "Needs you")]
    [DataRow(AgentTurnState.CompletionReported, "Finishing")]
    [DataRow(AgentTurnState.StopRequested, "Stopping")]
    [DataRow(AgentTurnState.Completed, "Done")]
    public void WorkMapsEachTurnStateToItsWord(AgentTurnState turnState, string expected)
    {
        var vm = ViewModel(Participant(turnState: turnState));

        Assert.AreEqual(expected, vm.Work, $"{turnState} must read as \"{expected}\".");
    }

    // ---- Refreshing rather than lying --------------------------------------------------------

    [TestMethod]
    public void UpdateWithNoSavedSessionIdClearsAStaleUnwatchedRunningAnswer()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.Running;
        Assert.AreEqual("Running", vm.Connection, "Sanity check on the stale answer before the update.");

        vm.Update(Participant(nativeSessionId: null), holdsTheTurn: false, jobIsFinished: false);

        Assert.AreEqual(
            "Not connected",
            vm.Connection,
            "There is nothing saved any more, so there is nothing left to have found running.");
    }

    [TestMethod]
    public void OpeningTheSessionHereClearsAStaleUnwatchedAnswer()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.Unknown;
        Assert.AreEqual("No answer", vm.Connection, "Sanity check on the stale answer before it opens.");

        vm.IsSessionOpenHere = true;
        vm.IsSessionOpenHere = false;

        Assert.AreEqual(
            "Not connected",
            vm.Connection,
            "Opening the session here must have thrown away the old unwatched guess, not kept it.");
    }

    [TestMethod]
    public void OpeningACliTabHereClearsAStaleUnwatchedAnswer()
    {
        var vm = ViewModel(Participant(nativeSessionId: "some-id"));
        vm.UnwatchedLiveness = AgentSessionLiveness.Running;
        Assert.AreEqual("Running", vm.Connection, "Sanity check on the stale answer before the tab opens.");

        vm.IsCliTabOpenHere = true;
        vm.IsCliTabOpenHere = false;

        Assert.AreEqual(
            "Not connected",
            vm.Connection,
            "Opening a CLI tab here must have thrown away the old unwatched guess, not kept it.");
    }
}
