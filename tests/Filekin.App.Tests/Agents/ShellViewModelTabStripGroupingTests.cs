using Filekin.App.ViewModels;
using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// How the tab strip groups itself: each agent project keeps the CLI tabs it owns, and the person's
/// own shells sit in a group of their own at the end.
/// </summary>
/// <remarks>
/// Ctrl+Tab walks the same list the strip draws, and these tests hold the two together. Two orders
/// built from one set of tabs is how a keyboard walk starts jumping about for no reason a person
/// can see, and it is exactly the kind of drift nobody notices until they are mid-task.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ShellViewModelTabStripGroupingTests
{
    private const string Session = "44444444-4444-4444-4444-444444444444";

    [TestMethod]
    public async Task AProjectKeepsTheCliTabsItOwns()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        OpenCli(shell, project.Id, AgentProvider.Codex);

        CollectionAssert.AreEqual(
            shell.TerminalTabs.ToArray(),
            tab.CliTabs.ToArray(),
            "The CLI Filekin opened for this project belongs beside it.");
        Assert.AreEqual(0, shell.PlainTerminals.Count, "It is not one of the person's own shells.");
    }

    [TestMethod]
    public async Task ATerminalSomebodyOpenedForThemselvesStaysInTheirOwnGroup()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());

        Assert.AreEqual(0, tab.CliTabs.Count, "No project owns a shell the person opened.");
        Assert.AreEqual(1, shell.PlainTerminals.Count);
        Assert.IsTrue(shell.HasPlainTerminals, "So the strip draws the divider before them.");
    }

    [TestMethod]
    public async Task TwoProjectsEachKeepTheirOwn()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var first = AgentProjects.Working(AgentProvider.Codex);
        var second = AgentProjectCoordinator.GrantSharedCheckoutConsent(
            AgentProjectCoordinator.Create(@"C:\work\other", "Another job."),
            AgentProjects.Now,
            AgentProjects.Approval);
        var firstTab = ShowProject(shell, first);
        var secondTab = ShowProject(shell, second);
        OpenCli(shell, first.Id, AgentProvider.Codex);
        OpenCli(shell, second.Id, AgentProvider.ClaudeCode);

        Assert.AreEqual(1, firstTab.CliTabs.Count);
        Assert.AreEqual(1, secondTab.CliTabs.Count);
        Assert.AreNotSame(firstTab.CliTabs[0], secondTab.CliTabs[0], "Neither project holds the other's.");
    }

    [TestMethod]
    public async Task ACliTabStaysInItsGroupAfterItsProviderExits()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        OpenCli(shell, project.Id, AgentProvider.Codex);

        await shell.TerminalTabs[0].CompleteAgentProcessAsync();
        shell.AddTerminal("PowerShell", new FakeTerminalSession());

        Assert.AreEqual(
            1,
            tab.CliTabs.Count,
            "A tab that moved groups the moment its CLI ended would jump under the reader.");
    }

    [TestMethod]
    public async Task ClosingAProjectHandsItsTabsBackToThePerson()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        OpenCli(shell, project.Id, AgentProvider.Codex);

        shell.CloseAgentProjectTab(tab);

        Assert.AreEqual(
            1,
            shell.PlainTerminals.Count,
            "Closing the control room never closes a terminal, so its tabs must still be somewhere.");
    }

    [TestMethod]
    public async Task CtrlTabWalksTheStripInTheOrderItIsDrawn()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        OpenCli(shell, project.Id, AgentProvider.Codex);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        var ownShell = shell.PlainTerminals[0];
        shell.SelectFilesWorkspace();

        shell.SelectAdjacentWorkspace(forward: true);
        Assert.AreSame(tab, shell.SelectedAgentProjectTab, "First stop after Files is the project.");

        shell.SelectAdjacentWorkspace(forward: true);
        Assert.AreSame(tab.CliTabs[0], shell.SelectedTerminal, "Then the CLI that project owns.");

        shell.SelectAdjacentWorkspace(forward: true);
        Assert.AreSame(ownShell, shell.SelectedTerminal, "Then the person's own shell.");

        shell.SelectAdjacentWorkspace(forward: true);
        Assert.IsTrue(shell.IsFilesWorkspaceSelected, "And round to Files again.");
    }

    [TestMethod]
    public async Task WalkingBackwardsFollowsTheSameOrder()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = ShowProject(shell, project);
        OpenCli(shell, project.Id, AgentProvider.Codex);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.SelectFilesWorkspace();

        shell.SelectAdjacentWorkspace(forward: false);
        Assert.AreSame(
            shell.PlainTerminals[0],
            shell.SelectedTerminal,
            "Back from Files is the last tab on the strip.");

        shell.SelectAdjacentWorkspace(forward: false);
        Assert.AreSame(tab.CliTabs[0], shell.SelectedTerminal);

        shell.SelectAdjacentWorkspace(forward: false);
        Assert.AreSame(tab, shell.SelectedAgentProjectTab);
    }

    private static AgentProjectTabViewModel ShowProject(
        ShellViewModel shell,
        AgentProjectState project,
        string? folderPath = null)
    {
        var tab = new AgentProjectTabViewModel(folderPath ?? project.FolderPath)
        {
            Project = project,
            ObjectiveDraft = project.Objective,
        };
        shell.AgentProjectTabs.Add(tab);
        shell.SelectAgentProjectTab(tab);
        return tab;
    }

    private static void OpenCli(ShellViewModel shell, Guid projectId, AgentProvider provider) =>
        shell.AddTerminal(
            $"{AgentParticipantViewModel.ShortName(provider)} CLI · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(projectId, provider, Session));
}
