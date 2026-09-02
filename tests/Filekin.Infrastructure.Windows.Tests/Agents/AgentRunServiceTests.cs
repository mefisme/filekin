using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentRunServiceTests
{
    private const string Approval = "Let Filekin agents work in this folder itself.";
    private string _databasePath = null!;
    private string _directory = null!;
    private string _mcpExecutablePath = null!;
    private string _projectFolder = null!;

    [TestInitialize]
    public void CreateDisposableState()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"Filekin-run-{Guid.NewGuid():N}");
        _projectFolder = Path.Combine(_directory, "project");
        Directory.CreateDirectory(_projectFolder);
        _databasePath = Path.Combine(_directory, "state.db");
        _mcpExecutablePath = Path.Combine(_directory, "Filekin.Mcp.exe");
        File.WriteAllText(_mcpExecutablePath, string.Empty);
    }

    [TestCleanup]
    public void RemoveDisposableState()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task NoAgentStartsUntilTheOwnerApprovesWorkingInTheFolderItself()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = AgentProjectCoordinator.Create(_projectFolder, "Tidy the build.");
        await store.SaveAsync(project);
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.StartAsync(project.Id));

        StringAssert.Contains(refusal.Message, "approves");
        Assert.AreEqual(0, launcher.Launches, "A refusal must not reach a provider.");
    }

    [TestMethod]
    public async Task StartingTheChosenAgentWaitsForItToReportBackBeforeGivingItTheTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var started = await service.StartAsync(project.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProvider.ClaudeCode, started.ActiveAgent);
        Assert.AreEqual(AgentProjectStatus.Working, started.Status);
        var request = launcher.LastRequest!;
        Assert.AreEqual(AgentProvider.ClaudeCode, request.Provider);
        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            launcher.StateAtLaunch?.Lease?.Owner,
            "The chosen provider must own the checkout before its work-capable prompt starts.");
        Assert.AreEqual(AgentProjectStatus.ClockingIn, launcher.StateAtLaunch?.Status);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            launcher.StateAtLaunch?.Participant(AgentProvider.ClaudeCode).ConnectionState,
            "Reserving a lease must not claim the provider has already connected.");
        Assert.AreEqual(Approval, request.Consent.ApprovalDescription);
        Assert.AreEqual(project.Id, request.McpServer.ProjectId);
        StringAssert.Contains(request.Prompt, "Tidy the build.", "The user's own objective is passed through.");
        StringAssert.Contains(request.Prompt, "filekin_clock_in");
        CollectionAssert.AreEqual(
            new[] { AgentProvider.ClaudeCode },
            service.RunningAgents(project.Id).ToArray());
    }

    [TestMethod]
    public async Task StartingReportsEachVisibleStageInTheOrderItHappens()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        var sources = new FreshUsageSourceFactory(20, 20);
        await using var runtime = Runtime(store, sources);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        var reports = new List<AgentStartProgress>();

        await service.StartAsync(
            project.Id,
            AgentProvider.ClaudeCode,
            new InlineProgress<AgentStartProgress>(reports.Add));

        CollectionAssert.AreEqual(
            new[]
            {
                new AgentStartProgress(AgentStartStage.CheckingUsage, AgentProvider.ClaudeCode),
                new AgentStartProgress(AgentStartStage.StartingAgent, AgentProvider.ClaudeCode),
                new AgentStartProgress(AgentStartStage.WaitingForConnection, AgentProvider.ClaudeCode),
                new AgentStartProgress(AgentStartStage.GivingTurn, AgentProvider.ClaudeCode),
            },
            reports);
        Assert.AreEqual(
            2,
            sources.TotalReads,
            "Start should read each provider once and must not prepare the chosen provider again after clock-in.");
    }

    [TestMethod]
    public async Task StartWorkCarriesOnASavedConversationInsteadOfLosingIt()
    {
        // Start work is one button and works out what starting means here (DECISIONS.md, 2026-09-01).
        // Ending a session — including closing the terminal it was being watched in — must not throw
        // an agent's memory away. A clean slate stays available and stays deliberate: a new objective.
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.RecordNativeSession(
                current,
                AgentProvider.Codex,
                "saved-codex-thread"));
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.AreEqual(
            "saved-codex-thread",
            launcher.LastRequest!.ResumeSessionId,
            "Start work carries on the conversation this agent already had.");
    }

    [TestMethod]
    public async Task StartWorkStartsFreshWhenThereIsNoConversationToCarryOn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.IsNull(
            launcher.LastRequest!.ResumeSessionId,
            "With nothing saved there is nothing to carry on, so a new conversation begins.");
        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual("native-Codex", persisted!.Participant(AgentProvider.Codex).NativeSessionId);
    }

    [TestMethod]
    public async Task ResumedCodexTerminalIsTheExistingWorkerAndItsCloseReconcilesTheProject()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        const string sessionId = "019c1234-5678-7abc-8def-0123456789ab";
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.RecordNativeSession(
                current,
                AgentProvider.Codex,
                sessionId));
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(current, AgentProvider.Codex, usage: null));
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await using var terminal = await service.RegisterTerminalSessionAsync(
            project.Id,
            AgentProvider.Codex,
            sessionId);

        var started = await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.AreEqual(AgentProvider.Codex, started.ActiveAgent);
        Assert.AreEqual(0, launcher.Launches, "Continue must not start a second Codex process.");
        CollectionAssert.AreEqual(
            new[] { AgentProvider.Codex },
            service.RunningAgents(project.Id).ToArray());

        await terminal.DisposeAsync();

        var closed = await store.LoadAsync(project.Id);
        Assert.IsNull(closed!.Lease, "Closing the terminal is provider-stop evidence for its CLI.");
        Assert.AreEqual(
            AgentConnectionState.Offline,
            closed.Participant(AgentProvider.Codex).ConnectionState);
        Assert.AreEqual(
            sessionId,
            closed.Participant(AgentProvider.Codex).NativeSessionId,
            "Ending the process must preserve the conversation that Continue can resume.");
        Assert.IsEmpty(service.RunningAgents(project.Id));
    }

    [TestMethod]
    public async Task TerminalRegistrationRefusesADifferentOrAlreadyOpenCodexConversation()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        const string sessionId = "019c1234-5678-7abc-8def-0123456789ab";
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.RecordNativeSession(
                current,
                AgentProvider.Codex,
                sessionId));
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var wrong = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RegisterTerminalSessionAsync(
                project.Id,
                AgentProvider.Codex,
                "019c9999-5678-7abc-8def-0123456789ab"));
        StringAssert.Contains(wrong.Message, "not the session saved");

        await using var terminal = await service.RegisterTerminalSessionAsync(
            project.Id,
            AgentProvider.Codex,
            sessionId);
        var duplicate = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RegisterTerminalSessionAsync(
                project.Id,
                AgentProvider.Codex,
                sessionId));

        StringAssert.Contains(duplicate.Message, "already has a live session");
        Assert.AreEqual(0, launcher.Launches);
    }

    [TestMethod]
    public async Task StartWorkContinuesAWaitingLiveSessionWithoutLaunchingAnotherOne()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);
        var existingHandle = launcher.LastHandle;

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.RequestStop(current, AgentProvider.Codex));
        var coordinator = new AgentProjectCoordinator(
            new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));
        await store.UpdateAsync(
            project.Id,
            current => coordinator.CompleteActiveTurn(
                current,
                AgentProvider.Codex,
                DateTimeOffset.UtcNow));

        var continued = await service.StartAsync(project.Id);

        Assert.AreEqual(1, launcher.Launches, "A waiting live session must not be relaunched.");
        Assert.AreEqual(AgentProvider.Codex, continued.ActiveAgent);
        Assert.AreSame(existingHandle, launcher.LastHandle);
    }

    [TestMethod]
    public async Task PromptSteersTheExactActiveProviderSession()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var prompted = await service.SendPromptAsync(project.Id, AgentProvider.Codex, "Check the failing test.");

        Assert.AreEqual(AgentProvider.Codex, prompted.ActiveAgent);
        Assert.AreEqual("Check the failing test.", launcher.LastHandle!.LastPrompt);
        Assert.IsTrue(service.Session(project.Id, AgentProvider.Codex)!.Events.Snapshot()
            .Any(item => item.Title == "You" && item.Summary == "Check the failing test."));
    }

    [TestMethod]
    public async Task SessionObservationUsesTheExactNativeSessionAndSurvivesItsStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var live = service.Session(project.Id, AgentProvider.Codex);
        Assert.IsNotNull(live);
        Assert.AreEqual("native-Codex", live.NativeSessionId);
        Assert.AreNotSame(
            launcher.LastHandle!.Events,
            live.Events,
            "The observation owns a stable conversation feed instead of one turn handle's feed.");
        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(
            "native-Codex",
            persisted!.Participant(AgentProvider.Codex).NativeSessionId,
            "The recorded identity is the session Filekin opened, not anything the agent reported.");

        await service.RequestStopAsync(project.Id);
        launcher.LastHandle.ReportStopped();
        _ = await WaitForAsync(store, project.Id, state => state.Status == AgentProjectStatus.Paused);

        Assert.AreSame(live, service.Session(project.Id, AgentProvider.Codex));
    }

    [TestMethod]
    public async Task WithoutAChoiceFilekinStartsTheAgentWithMoreAllowanceLeft()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store, codexUsedPercent: 70, claudeUsedPercent: 25);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var started = await service.StartAsync(project.Id);

        Assert.AreEqual(
            AgentProvider.ClaudeCode,
            started.ActiveAgent,
            "Claude Code has 75 percent left against Codex's 30.");
    }

    [TestMethod]
    public async Task AnAgentThatNeverReportsBackIsAskedToStopAndKeepsNoTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => service.StartAsync(project.Id, AgentProvider.Codex));

        Assert.IsTrue(launcher.LastHandle!.StopRequested, "A session Filekin gave up on is asked to stop.");
        var reloaded = await store.LoadAsync(project.Id);
        Assert.IsNull(reloaded!.Lease, "Nobody may hold the turn after a failed start.");
        Assert.IsEmpty(service.RunningAgents(project.Id));
    }

    [TestMethod]
    public async Task StoppingIsRequestedCooperativelyAndTheTurnIsReleasedOnlyWhenTheSessionEnds()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var stopping = await service.RequestStopAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.StopPending, stopping.Status);
        Assert.AreEqual(AgentProvider.Codex, stopping.ActiveAgent, "Asking is not proof the session ended.");
        Assert.IsTrue(launcher.LastHandle!.StopRequested);

        launcher.LastHandle.ReportStopped();
        var paused = await WaitForAsync(
            store,
            project.Id,
            state => state.Status == AgentProjectStatus.Paused &&
                service.RunningAgents(project.Id).Count == 0);

        Assert.IsNull(paused.Lease);
        Assert.IsNull(service.StopFault);
        Assert.IsEmpty(service.RunningAgents(project.Id));
    }

    [TestMethod]
    public async Task PassingTheTurnAsksTheWorkingAgentToHandOverEarly()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var passing = await service.PassTheTurnAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.HandoffPending, passing.Status);
        Assert.AreEqual(AgentHandoffReason.UserRequested, passing.RequestedHandoffReason);
        Assert.AreEqual(AgentProvider.Codex, passing.ActiveAgent, "A request must not release the turn.");
        Assert.IsFalse(launcher.LastHandle!.StopRequested, "Passing the turn is not stopping.");
    }

    [TestMethod]
    public async Task AnAgentWaitingForAPersonSaysSoInsteadOfLookingBusy()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        launcher.LastHandle!.ReportNeedsPerson("Codex is waiting for permission.");
        var blocked = await WaitForAsync(
            store,
            project.Id,
            state => state.Status == AgentProjectStatus.NeedsAttention);

        Assert.AreEqual(
            AgentProvider.Codex,
            blocked.ActiveAgent,
            "A question is not proof that the session stopped, so the turn is kept.");
        StringAssert.Contains(blocked.AttentionReason, "waiting");
        Assert.AreEqual("Codex is waiting for permission.", service.LastReport(project.Id, AgentProvider.Codex));
    }

    [TestMethod]
    public async Task TheOtherAgentIsStartedOnlyWhenThereIsSomethingToHandOver()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);
        Assert.AreEqual(1, launcher.Launches, "Filekin does not keep a second agent idling.");

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                AgentProjectCoordinator.RequestHandoff(
                    current,
                    AgentProvider.Codex,
                    AgentHandoffReason.UserRequested),
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The partner is started at the moment it is needed.");
        StringAssert.Contains(
            launcher.LastRequest!.Prompt,
            "handed this work over",
            "The agent picking up a handoff is told that is what it is doing.");
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task AnAgentThatHandsOverByItselfBringsThePartnerIn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Nobody asked. Codex decided its own part was done, which is the only way a relay can run
        // without a person pressing a button for every leg.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The partner is started at the moment it is needed.");
        Assert.AreEqual(AgentHandoffReason.WorkCompleted, transferred.LastHandoff?.Reason);
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task AReturningAgentResumesItsConversationInsteadOfStartingOver()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await service.StartAsync(project.Id, AgentProvider.Codex);
        var firstCodexTurn = launcher.LastHandle!;
        var firstCodexObservation = service.Session(project.Id, AgentProvider.Codex);
        Assert.IsNotNull(firstCodexObservation);
        Assert.IsNull(launcher.LastRequest!.ResumeSessionId);

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        firstCodexTurn.ReportStopped();
        await WaitForAsync(store, project.Id, state => state.ActiveAgent == AgentProvider.ClaudeCode);
        var claudeTurn = launcher.LastHandle!;

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.ClaudeCode, AgentProvider.Codex)));
        claudeTurn.ReportStopped();
        await WaitForAsync(store, project.Id, state => state.ActiveAgent == AgentProvider.Codex);

        Assert.AreEqual(3, launcher.Launches);
        Assert.AreEqual(AgentProvider.Codex, launcher.LastRequest!.Provider);
        Assert.AreEqual(
            "native-Codex",
            launcher.LastRequest.ResumeSessionId,
            "The provider process may restart, but the Codex thread must remain the same conversation.");
        Assert.AreSame(
            firstCodexObservation,
            service.Session(project.Id, AgentProvider.Codex),
            "The session observation must keep receiving the resumed conversation's later turns.");
    }

    [TestMethod]
    public async Task AnIdleSessionCanBeEndedEvenThoughItHoldsNoTurn()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 2 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Claude clocked in behind the turn: here, waiting, and holding nothing.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(current, AgentProvider.ClaudeCode, usage: null));

        var stopped = await service.StopSessionsAsync(project.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(2, stopped);
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.ClaudeCode);
        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(
            AgentConnectionState.Offline,
            persisted!.Participant(AgentProvider.ClaudeCode).ConnectionState,
            "An agent whose session ended is no longer here.");
        Assert.AreEqual(
            AgentProvider.Codex,
            persisted.ActiveAgent,
            "Ending somebody else's session never touches the turn.");
        Assert.AreEqual(AgentProjectStatus.Working, persisted.Status);
    }

    [TestMethod]
    public async Task AnAgentIsNeverStartedForAProjectWithNoObjective()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, string.Empty);
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        // The first live run did exactly this: the agent started, clocked in, spent a turn, and could
        // only message a person to ask what the job was.
        var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.StartAsync(project.Id, AgentProvider.Codex));

        StringAssert.Contains(refused.Message, "no objective");
        Assert.AreEqual(0, launcher.Launches, "Nothing may be spent before there is something to do.");
    }

    [TestMethod]
    public async Task AnObjectiveOfNothingButSpacesIsStillNoObjective()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "   ");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.StartAsync(project.Id, AgentProvider.Codex));
        Assert.AreEqual(0, launcher.Launches);
    }

    [TestMethod]
    public async Task ClosingCountsWhatTheProviderStillHasOpenNotWhatThisWindowWatches()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var launcher = new FakeLauncher(store);

        // A Claude background session stays open and idle after its turn, so it is no longer watched
        // here long before it stops existing. Closing on the watched list reported nothing while two
        // of them were still running, and that is how they were left behind.
        launcher.LiveSessionsByProvider[AgentProvider.ClaudeCode] = 2;
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        Assert.AreEqual(0, service.LiveSessions().Count, "Nothing is being watched.");

        var live = await service.CountLiveProviderSessionsAsync();

        Assert.AreEqual(2, live.Sessions);
        Assert.IsFalse(live.Unknown);
        Assert.IsTrue(live.AnythingRunning);
        Assert.IsNotNull(project);
    }

    [TestMethod]
    public async Task AProviderThatCannotBeAskedIsReportedAsUnknownNotAsNothing()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var launcher = new FakeLauncher(store)
        {
            CountLiveSessionsFault = new InvalidOperationException("Claude Code did not answer."),
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var live = await service.CountLiveProviderSessionsAsync();

        Assert.IsTrue(live.Unknown, "Not being able to ask is not the same as nothing running.");
        Assert.IsTrue(live.AnythingRunning);
    }

    [TestMethod]
    public async Task AFailedUnwatchedSessionCheckReturnsUnknownInsteadOfStaleSuccess()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var launcher = new FakeLauncher(store)
        {
            ListClaudeBackgroundAgentsFault = new InvalidOperationException("Claude Code did not answer."),
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var liveness = await service.UnwatchedSessionLivenessAsync(
            project,
            AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentSessionLiveness.Unknown, liveness);
    }

    [TestMethod]
    public async Task AMatchingUnwatchedClaudeSessionIsRunning()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var launcher = new FakeLauncher(store)
        {
            ClaudeBackgroundAgents =
            [
                new ClaudeBackgroundAgent(
                    "short-id",
                    "claude-background-session",
                    _projectFolder,
                    "background",
                    "filekin",
                    "idle",
                    "done",
                    1234),
            ],
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var liveness = await service.UnwatchedSessionLivenessAsync(
            project,
            AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentSessionLiveness.Running, liveness);
    }

    [TestMethod]
    public async Task ClosingChecksSavedProjectsConcurrently()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var secondFolder = Path.Combine(_projectFolder, "second-count");
        Directory.CreateDirectory(secondFolder);
        await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the other build.", secondFolder));
        var launcher = new FakeLauncher(store)
        {
            CountGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var counting = service.CountLiveProviderSessionsAsync();
        await launcher.TwoClaudeCountsStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(counting.IsCompleted, "Both provider checks are waiting on the same test gate.");

        launcher.CountGate.SetResult();
        var live = await counting;

        Assert.AreEqual(0, live.Sessions);
        Assert.IsFalse(live.Unknown);
    }

    [TestMethod]
    public async Task ClosingDoesNotProbeProvidersForOfflineSavedProjects()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var live = await service.CountLiveProviderSessionsAsync();

        Assert.AreEqual(0, live.Sessions);
        Assert.IsFalse(live.Unknown);
        Assert.AreEqual(0, launcher.CountLiveSessionsCalls);
    }

    [TestMethod]
    public async Task ClosingDoesNotProbeAStaleUnwatchedCodexRecord()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(
                AgentProjectCoordinator.RecordNativeSession(
                    current,
                    AgentProvider.Codex,
                    "codex-app-server-thread"),
                AgentProvider.Codex,
                usage: null));
        var launcher = new FakeLauncher(store);
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        var live = await service.CountLiveProviderSessionsAsync();

        Assert.AreEqual(0, live.Sessions);
        Assert.IsFalse(live.Unknown);
        Assert.AreEqual(0, launcher.CountLiveSessionsCalls);
    }

    [TestMethod]
    public async Task EndingOnCloseReachesASessionThisWindowIsNoLongerWatching()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        await MarkClaudePresentAsync(
            store,
            await ApprovedProjectAsync(store, "Tidy the build."));
        var launcher = new FakeLauncher(store) { StoppableSessions = 1 };
        launcher.LiveSessionsByProvider[AgentProvider.ClaudeCode] = 1;
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        Assert.IsNull(await service.StopAllSessionsAsync());

        CollectionAssert.Contains(
            launcher.StopSessionsCalls,
            AgentProvider.ClaudeCode,
            "The session nobody was watching is exactly the one being left behind.");
    }

    [TestMethod]
    public async Task ClosingSeesEverySessionThisWindowHasOpen()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);

        Assert.AreEqual(0, service.LiveSessions().Count, "Nothing is running before anything starts.");

        await service.StartAsync(project.Id, AgentProvider.Codex);

        CollectionAssert.AreEqual(
            new[] { new AgentLiveSession(project.Id, AgentProvider.Codex) },
            service.LiveSessions().ToArray());
        var live = await service.CountLiveProviderSessionsAsync();
        Assert.AreEqual(1, live.Sessions, "The current handle is already positive liveness evidence.");
        Assert.AreEqual(0, launcher.CountLiveSessionsCalls, "A watched handle needs no provider probe.");
    }

    [TestMethod]
    public async Task AClosingWindowAsksEverySessionToStopAndReportsThatItDid()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.IsNull(
            await service.StopAllSessionsAsync(),
            "Every session was asked to stop, so the window is leaving nothing behind.");
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.Codex);
        Assert.IsTrue(launcher.LastHandle!.StopRequested);
    }

    [TestMethod]
    public async Task AClosingWindowSaysWhenAnAgentCouldNotBeEnded()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store)
        {
            ClockInOnLaunch = true,
            StopSessionsFault = new InvalidOperationException("Claude Code did not answer."),
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        var failure = await service.StopAllSessionsAsync();

        Assert.IsNotNull(failure, "A close that left a session running must never report success.");
        StringAssert.Contains(failure, "Claude Code did not answer.");
        Assert.IsNotNull(service.StopFault);
    }

    [TestMethod]
    public async Task OneAgentThatWillNotStopDoesNotSpareTheOthers()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var first = await ApprovedProjectAsync(store, "Tidy the build.");
        var secondFolder = Path.Combine(_projectFolder, "second");
        Directory.CreateDirectory(secondFolder);
        var second = await ApprovedProjectAsync(store, "Tidy the other build.", secondFolder);
        var launcher = new FakeLauncher(store)
        {
            ClockInOnLaunch = true,
            StoppableSessions = 1,
            StopSessionsFault = new InvalidOperationException("Codex did not answer."),
            StopSessionsFaultFor = AgentProvider.Codex,
        };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(first.Id, AgentProvider.Codex);
        await service.StartAsync(second.Id, AgentProvider.ClaudeCode);

        var failure = await service.StopAllSessionsAsync();

        Assert.IsNotNull(failure);
        CollectionAssert.Contains(
            launcher.StopSessionsCalls,
            AgentProvider.ClaudeCode,
            "The agent that could stop must still have been asked.");
    }

    [TestMethod]
    public async Task EndingTheWorkingAgentsSessionStaysTheCooperativeStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 1 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        await service.StopSessionsAsync(project.Id, AgentProvider.Codex);

        var persisted = await store.LoadAsync(project.Id);
        Assert.AreEqual(AgentProjectStatus.StopPending, persisted!.Status);
        Assert.AreEqual(
            AgentProvider.Codex,
            persisted.ActiveAgent,
            "The turn is released only when that provider reports its session ended.");
        Assert.IsTrue(launcher.LastHandle!.StopRequested);
    }

    [TestMethod]
    public async Task AProviderWithoutACooperativeStopSaysSoInsteadOfPretending()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = null };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        Assert.IsNull(await service.StopSessionsAsync(project.Id, AgentProvider.ClaudeCode));
    }

    [TestMethod]
    public async Task ASessionThatEndsWithoutTheTurnIsNotTreatedAsALeaseOwnersStop()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Tidy the build.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);
        var codex = launcher.LastHandle!;

        // The turn is released first, and only then does the session end.
        var coordinator = new AgentProjectCoordinator(new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));
        await store.UpdateAsync(
            project.Id,
            current => coordinator.CompleteActiveTurn(current, AgentProvider.Codex, DateTimeOffset.UtcNow));
        codex.ReportStopped();

        var persisted = await WaitForAsync(
            store,
            project.Id,
            state => state.Participant(AgentProvider.Codex).ConnectionState == AgentConnectionState.Offline);

        Assert.IsNull(service.StopFault, "A session ending without a turn is not a fault.");
        Assert.IsNull(persisted.Lease);
    }

    [TestMethod]
    public async Task ThePartnerIsStartedEvenWhenTheProjectStillRemembersItAsConnected()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // Claude clocked in during an earlier run and its session has long since ended. The project
        // still says it is here, and that is exactly the state that used to leave work sitting still.
        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.ClockIn(current, AgentProvider.ClaudeCode, usage: null));
        Assert.AreNotEqual(
            AgentConnectionState.Offline,
            (await store.LoadAsync(project.Id))!.Participant(AgentProvider.ClaudeCode).ConnectionState);
        CollectionAssert.DoesNotContain(
            service.RunningAgents(project.Id).ToArray(),
            AgentProvider.ClaudeCode);

        await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.SubmitHandoff(
                current,
                Handoff(AgentProvider.Codex, AgentProvider.ClaudeCode)));
        launcher.LastHandle!.ReportStopped();

        var transferred = await WaitForAsync(
            store,
            project.Id,
            state => state.ActiveAgent == AgentProvider.ClaudeCode);

        Assert.AreEqual(AgentProjectStatus.Working, transferred.Status);
        Assert.AreEqual(2, launcher.Launches, "The turn never moves to an agent nobody is running.");
        Assert.IsNull(service.StopFault);
    }

    [TestMethod]
    public async Task StoppingAnAgentNobodyIsWatchingReleasesTheTurnInsteadOfWaitingForever()
    {
        using var store = new SqliteAgentProjectStore(_databasePath);
        var project = await ApprovedProjectAsync(store, "Take turns writing the file.");
        var launcher = new FakeLauncher(store) { ClockInOnLaunch = true, StoppableSessions = 0 };
        await using var runtime = Runtime(store);
        await runtime.StartAsync();
        await using var service = Service(runtime, store, launcher);
        await service.StartAsync(project.Id, AgentProvider.Codex);

        // The session Filekin was watching is gone, as it would be after a restart, while the turn
        // is still recorded against that agent. Nothing will ever report a stop for it.
        await service.DisposeAsync();
        await using var reopened = Service(runtime, store, launcher);
        Assert.IsEmpty(reopened.RunningAgents(project.Id));

        var stopped = await reopened.RequestStopAsync(project.Id);

        Assert.AreEqual(AgentProjectStatus.Paused, stopped.Status);
        Assert.IsNull(stopped.Lease, "A turn nobody is running must not hold the project.");
        StringAssert.Contains(stopped.AttentionReason, "resumed");
        CollectionAssert.Contains(launcher.StopSessionsCalls, AgentProvider.Codex);
    }

    private static AgentHandoff Handoff(AgentProvider from, AgentProvider to) =>
        new(
            Guid.NewGuid(),
            from,
            to,
            DateTimeOffset.UtcNow,
            AgentHandoffReason.UserRequested,
            "First leg done.",
            "Read the state and wrote the marker.",
            "Carry on from here.",
            "Tests pass.",
            string.Empty);

    private static async Task<AgentProjectState> WaitForAsync(
        SqliteAgentProjectStore store,
        Guid projectId,
        Func<AgentProjectState, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var state = await store.LoadAsync(projectId);
            if (state is not null && condition(state))
            {
                return state;
            }

            Assert.IsLessThan(deadline, DateTimeOffset.UtcNow, "The project never reached the expected state.");
            await Task.Delay(20);
        }
    }

    private async Task<AgentProjectState> ApprovedProjectAsync(
        SqliteAgentProjectStore store,
        string objective,
        string? folderPath = null)
    {
        var project = AgentProjectCoordinator.Create(folderPath ?? _projectFolder, objective);
        await store.SaveAsync(project);
        return await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.GrantSharedCheckoutConsent(
                current,
                DateTimeOffset.UtcNow,
                Approval));
    }

    private static async Task<AgentProjectState> MarkClaudePresentAsync(
        SqliteAgentProjectStore store,
        AgentProjectState project)
    {
        var withSession = await store.UpdateAsync(
            project.Id,
            current => AgentProjectCoordinator.RecordNativeSession(
                current,
                AgentProvider.ClaudeCode,
                "claude-background-session"));
        return await store.UpdateAsync(
            withSession.Id,
            current => AgentProjectCoordinator.ClockIn(
                current,
                AgentProvider.ClaudeCode,
                usage: null));
    }

    private AgentCoordinationRuntime Runtime(
        IAgentProjectStore store,
        double codexUsedPercent = 20,
        double claudeUsedPercent = 20) =>
        Runtime(store, new FreshUsageSourceFactory(codexUsedPercent, claudeUsedPercent));

    private AgentCoordinationRuntime Runtime(
        IAgentProjectStore store,
        FreshUsageSourceFactory sources) =>
        new(
            store,
            Coordinator(),
            sources,
            new AgentMcpLaunchConfigurationFactory(_mcpExecutablePath, _databasePath),
            TimeProvider.System,
            TimeSpan.FromMinutes(1));

    private static AgentProjectCoordinator Coordinator() =>
        new(new AgentCoordinationPolicy(10, 30, TimeSpan.FromMinutes(5)));

    private static AgentRunService Service(
        AgentCoordinationRuntime runtime,
        IAgentProjectStore store,
        IAgentSessionLauncher launcher) =>
        new(
            runtime,
            store,
            Coordinator(),
            launcher,
            TimeProvider.System,
            clockInTimeout: TimeSpan.FromMilliseconds(200),
            clockInPollInterval: TimeSpan.FromMilliseconds(10));

    /// <summary>Reports allowance observed right now, so freshness never depends on test timing.</summary>
    private sealed class FreshUsageSourceFactory(double codexUsedPercent, double claudeUsedPercent)
        : IAgentUsageSourceFactory
    {
        private int _totalReads;

        public int TotalReads => Volatile.Read(ref _totalReads);

        public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath) =>
            new FreshUsageSource(
                provider,
                provider == AgentProvider.Codex ? codexUsedPercent : claudeUsedPercent,
                () => Interlocked.Increment(ref _totalReads));

        private sealed class FreshUsageSource(
            AgentProvider provider,
            double usedPercent,
            Action noteRead) : IAgentUsageSource
        {
            public AgentProvider Provider => provider;

            public Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                noteRead();
                return Task.FromResult(new AgentUsageSnapshot(
                    provider,
                    DateTimeOffset.UtcNow,
                    [new AgentUsageWindow("primary", usedPercent, TimeSpan.FromHours(5), null)]));
            }

            public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return await ReadAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Stands in for a provider. It never starts a process, and it clocks the agent in through the
    /// same store the real MCP server writes to, which is exactly what Filekin waits for.
    /// </summary>
    private sealed class FakeLauncher(IAgentProjectStore store) : IAgentSessionLauncher
    {
        public bool ClockInOnLaunch { get; init; }

        public int Launches { get; private set; }

        public AgentSessionLaunchRequest? LastRequest { get; private set; }

        public AgentProjectState? StateAtLaunch { get; private set; }

        public FakeHandle? LastHandle { get; private set; }

        public int? StoppableSessions { get; set; }

        public Exception? StopSessionsFault { get; init; }

        /// <summary>Refuse only this agent, so a close can be proved to ask the rest anyway.</summary>
        public AgentProvider? StopSessionsFaultFor { get; init; }

        public List<AgentProvider> StopSessionsCalls { get; } = [];

        /// <summary>What the provider says is still open, whatever this window is watching.</summary>
        public Dictionary<AgentProvider, int?> LiveSessionsByProvider { get; } = [];

        public Exception? CountLiveSessionsFault { get; init; }

        public Exception? ListClaudeBackgroundAgentsFault { get; init; }

        public IReadOnlyList<ClaudeBackgroundAgent> ClaudeBackgroundAgents { get; init; } = [];

        public TaskCompletionSource? CountGate { get; init; }

        public TaskCompletionSource TwoClaudeCountsStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _claudeCountCalls;

        public int CountLiveSessionsCalls { get; private set; }

        public async Task<int?> CountLiveSessionsAsync(
            AgentProvider provider,
            string projectFolderPath,
            CancellationToken cancellationToken = default)
        {
            CountLiveSessionsCalls++;
            if (CountLiveSessionsFault is { } fault)
            {
                throw fault;
            }

            if (provider == AgentProvider.ClaudeCode && CountGate is { } gate)
            {
                if (Interlocked.Increment(ref _claudeCountCalls) >= 2)
                {
                    TwoClaudeCountsStarted.TrySetResult();
                }

                await gate.Task.WaitAsync(cancellationToken);
            }

            return LiveSessionsByProvider.GetValueOrDefault(provider);
        }

        public Task<int?> StopSessionsAsync(
            AgentProvider provider,
            string projectFolderPath,
            CancellationToken cancellationToken = default)
        {
            StopSessionsCalls.Add(provider);
            if (StopSessionsFault is { } fault &&
                (StopSessionsFaultFor is null || StopSessionsFaultFor == provider))
            {
                throw fault;
            }

            return Task.FromResult(StoppableSessions);
        }

        public Task<IReadOnlyList<ClaudeBackgroundAgent>> ListClaudeBackgroundAgentsAsync(
            string projectFolderPath,
            CancellationToken cancellationToken = default)
        {
            if (ListClaudeBackgroundAgentsFault is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(ClaudeBackgroundAgents);
        }

        public async Task<IAgentSessionHandle> LaunchAsync(
            AgentSessionLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            Launches++;
            LastRequest = request;
            StateAtLaunch = await store.LoadAsync(request.ProjectId, cancellationToken);
            if (ClockInOnLaunch)
            {
                await store.UpdateAsync(
                    request.ProjectId,
                    current => AgentProjectCoordinator.ClockIn(current, request.Provider, usage: null),
                    cancellationToken);
            }

            LastHandle = new FakeHandle(request.Provider);
            return LastHandle;
        }
    }

    private sealed class FakeHandle(AgentProvider provider)
        : IAgentSessionHandle, IInteractiveAgentSessionHandle
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _needsPerson =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentProvider Provider => provider;

        public string NativeSessionId => $"native-{provider}";

        public Task Stopped => _stopped.Task;

        public Task<string> NeedsPerson => _needsPerson.Task;

        public string? LastReport { get; private set; }

        public AgentSessionEventFeed Events { get; } = new();

        public bool StopRequested { get; private set; }

        public string? LastPrompt { get; private set; }

        public AgentSessionRequestResponse? LastResponse { get; private set; }

        public void ReportNeedsPerson(string reason)
        {
            LastReport = reason;
            _needsPerson.TrySetResult(reason);
        }

        public void ReportStopped() => _stopped.TrySetResult();

        public Task RequestStopAsync(CancellationToken cancellationToken = default)
        {
            StopRequested = true;
            return Task.CompletedTask;
        }

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.CompletedTask;
        }

        public Task RespondAsync(
            AgentSessionRequestResponse response,
            CancellationToken cancellationToken = default)
        {
            LastResponse = response;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
