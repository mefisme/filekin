using Filekin.App.ViewModels;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// The reset that follows removing a project: if this window has that folder's own control-room tab
/// open, closing it must land the person back on Files exactly as any other closed project tab does —
/// "the first screen" rather than a stale control room pointing at a project that no longer exists.
/// </summary>
/// <remarks>
/// This targets <see cref="ShellViewModel.CloseAgentProjectTabForRemovedFolder"/> directly rather than
/// the full <c>/projects</c> removal path: that path calls <c>AgentRuntimeAsync</c>, which hard-codes
/// the owner's real <c>%APPDATA%</c> database (HANDOFF.md). The database side of removal — the project
/// row and everything that cascades from it actually being gone — is proved at the store and runtime
/// layers in Filekin.Infrastructure.Windows.Tests, against a temp database. This is the seam that lets
/// the tab-reset half be proved too, without either test touching a database it should not.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ShellViewModelRemoveAgentProjectTests
{
    private const string OtherFolder = @"C:\work\other-project";

    [TestMethod]
    public async Task ClosingTheTabForARemovedFolderResetsToFiles()
    {
        await using var shell = NewShell();
        var tab = OpenTab(shell, AgentProjects.Folder);
        Assert.IsTrue(shell.IsAgentsOpen);
        Assert.IsFalse(shell.IsFilesWorkspaceSelected);

        shell.CloseAgentProjectTabForRemovedFolder(AgentProjects.Folder);

        Assert.IsFalse(shell.IsAgentsOpen, "The stale control room must not be left showing.");
        Assert.IsTrue(shell.IsFilesWorkspaceSelected, "Closing the selected tab falls back to Files.");
        Assert.IsFalse(shell.AgentProjectTabs.Contains(tab));
    }

    [TestMethod]
    public async Task TheFolderIsMatchedWithoutCaseSensitivity()
    {
        await using var shell = NewShell();
        var tab = OpenTab(shell, AgentProjects.Folder);

        shell.CloseAgentProjectTabForRemovedFolder(AgentProjects.Folder.ToUpperInvariant());

        Assert.IsFalse(shell.AgentProjectTabs.Contains(tab), "Windows folder identity is case-insensitive.");
    }

    [TestMethod]
    public async Task NothingHappensWhenNoTabIsOpenForThatFolder()
    {
        await using var shell = NewShell();

        shell.CloseAgentProjectTabForRemovedFolder(@"C:\nowhere\ever\opened");

        Assert.AreEqual(0, shell.AgentProjectTabs.Count);
        Assert.IsFalse(shell.IsAgentsOpen);
    }

    [TestMethod]
    public async Task AnotherProjectsOpenTabAndSelectionAreLeftAlone()
    {
        await using var shell = NewShell();
        var kept = OpenTab(shell, AgentProjects.Folder);
        var removed = new AgentProjectTabViewModel(OtherFolder);
        shell.AgentProjectTabs.Add(removed);
        shell.SelectAgentProjectTab(kept);

        shell.CloseAgentProjectTabForRemovedFolder(OtherFolder);

        Assert.IsTrue(shell.AgentProjectTabs.Contains(kept), "The tab that was not removed must stay open.");
        Assert.IsFalse(shell.AgentProjectTabs.Contains(removed));
        Assert.IsTrue(
            shell.IsAgentsOpen,
            "Removing a project whose tab was not the one being read must not move the view.");
        Assert.AreSame(kept, shell.SelectedAgentProjectTab, "The person was reading this tab and stays on it.");
    }

    private static ShellViewModel NewShell() => new(new FakeDirectoryLister());

    private static AgentProjectTabViewModel OpenTab(ShellViewModel shell, string folderPath)
    {
        var tab = new AgentProjectTabViewModel(folderPath) { Project = AgentProjects.Ready() };
        shell.AgentProjectTabs.Add(tab);
        shell.SelectAgentProjectTab(tab);
        return tab;
    }
}
