using Filekin.App.ViewModels;
using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// Putting a CLI tab back on a resumed session. Claude has to stop to be resumed, so the tab a person
/// was reading is left as an ordinary shell when the turn moves on, and this is what puts it back.
/// </summary>
/// <remarks>
/// The rule that matters most here is what it must never do: a window that grows terminals by itself
/// is a worse fault than the one being fixed (DECISIONS.md, 2026-09-02). Only a tab the person
/// already opened is followed, and a tab they closed stays closed.
///
/// No provider is launched. The way a CLI is opened is passed in, so these tests can also state the
/// order the work happens in — the old tab is still there while the new one opens.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ShellViewModelReattachAgentCliTabsTests
{
    private const string OldSession = "11111111-1111-1111-1111-111111111111";
    private const string NewSession = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    public async Task ATabSomebodyLeftOpenIsPutBackOnTheResumedSession()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        var stranded = StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(1, opened.Count, "The stranded tab is the one thing there was to put back.");
        Assert.AreEqual(NewSession, opened[0].SessionId, "It must be put on the session that is running now.");
        Assert.AreEqual(1, shell.TerminalTabs.Count, "Putting a tab back replaces it; it does not add one.");
        Assert.IsFalse(shell.TerminalTabs.Contains(stranded), "The tab showing the ended CLI is gone.");
        Assert.AreEqual(
            NewSession,
            shell.TerminalTabs[0].AgentSession?.NativeSessionId,
            "The tab left standing is the one on the resumed conversation.");
    }

    [TestMethod]
    public async Task NoTabIsOpenedForSomebodyWhoNeverOpenedOne()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "A person who has not opened a CLI has not asked for one.");
        Assert.AreEqual(0, shell.TerminalTabs.Count, "The window must not grow a terminal by itself.");
    }

    [TestMethod]
    public async Task ATabThatStillHasItsAgentRunningIsLeftAlone()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);

        // No CompleteAgentProcessAsync: this CLI has not exited, so nothing has been stranded.
        shell.AddTerminal(
            "Claude · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(project.Id, AgentProvider.ClaudeCode, OldSession));
        var live = shell.TerminalTabs[0];

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "A CLI that is still running is not something to reopen.");
        Assert.AreSame(live, shell.TerminalTabs[0], "The live tab stays exactly where it is.");
    }

    [TestMethod]
    public async Task ATabAlreadyShowingTheResumedSessionIsNotReopened()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode, NewSession);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "This tab was showing that conversation already.");
        Assert.AreEqual(1, shell.TerminalTabs.Count, "Nothing was added and nothing was closed.");
    }

    [TestMethod]
    public async Task ATabBelongingToAnotherProjectIsNotFollowed()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, Guid.NewGuid(), AgentProvider.ClaudeCode);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "Another folder's terminal is not this project's to reuse.");
    }

    [TestMethod]
    public async Task ATabBelongingToTheOtherAgentIsNotFollowed()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.Codex);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "Codex's tab must never be put on Claude's conversation.");
    }

    [TestMethod]
    public async Task TheNewTabIsOpenedBeforeTheOldOneIsClosed()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        var stranded = StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        var oldTabWasStillThere = false;

        await shell.ReattachAgentCliTabsAsync(project, (provider, resumed) =>
        {
            oldTabWasStillThere = shell.TerminalTabs.Contains(stranded);
            Open(shell, project.Id, provider, resumed);
            return Task.FromResult<string?>(null);
        });

        Assert.IsTrue(
            oldTabWasStillThere,
            "A session the provider will not attach to yet must leave the person's tab where it is.");
    }

    [TestMethod]
    public async Task AProviderThatRefusesLeavesTheTabExactlyWhereItWas()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        var stranded = StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);

        await shell.ReattachAgentCliTabsAsync(
            project,
            (_, _) => Task.FromResult<string?>("Claude Code is not running that session."));

        Assert.AreEqual(1, shell.TerminalTabs.Count, "A refusal closes nothing.");
        Assert.AreSame(stranded, shell.TerminalTabs[0], "The person keeps the tab they had.");
        Assert.AreEqual(
            OldSession,
            stranded.ReattachableAgentSession?.NativeSessionId,
            "It still remembers the CLI it was showing.");
    }

    [TestMethod]
    public async Task ARefusedSessionIsNotTriedAgainOnTheNextRefresh()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        var tries = 0;

        for (var refresh = 0; refresh < 3; refresh++)
        {
            await shell.ReattachAgentCliTabsAsync(project, (_, _) =>
            {
                tries++;
                return Task.FromResult<string?>("Not yet.");
            });
        }

        Assert.AreEqual(1, tries, "A tab that reopened itself on every refresh would flicker under the reader.");
    }

    [TestMethod]
    public async Task TheSameResumedSessionIsPutBackOnlyOnce()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);

        var first = await ReattachAsync(shell, project);
        var second = await ReattachAsync(shell, project);

        Assert.AreEqual(1, first.Count, "The first refresh does the work.");
        Assert.AreEqual(0, second.Count, "The second has nothing left to do.");
        Assert.AreEqual(1, shell.TerminalTabs.Count, "And it must not stack another terminal on top.");
    }

    [TestMethod]
    public async Task TheReattachedTabTakesThePlaceOfTheOneItReplaced()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        var ordinary = shell.TerminalTabs[1];

        await ReattachAsync(shell, project);

        Assert.AreEqual(2, shell.TerminalTabs.Count, "One tab in, one tab out.");
        Assert.AreEqual(
            NewSession,
            shell.TerminalTabs[0].AgentSession?.NativeSessionId,
            "The tab strip must not reorder itself under the person reading it.");
        Assert.AreSame(ordinary, shell.TerminalTabs[1], "The terminal they also had stays put.");
    }

    [TestMethod]
    public async Task SomebodyReadingAnotherTerminalIsNotDraggedToTheReattachedOne()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        var ordinary = shell.TerminalTabs[1];
        shell.SelectTerminal(ordinary);

        await ReattachAsync(shell, project);

        Assert.AreSame(ordinary, shell.SelectedTerminal, "A tab that opened by itself must not steal the view.");
    }

    [TestMethod]
    public async Task SomebodyReadingThatExactTabStaysOnIt()
    {
        await using var shell = NewShell();
        var project = ResumedProject(AgentProvider.ClaudeCode);
        var stranded = StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.SelectTerminal(stranded);

        await ReattachAsync(shell, project);

        Assert.AreEqual(
            NewSession,
            shell.SelectedTerminal?.AgentSession?.NativeSessionId,
            "The person was reading this CLI, so they are left reading it.");
    }

    [TestMethod]
    public async Task BothAgentsGetTheirOwnTabBack()
    {
        await using var shell = NewShell();
        var project = AgentProjects.WithSession(
            AgentProjects.WithSession(
                AgentProjects.BothPresent(AgentProvider.ClaudeCode),
                AgentProvider.ClaudeCode,
                NewSession),
            AgentProvider.Codex,
            NewSession);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.Codex);

        var opened = await ReattachAsync(shell, project);

        CollectionAssert.AreEquivalent(
            new[] { AgentProvider.Codex, AgentProvider.ClaudeCode },
            opened.ConvertAll(entry => entry.Provider),
            "Each agent's own tab is put back on its own conversation.");
        Assert.AreEqual(2, shell.TerminalTabs.Count, "Two tabs went out and two came back.");
    }

    [TestMethod]
    public async Task AnAgentWithNoSavedConversationIsSkipped()
    {
        await using var shell = NewShell();
        var project = AgentProjects.Working(AgentProvider.ClaudeCode);
        StrandedAgentTab(shell, project.Id, AgentProvider.ClaudeCode);

        var opened = await ReattachAsync(shell, project);

        Assert.AreEqual(0, opened.Count, "There is no session to put the tab back on.");
    }

    private static ShellViewModel NewShell() => new(new FakeDirectoryLister());

    /// <summary>A project whose named agent has been resumed into a conversation nobody is showing.</summary>
    private static AgentProjectState ResumedProject(AgentProvider provider) =>
        AgentProjects.WithSession(AgentProjects.Working(provider), provider, NewSession);

    /// <summary>A tab a person opened whose CLI has since exited back to PowerShell.</summary>
    private static TerminalTabViewModel StrandedAgentTab(
        ShellViewModel shell,
        Guid projectId,
        AgentProvider provider,
        string sessionId = OldSession)
    {
        Open(shell, projectId, provider, sessionId);
        var tab = shell.TerminalTabs[^1];
        tab.CompleteAgentProcessAsync().GetAwaiter().GetResult();
        return tab;
    }

    private static void Open(ShellViewModel shell, Guid projectId, AgentProvider provider, string sessionId) =>
        shell.AddTerminal(
            $"{AgentParticipantViewModel.ShortName(provider)} · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(projectId, provider, sessionId));

    private static async Task<List<(AgentProvider Provider, string SessionId)>> ReattachAsync(
        ShellViewModel shell,
        AgentProjectState project)
    {
        var opened = new List<(AgentProvider Provider, string SessionId)>();
        await shell.ReattachAgentCliTabsAsync(project, (provider, resumed) =>
        {
            opened.Add((provider, resumed));
            Open(shell, project.Id, provider, resumed);
            return Task.FromResult<string?>(null);
        });
        return opened;
    }
}
