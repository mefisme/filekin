using Filekin.App.ViewModels;
using Filekin.Core.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// How the tab strip gives out its width. Tabs shrink evenly as the strip fills, the way a browser's
/// do, and the strip has no scrollbar under it any more.
/// </summary>
/// <remarks>
/// A horizontal scrollbar is a poor way to find a tab: it hides the thing being looked for, and it
/// turns up exactly when there is least room for it. The owner called it clunky on 2026-09-03, and
/// `HANDOFF-ARCHIVE.md` had recorded the overflow on 2026-08-27 with the shape left undecided.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ShellViewModelTabStripWidthTests
{
    private const string Session = "55555555-5555-5555-5555-555555555555";
    private const double WideWindow = 1600;
    private const double NarrowWindow = 620;

    [TestMethod]
    public async Task WithRoomToSpareATitleIsNotShrunkAtAll()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        shell.AddTerminal("PowerShell", new FakeTerminalSession());

        shell.MeasureTabStrip(WideWindow);

        Assert.IsTrue(shell.AreTabTitlesShowing);
        Assert.AreEqual(
            180,
            shell.TabTitleMaxWidth,
            "One tab in a wide window is never squeezed to make room for nothing.");
    }

    [TestMethod]
    public async Task EachNewTabTakesAShareRatherThanPushingTheLastOneOff()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.MeasureTabStrip(NarrowWindow);
        var withOne = shell.TabTitleMaxWidth;

        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.AddTerminal("PowerShell", new FakeTerminalSession());

        Assert.IsTrue(
            shell.TabTitleMaxWidth < withOne,
            "Opening a tab has to cost every tab a little, or the last one falls off the end.");
    }

    [TestMethod]
    public async Task ATabStripThatChangesWithoutBeingResizedStillReMeasures()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        shell.MeasureTabStrip(NarrowWindow);
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        var withOne = shell.TabTitleMaxWidth;

        // No resize here. The window is the same size; there is simply one more tab in it.
        shell.AddTerminal("PowerShell", new FakeTerminalSession());

        Assert.IsTrue(
            shell.TabTitleMaxWidth < withOne,
            "The share moves when the tabs change, not only when the window does.");
    }

    [TestMethod]
    public async Task PastAPointTheStripIsHonestlyBetterOffAsIcons()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        for (var index = 0; index < 12; index++)
        {
            shell.AddTerminal("PowerShell", new FakeTerminalSession());
        }

        shell.MeasureTabStrip(NarrowWindow);

        Assert.IsFalse(
            shell.AreTabTitlesShowing,
            "Two or three clipped characters say less than the icon beside them and cost the width.");
        Assert.AreEqual(0, shell.TabTitleMaxWidth);
    }

    [TestMethod]
    public async Task ACliTabCountsAgainstTheStripWhereverItsGroupIs()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        var project = AgentProjects.Working(AgentProvider.Codex);
        var tab = new AgentProjectTabViewModel(project.FolderPath)
        {
            Project = project,
            ObjectiveDraft = project.Objective,
        };
        shell.AgentProjectTabs.Add(tab);
        shell.SelectAgentProjectTab(tab);
        shell.MeasureTabStrip(NarrowWindow);
        var withTheProjectAlone = shell.TabTitleMaxWidth;

        shell.AddTerminal(
            "Codex CLI · demo",
            new FakeTerminalSession(),
            new AgentTerminalIdentity(project.Id, AgentProvider.Codex, Session));

        Assert.IsTrue(
            shell.TabTitleMaxWidth < withTheProjectAlone,
            "A tab drawn inside a project's group takes strip width like any other.");
    }

    [TestMethod]
    public async Task AnEmptyStripIsNotDividedByNothing()
    {
        await using var shell = new ShellViewModel(new FakeDirectoryLister());

        shell.MeasureTabStrip(NarrowWindow);

        Assert.IsTrue(shell.AreTabTitlesShowing);
        Assert.AreEqual(180, shell.TabTitleMaxWidth);
    }

    [TestMethod]
    public async Task ARunOfZeroWidthDoesNotThrowAwayTheWidthAlreadyKnown()
    {
        // WPF raises a size change of zero while a window is minimized or first laid out. Taking that
        // as the truth would collapse every title to an icon and leave it there.
        await using var shell = new ShellViewModel(new FakeDirectoryLister());
        shell.AddTerminal("PowerShell", new FakeTerminalSession());
        shell.MeasureTabStrip(WideWindow);
        var known = shell.TabTitleMaxWidth;

        shell.MeasureTabStrip(0);

        Assert.AreEqual(known, shell.TabTitleMaxWidth);
    }
}
