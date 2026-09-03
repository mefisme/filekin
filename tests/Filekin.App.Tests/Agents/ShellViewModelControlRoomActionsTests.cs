using Filekin.App.ViewModels;
using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// The half of the control room a person presses: Start, Pass, Stop, and the one sentence beside
/// them. A label and the button under it must never disagree, because a control that reads as
/// pressable and then refuses is the fault this surface has actually had.
/// </summary>
/// <remarks>
/// A project is shown the way the app shows one — an <see cref="AgentProjectTabViewModel"/> holding
/// the state, added and selected — so nothing here needs a store, a runtime, or a provider. That is
/// also the only honest way to reach it: the shell reads its own project field, and a test that set
/// that field directly would prove nothing about the path the app takes.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ShellViewModelControlRoomActionsTests
{
    private const string Session = "33333333-3333-3333-3333-333333333333";

    // ---- What the start button says ----

    [TestMethod]
    public async Task AProjectWithNothingRunningSaysStartWork()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());

        Assert.AreEqual("Start work", shell.AgentStartActionLabel);
    }

    [TestMethod]
    public async Task AFinishedProjectAsksForTheNextObjectiveRatherThanSoundingPressable()
    {
        await using var shell = ControlRoom(AgentProjects.Done(AgentProvider.Codex));

        Assert.AreEqual("Write a new objective", shell.AgentStartActionLabel);
        Assert.IsFalse(shell.IsAgentTurnActionsVisible, "A finished job has no turn to start, pass or stop.");
    }

    [TestMethod]
    public async Task ARunningAgentThatHasReportedInSaysContinue()
    {
        var project = ConnectedAndIdle(AgentProvider.Codex);
        await using var shell = ControlRoom(project);
        OpenCliTab(shell, project, AgentProvider.Codex);

        Assert.AreEqual(
            "Continue",
            shell.AgentStartActionLabel,
            "There is a session here Filekin can hand the turn to.");
        Assert.IsTrue(shell.CanStartAgents, "And the button under that word must actually work.");
    }

    [TestMethod]
    public async Task ASavedConversationOnItsOwnIsNotSomethingToContinue()
    {
        var project = AgentProjects.WithSession(
            AgentProjects.Ready(),
            AgentProvider.Codex,
            Session);
        await using var shell = ControlRoom(project);

        Assert.AreEqual(
            "Start work",
            shell.AgentStartActionLabel,
            "Nothing is running, so there is nothing to carry on with. Start work keeps what it knew.");
    }

    // ---- The CLI tab Filekin cannot reach ----

    [TestMethod]
    public async Task ACliTabFilekinCannotReachDisablesTheStartControl()
    {
        // The agent worked here, stopped, and somebody opened its CLI again by hand. The tool is
        // running and the agent has not reported in, so no start control can do anything: Filekin
        // only dispatches a turn into a session it holds, and it does not hold this one.
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        OpenCliTab(shell, project, AgentProvider.Codex);

        Assert.IsFalse(
            shell.CanStartAgents,
            "Filekin cannot give a turn to a terminal somebody else is driving.");
        Assert.AreEqual(
            "Start work",
            shell.AgentStartActionLabel,
            "It must not offer Continue for a tool that has not reported in.");
    }

    [TestMethod]
    public async Task ACliTabFilekinCannotReachNamesTheTabAndTheWayOut()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        OpenCliTab(shell, project, AgentProvider.Codex);

        Assert.AreEqual(
            "Filekin lost track of this Codex session. Close its CLI tab and Filekin takes it back over.",
            shell.AgentsStatus,
            "The band states the fact and the one thing to do. Nothing else belongs on one line.");
        Assert.AreEqual(
            "Filekin lost track of this Codex session — usually because it carried on after Filekin "
                + "closed. Close its CLI tab and Filekin takes it back over. Nothing is lost.",
            shell.AgentStartActionHint,
            "The button has room for the cause, hedged, because it is not the only way here.");
        Assert.IsFalse(
            shell.AgentsStatus.Contains("outside Filekin", StringComparison.Ordinal),
            "Both sentences must name this one session. Somebody running Codex for five other "
                + "things must not read this as all of it being in the way.");
        Assert.IsFalse(
            shell.AgentsStatus.Contains("Start work", StringComparison.Ordinal),
            "The start button is beside this and says what it does; naming it here buries the fact.");
    }

    [TestMethod]
    public async Task AnAllowanceReadingIsWhatTellsThoseTwoStatesApart()
    {
        // A clock-in carries no allowance, so an agent that has reported in but has not been read
        // yet stays UsagePending, and the surface counts that as not reported in. The reading is
        // what turns the same tab from a blocked start into a Continue.
        var pending = ResumedByHand(AgentProvider.Codex);
        Assert.AreEqual(
            AgentConnectionState.UsagePending,
            pending.Participant(AgentProvider.Codex).ConnectionState,
            "This is the state a clock-in leaves behind before any allowance is read.");

        await using var shell = ControlRoom(pending);
        OpenCliTab(shell, pending, AgentProvider.Codex);
        Assert.IsFalse(shell.CanStartAgents, "Without a reading the start is refused.");

        var read = AgentProjects.WithUsage(pending, AgentProvider.Codex, AgentProjects.Window());
        await using var afterReading = ControlRoom(read);
        OpenCliTab(afterReading, read, AgentProvider.Codex);

        Assert.AreEqual(
            AgentConnectionState.Ready,
            read.Participant(AgentProvider.Codex).ConnectionState);
        Assert.IsTrue(afterReading.CanStartAgents, "With one, the same tab is a session to carry on.");
    }

    // ---- Whether starting is possible at all ----

    [TestMethod]
    public async Task AnApprovedProjectWithAnObjectiveAndNoTurnCanStart()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());

        Assert.IsTrue(shell.CanStartAgents);
        Assert.IsTrue(shell.IsAgentStartVisible);
    }

    [TestMethod]
    public async Task AFolderTheOwnerHasNotApprovedOffersNoStartAtAll()
    {
        await using var shell = ControlRoom(AgentProjects.NotSetUp());

        Assert.IsFalse(shell.CanStartAgents, "No agent runs in a folder nobody approved.");
        Assert.IsFalse(shell.IsAgentStartVisible, "And the control is not even shown.");
        Assert.IsTrue(shell.IsAgentConsentNeeded);
    }

    [TestMethod]
    public async Task AnAgentAlreadyHoldingTheTurnCannotBeStartedAgain()
    {
        await using var shell = ControlRoom(AgentProjects.Working(AgentProvider.Codex));

        Assert.IsFalse(shell.CanStartAgents, "The single writer lease is already taken.");
    }

    [TestMethod]
    public async Task AFinishedProjectCannotStart()
    {
        await using var shell = ControlRoom(AgentProjects.Done(AgentProvider.Codex));

        Assert.IsFalse(shell.CanStartAgents);
    }

    [TestMethod]
    public async Task AProjectWithNothingToDoCannotStart()
    {
        var noObjective = AgentProjectCoordinator.GrantSharedCheckoutConsent(
            AgentProjectCoordinator.Create(AgentProjects.Folder, ""),
            AgentProjects.Now,
            AgentProjects.Approval);
        await using var shell = ControlRoom(noObjective);

        Assert.IsFalse(
            shell.CanStartAgents,
            "An agent started with no objective can only ask what the job is.");
    }

    [TestMethod]
    public async Task ClearingTheObjectiveBoxOverASavedObjectiveStopsTheStart()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());
        Assert.IsTrue(shell.CanStartAgents, "The saved objective is enough on its own.");

        shell.AgentsObjective = string.Empty;

        Assert.IsFalse(
            shell.CanStartAgents,
            "Emptying the box is the person saying the next job is not written yet.");
    }

    [TestMethod]
    public async Task TextStillSittingInTheObjectiveBoxCountsAsSomethingToDo()
    {
        var noObjective = AgentProjectCoordinator.GrantSharedCheckoutConsent(
            AgentProjectCoordinator.Create(AgentProjects.Folder, ""),
            AgentProjects.Now,
            AgentProjects.Approval);
        await using var shell = ControlRoom(noObjective);

        shell.AgentsObjective = "Tidy the build.";

        Assert.IsTrue(shell.CanStartAgents, "Start saves the typed objective first rather than refusing.");
    }

    // ---- Passing the turn ----

    [TestMethod]
    public async Task TheTurnCanBePassedOnlyWhileSomebodyIsWorking()
    {
        await using var working = ControlRoom(AgentProjects.Working(AgentProvider.Codex));
        Assert.IsTrue(working.CanPassTheAgentTurn);

        await using var ready = ControlRoom(AgentProjects.Ready());
        Assert.IsFalse(ready.CanPassTheAgentTurn, "There is no turn to pass.");
    }

    [TestMethod]
    public async Task ATurnAlreadyBeingHandedOverIsNotPassedAgain()
    {
        await using var shell = ControlRoom(AgentProjects.HandingOver(AgentProvider.Codex));

        Assert.IsFalse(
            shell.CanPassTheAgentTurn,
            "Asking twice would leave two requests against one turn.");
    }

    [TestMethod]
    public async Task AnAgentAlreadyAskedToStopIsNotAskedToHandOverInstead()
    {
        await using var shell = ControlRoom(AgentProjects.Stopping(AgentProvider.Codex));

        Assert.IsFalse(shell.CanPassTheAgentTurn);
    }

    // ---- Stopping ----

    [TestMethod]
    public async Task StopFromAProjectTabTargetsThatProjectsOwnTurn()
    {
        await using var shell = ControlRoom(AgentProjects.Working(AgentProvider.Codex));

        Assert.IsTrue(shell.IsAgentsWorkspaceSelected, "The project tab is the selected workspace.");
        Assert.IsTrue(shell.CanStopAgents);
    }

    [TestMethod]
    public async Task StopIsRefusedOnAProjectTabWithNoTurnRunning()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());

        Assert.IsFalse(shell.CanStopAgents, "There is nothing running here to stop.");
    }

    [TestMethod]
    public async Task TheFilesStripOffersStopWhenExactlyOneProjectIsRunning()
    {
        await using var shell = ControlRoom(AgentProjects.Working(AgentProvider.Codex));
        shell.SelectFilesWorkspace();

        Assert.IsFalse(shell.IsAgentsWorkspaceSelected);
        Assert.IsTrue(shell.CanStopAgents, "One running project is unambiguous, so the strip may stop it.");
    }

    [TestMethod]
    public async Task TheFilesStripWillNotStopAnythingWhileTwoProjectsAreRunning()
    {
        await using var shell = ControlRoom(AgentProjects.Working(AgentProvider.Codex));
        var second = AgentProjectCoordinator.SetObjective(
            AgentProjects.Working(AgentProvider.ClaudeCode),
            "Another job.");
        Show(shell, second, @"C:\work\other");
        shell.SelectFilesWorkspace();

        Assert.IsFalse(
            shell.CanStopAgents,
            "With two running projects the strip cannot tell which one Stop would end.");
    }

    // ---- Clearing an attention state ----

    [TestMethod]
    public async Task AProjectAskingForAPersonOffersTheWayOutOfIt()
    {
        await using var shell = ControlRoom(AgentProjects.NeedsSomebody(AgentProvider.Codex));

        Assert.IsTrue(shell.CanClearAgentAttention);
    }

    [TestMethod]
    public async Task AnAgentStillHoldingTheTurnIsNotClearedFromUnderneath()
    {
        await using var shell = ControlRoom(AgentProjects.NeedsYou(AgentProvider.Codex));

        Assert.IsFalse(
            shell.CanClearAgentAttention,
            "A blocked agent keeps its lease, and dropping it would lose track of a running agent.");
    }

    [TestMethod]
    public async Task AHealthyProjectHasNoWarningToClear()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());

        Assert.IsFalse(shell.CanClearAgentAttention);
    }

    // ---- The sentence beside the buttons, which must use the same words ----

    [TestMethod]
    public async Task AProjectWaitingForItsFirstAgentSaysSo()
    {
        await using var shell = ControlRoom(AgentProjects.Approved());

        Assert.AreEqual("Waiting for an agent to report in.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task AnIdleProjectNamesTheButtonItIsShowing()
    {
        await using var shell = ControlRoom(AgentProjects.Ready());

        Assert.AreEqual($"Nobody is working. Press {shell.AgentStartActionLabel} to carry on.", shell.AgentsStatus);
        Assert.AreEqual("Nobody is working. Press Start work to carry on.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task AWorkingProjectNamesTheAgentThatHoldsTheTurn()
    {
        await using var shell = ControlRoom(AgentProjects.Working(AgentProvider.ClaudeCode));

        Assert.AreEqual("Claude Code is working now.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task AHandoverSaysWhenTheOtherAgentStarts()
    {
        await using var shell = ControlRoom(AgentProjects.HandingOver(AgentProvider.Codex));

        StringAssert.Contains(shell.AgentsStatus, "was asked to hand over");
        StringAssert.Contains(shell.AgentsStatus, "starts when this session ends");
    }

    [TestMethod]
    public async Task AStopInProgressSaysItIsFinishingSafely()
    {
        await using var shell = ControlRoom(AgentProjects.Stopping(AgentProvider.Codex));

        Assert.AreEqual("Codex was asked to stop, and is finishing safely.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task ACompletionReportSaysTheAgentIsStillFinishing()
    {
        await using var shell = ControlRoom(AgentProjects.Finishing(AgentProvider.Codex));

        Assert.AreEqual("Codex says the work is done, and is finishing.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task APausedProjectUsesTheSameWordTheRowsUse()
    {
        var stopped = AgentProjects.Stopped(AgentProvider.Codex);
        await using var shell = ControlRoom(stopped);

        StringAssert.StartsWith(
            shell.AgentsStatus,
            "Stopped",
            "The rows say Stopped, and one surface must not use two words for one state.");
        StringAssert.Contains(shell.AgentsStatus, "Press Start work to carry on.");
        Assert.AreEqual(
            "Stopped",
            shell.AgentsStatus.Split(' ')[0].TrimEnd('.'),
            "The recorded reason opens with that word already; announcing it twice reads as a stutter.");
        Assert.AreEqual(
            1,
            shell.AgentsStatus.Split("Stopped", StringSplitOptions.None).Length - 1,
            "The word belongs in the sentence once.");
    }

    [TestMethod]
    public async Task AProjectThatNeedsAPersonGivesTheReasonAndTheWayOut()
    {
        await using var shell = ControlRoom(
            AgentProjects.NeedsSomebody(AgentProvider.Codex, "It stopped without saying what it did."));

        Assert.AreEqual(
            "Needs you. It stopped without saying what it did. Press Clear the warning to start again.",
            shell.AgentsStatus);
    }

    [TestMethod]
    public async Task AFinishedProjectAsksForTheNextObjective()
    {
        await using var shell = ControlRoom(AgentProjects.Done(AgentProvider.Codex));

        Assert.AreEqual("Finished. Enter the next objective to start again.", shell.AgentsStatus);
    }

    [TestMethod]
    public async Task ATabWithNoProjectBehindItSaysTheFolderIsNotSetUp()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var tab = new AgentProjectTabViewModel(AgentProjects.Folder);
        shell.AgentProjectTabs.Add(tab);

        shell.SelectAgentProjectTab(tab);

        Assert.AreEqual("This folder is not set up for agents yet.", shell.AgentsStatus);
        Assert.AreEqual(0, shell.AgentParticipants.Count, "There are no agents to list yet.");
        Assert.IsFalse(shell.IsAgentStartVisible, "And nothing to start.");
    }

    // ---- Helpers ----

    /// <summary>Opens one project's control room, the way the app itself opens one.</summary>
    private static ShellViewModel ControlRoom(AgentProjectState project, string? choose = null)
    {
        var shell = new ShellViewModel(new FakeDirectoryLister());
        Show(shell, project, project.FolderPath);
        if (choose is not null)
        {
            shell.AgentChoice = choose;
        }

        return shell;
    }

    // ---- The same tab, when the person has asked Filekin to take it back ----

    [TestMethod]
    public async Task TheSettingLetsWorkStartWithThatTabStillOpen()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);

        Assert.IsTrue(
            shell.CanStartAgents,
            "The tab is still in the way; the difference is that Filekin will move it.");
    }

    [TestMethod]
    public async Task WithTheSettingOnBothSentencesSayFilekinDoesIt()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);

        Assert.AreEqual(
            "Filekin lost track of this Codex session. It takes the CLI tab back when work starts.",
            shell.AgentsStatus,
            "Telling somebody to close a tab Filekin is about to close is a wrong instruction.");
        StringAssert.Contains(
            shell.AgentStartActionHint,
            "Filekin closes that CLI tab itself",
            StringComparison.Ordinal,
            "The button still has room to say why the session was lost, and what happens next.");
    }

    [TestMethod]
    public async Task TakingTheTabBackClosesIt()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);
        var codex = shell.AgentParticipants.First(row => row.Provider == AgentProvider.Codex);

        await shell.TakeAgentCliTabBackAsync(project.Id, codex);

        Assert.AreEqual(0, shell.TerminalTabs.Count, "The tab holding the lost session is gone.");
    }

    [TestMethod]
    public async Task TakingTheTabBackLeavesEveryOtherTerminalAlone()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);

        // A shell the person opened for themselves, and the other agent's CLI on the same project.
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.AddTerminal(
            "Claude · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(project.Id, AgentProvider.ClaudeCode, Session));
        var codex = shell.AgentParticipants.First(row => row.Provider == AgentProvider.Codex);

        await shell.TakeAgentCliTabBackAsync(project.Id, codex);

        Assert.AreEqual(
            2,
            shell.TerminalTabs.Count,
            "Only the tab holding the blocked agent's session may be closed.");
        Assert.IsFalse(
            shell.TerminalTabs.Any(tab => tab.AgentSession?.Provider == AgentProvider.Codex),
            "And that one is the one that went.");
    }

    [TestMethod]
    public async Task ACliTabFilekinTookAwayComesBackOnTheLiveSession()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);
        var codex = shell.AgentParticipants.First(row => row.Provider == AgentProvider.Codex);
        await shell.TakeAgentCliTabBackAsync(project.Id, codex);

        var opened = new List<(AgentProvider Provider, string Session)>();
        await shell.GiveBackAgentCliTabsAsync(project, (provider, live) =>
        {
            opened.Add((provider, live));
            shell.AddTerminal(
                "Codex · demo",
                new FakeTerminalSession(),
                new AgentTerminalIdentity(project.Id, provider, live));
            return Task.FromResult<string?>(null);
        });

        Assert.AreEqual(1, opened.Count, "The person is owed exactly one CLI back.");
        Assert.AreEqual(AgentProvider.Codex, opened[0].Provider, "And only the agent whose tab went.");
        Assert.AreEqual(Session, opened[0].Session, "Opened on the session Filekin holds now.");
    }

    [TestMethod]
    public async Task ACliTabIsOnlyGivenBackOnce()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");
        shell.ReopenAgentCliTabsAutomatically = true;
        OpenCliTab(shell, project, AgentProvider.Codex);
        var codex = shell.AgentParticipants.First(row => row.Provider == AgentProvider.Codex);
        await shell.TakeAgentCliTabBackAsync(project.Id, codex);

        var opens = 0;
        Task<string?> Refuse(AgentProvider provider, string live)
        {
            opens++;
            return Task.FromResult<string?>("Codex will not resume that thread yet.");
        }

        await shell.GiveBackAgentCliTabsAsync(project, Refuse);
        await shell.GiveBackAgentCliTabsAsync(project, Refuse);

        Assert.AreEqual(
            1,
            opens,
            "A provider that refuses must not be asked again, or a tab reopens in a loop.");
    }

    [TestMethod]
    public async Task NobodyIsOwedACliTabTheyNeverLost()
    {
        var project = ResumedByHand(AgentProvider.Codex);
        await using var shell = ControlRoom(project, choose: "Codex");

        var opens = 0;
        await shell.GiveBackAgentCliTabsAsync(project, (_, _) =>
        {
            opens++;
            return Task.FromResult<string?>(null);
        });

        Assert.AreEqual(0, opens, "Filekin owes a CLI back only where it took one away.");
    }

    private static void Show(ShellViewModel shell, AgentProjectState project, string folderPath)
    {
        var tab = new AgentProjectTabViewModel(folderPath)
        {
            Project = project,
            ObjectiveDraft = project.Objective,
        };
        shell.AgentProjectTabs.Add(tab);
        shell.SelectAgentProjectTab(tab);
    }

    /// <summary>A CLI for this agent, open in one of this window's terminal tabs.</summary>
    private static void OpenCliTab(ShellViewModel shell, AgentProjectState project, AgentProvider provider)
    {
        shell.AddTerminal(
            $"{AgentParticipantViewModel.ShortName(provider)} · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(project.Id, provider, Session));
        shell.SelectAgentProjectTab(shell.AgentProjectTabs[0]);
    }

    /// <summary>
    /// An agent that worked here, stopped, and whose CLI somebody has opened again by hand. Filekin
    /// holds no session for it, and no allowance has been read since it clocked in.
    /// </summary>
    private static AgentProjectState ResumedByHand(AgentProvider provider) =>
        AgentProjects.WithSession(AgentProjects.Stopped(provider), provider, Session);

    /// <summary>
    /// The same agent once its allowance has been read: present, reported in, holding no turn. It is
    /// the one state where a start control may honestly say Continue.
    /// </summary>
    private static AgentProjectState ConnectedAndIdle(AgentProvider provider) =>
        AgentProjects.WithUsage(ResumedByHand(provider), provider, AgentProjects.Window());
}
