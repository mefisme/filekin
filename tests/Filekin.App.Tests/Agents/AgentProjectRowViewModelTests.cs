using Filekin.App.ViewModels;
using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// Covers the two facts every <c>/projects</c> row states — connection and work — plus the
/// objective, folder name, agents list, and usage cells built alongside them. Every project state is
/// reached through <see cref="AgentProjects"/> or the coordinator's own public transitions, never by
/// writing fields, so a passing test proves a real sequence of events produces the row it claims.
/// </summary>
[TestClass]
public sealed class AgentProjectRowViewModelTests
{
    private static readonly AgentProvider[] NoProviders = [];

    private static AgentProjectRowViewModel Row(
        AgentProjectState project,
        IReadOnlyCollection<AgentProvider>? running = null,
        IReadOnlyCollection<AgentProvider>? unknown = null) =>
        new(project, running ?? NoProviders, unknown ?? NoProviders, AgentProjects.Now);

    // ---- Connection: answered by the live sessions passed in, never by stored state ----

    [TestMethod]
    public void ARunningProviderSaysRunning()
    {
        var row = Row(AgentProjects.Approved(), running: [AgentProvider.Codex]);

        Assert.AreEqual("Running", row.Connection, "A live session must read as connected.");
        Assert.IsTrue(row.IsRunning, "The row must flag that an agent is actually running.");
    }

    [TestMethod]
    public void NobodyRunningOrUncheckedSaysNotConnected()
    {
        var row = Row(AgentProjects.Approved());

        Assert.AreEqual("Not connected", row.Connection);
        Assert.IsFalse(row.IsRunning);
        Assert.IsFalse(row.IsConnectionUnknown);
    }

    [TestMethod]
    public void AnUncheckedProviderSaysNoAnswerInsteadOfNotConnected()
    {
        var row = Row(AgentProjects.Approved(), unknown: [AgentProvider.Codex]);

        Assert.AreEqual(
            "No answer",
            row.Connection,
            "A provider Filekin could not reach is not the same as one that is simply off.");
        Assert.IsTrue(row.IsConnectionUnknown);
    }

    [TestMethod]
    public void RestartingFilekinDoesNotMakeAStoredSessionLookAlive()
    {
        // Working(...) stores the agent as clocked in and Ready, but no running provider is passed:
        // the live check, never the stored state, must decide.
        var row = Row(AgentProjects.Working(AgentProvider.Codex));

        Assert.AreEqual(
            "Not connected",
            row.Connection,
            "A restart must not let a row claim a session that already ended.");
        Assert.IsFalse(row.IsRunning);
    }

    [TestMethod]
    public void RunningBeatsUnknownWhenBothAreReported()
    {
        var row = Row(
            AgentProjects.Approved(),
            running: [AgentProvider.Codex],
            unknown: [AgentProvider.ClaudeCode]);

        Assert.AreEqual("Running", row.Connection);
    }

    // ---- Work: the same vocabulary as a row in the control room, most pressing answer first ----

    [TestMethod]
    public void AFolderWithNoSharedCheckoutConsentIsNotSetUp()
    {
        var row = Row(AgentProjects.NotSetUp());

        Assert.AreEqual("Not set up", row.Work);
    }

    [TestMethod]
    public void ABlockedAgentNeedsYouEvenWhileItIsRunning()
    {
        var row = Row(AgentProjects.NeedsYou(AgentProvider.Codex), running: [AgentProvider.Codex]);

        Assert.AreEqual(
            "Needs you",
            row.Work,
            "A blocked agent that needs the user must not read as merely working.");
    }

    [TestMethod]
    public void AnActiveTurnIsWorking()
    {
        var row = Row(AgentProjects.Working(AgentProvider.Codex));

        Assert.AreEqual("Working", row.Work);
    }

    [TestMethod]
    public void AWrittenHandoffIsHandingOver()
    {
        var row = Row(AgentProjects.HandingOver(AgentProvider.Codex));

        Assert.AreEqual("Handing over", row.Work);
    }

    [TestMethod]
    public void AStopTheUserAskedForIsStopping()
    {
        var row = Row(AgentProjects.Stopping(AgentProvider.Codex));

        Assert.AreEqual("Stopping", row.Work);
    }

    [TestMethod]
    public void ACompletionReportNotYetConfirmedIsFinishing()
    {
        var row = Row(AgentProjects.Finishing(AgentProvider.Codex));

        Assert.AreEqual("Finishing", row.Work);
    }

    [TestMethod]
    public void AConfirmedCompletionIsDone()
    {
        var row = Row(AgentProjects.Done(AgentProvider.Codex));

        Assert.AreEqual("Done", row.Work);
    }

    [TestMethod]
    public void NothingPressingButAProviderRunningIsWaiting()
    {
        var row = Row(AgentProjects.Approved(), running: [AgentProvider.Codex]);

        Assert.AreEqual("Waiting", row.Work);
    }

    [TestMethod]
    public void NobodyRunningAndNobodyHasWorkedIsNotStarted()
    {
        var row = Row(AgentProjects.Approved());

        Assert.AreEqual("Not started", row.Work);
    }

    [TestMethod]
    public void NobodyRunningButSomeoneHasWorkedIsStopped()
    {
        var row = Row(AgentProjects.Stopped(AgentProvider.Codex));

        Assert.AreEqual("Stopped", row.Work);
    }

    [TestMethod]
    public void NeedsYouWinsEvenWhenThePartnerIsWaiting()
    {
        var blocked = AgentProjectCoordinator.MarkBlocked(
            AgentProjects.BothPresent(AgentProvider.Codex),
            AgentProvider.Codex,
            "It cannot read the folder.");
        var row = Row(blocked, running: [AgentProvider.ClaudeCode]);

        Assert.AreEqual(
            "Needs you",
            row.Work,
            "The priority order must hold even though the other agent is only waiting, not blocked.");
    }

    // ---- Objective, FolderName and AutomationName ----

    [TestMethod]
    public void AnEmptyObjectiveSaysSoInsteadOfShowingNothing()
    {
        var row = Row(AgentProjectCoordinator.Create(AgentProjects.Folder, ""));

        Assert.AreEqual("No objective yet", row.Objective);
    }

    [TestMethod]
    public void AWrittenObjectiveIsShownAsWritten()
    {
        var row = Row(AgentProjectCoordinator.Create(AgentProjects.Folder, "Fix the build."));

        Assert.AreEqual("Fix the build.", row.Objective);
    }

    [TestMethod]
    public void FolderNameIsTheFoldersOwnName()
    {
        var row = Row(AgentProjects.Approved());

        Assert.AreEqual("demo", row.FolderName);
    }

    [TestMethod]
    public void ADriveRootFallsBackToTheWholePath()
    {
        var row = Row(AgentProjectCoordinator.Create(@"C:\", "Tidy up."));

        Assert.AreEqual(
            @"C:\",
            row.FolderName,
            "A drive root has no name of its own, so the whole path is shown instead of an empty cell.");
    }

    [TestMethod]
    public void AutomationNameReadsTheRowAloudInOrder()
    {
        var row = Row(AgentProjects.Working(AgentProvider.Codex), running: [AgentProvider.Codex]);

        Assert.AreEqual(
            $"{row.FolderName}, {row.Connection}, {row.Work}, {row.Agents}",
            row.AutomationName,
            "The automation name must state the folder, connection, work and agents, in that order.");
        Assert.AreEqual("demo, Running, Working, Codex ●", row.AutomationName);
    }

    // ---- Agents text ----

    [TestMethod]
    public void AFolderNobodyHasStartedSaysSoInsteadOfShowingAnEmptyCell()
    {
        var row = Row(AgentProjects.Approved());

        Assert.AreEqual("None yet", row.Agents);
    }

    [TestMethod]
    public void ARunningAgentIsMarkedAndAQuietOneIsNot()
    {
        var withClaudeSaved = AgentProjects.WithSession(
            AgentProjects.Working(AgentProvider.Codex),
            AgentProvider.ClaudeCode,
            "a-saved-conversation");
        var row = Row(withClaudeSaved, running: [AgentProvider.Codex]);

        Assert.AreEqual("Codex ●  ·  Claude Code", row.Agents);
    }

    [TestMethod]
    public void AgentNamesAreOrderedByProviderAndJoined()
    {
        var row = Row(
            AgentProjects.Approved(),
            running: [AgentProvider.Codex, AgentProvider.ClaudeCode]);

        Assert.AreEqual("Codex ●  ·  Claude Code ●", row.Agents);
    }

    // ---- Usage ----

    [TestMethod]
    public void NoReadingsSaysUnknown()
    {
        var row = Row(AgentProjects.Approved());

        Assert.AreEqual("unknown", row.Usage);
    }

    [TestMethod]
    public void AReadingShowsTheShortNameAndTheTightestWindow()
    {
        var withUsage = AgentProjects.WithUsage(
            AgentProjects.Approved(),
            AgentProvider.Codex,
            new AgentUsageWindow("primary", UsedPercent: 60, WindowDuration: null, ResetsAt: null));
        var row = Row(withUsage);

        Assert.AreEqual("Codex 40%", row.Usage);
    }

    [TestMethod]
    public void TheTightestOfSeveralWindowsIsTheOneShown()
    {
        // One cell has room for one number, and the number worth reading is the one that stops work.
        var withUsage = AgentProjects.WithUsage(
            AgentProjects.Approved(),
            AgentProvider.Codex,
            new AgentUsageWindow("primary", UsedPercent: 20, WindowDuration: null, ResetsAt: null),
            new AgentUsageWindow("secondary", UsedPercent: 93, WindowDuration: null, ResetsAt: null));
        var row = Row(withUsage);

        Assert.AreEqual("Codex 7%", row.Usage, "The window with least left is the one that decides.");
    }

    [TestMethod]
    public void AWindowPastItsResetTimeCountsAsFullAtNow()
    {
        var withUsage = AgentProjects.WithUsage(
            AgentProjects.Approved(),
            AgentProvider.Codex,
            new AgentUsageWindow(
                "primary",
                UsedPercent: 90,
                WindowDuration: null,
                ResetsAt: AgentProjects.Now - TimeSpan.FromHours(1)));
        var row = Row(withUsage);

        Assert.AreEqual(
            "Codex 100%",
            row.Usage,
            "A window whose reset time has passed is full again, not still spent.");
    }

    [TestMethod]
    public void TwoAgentsWithReadingsAreJoinedInProviderOrder()
    {
        var bothReporting = AgentProjects.WithUsage(
            AgentProjects.WithUsage(
                AgentProjects.Approved(),
                AgentProvider.Codex,
                new AgentUsageWindow("primary", UsedPercent: 60, WindowDuration: null, ResetsAt: null)),
            AgentProvider.ClaudeCode,
            new AgentUsageWindow("five_hour", UsedPercent: 45, WindowDuration: null, ResetsAt: null));
        var row = Row(bothReporting);

        Assert.AreEqual("Codex 40%  ·  Claude 55%", row.Usage);
    }
}
