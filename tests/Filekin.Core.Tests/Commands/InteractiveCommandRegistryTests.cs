using Filekin.Core.Commands;

namespace Filekin.Core.Tests.Commands;

[TestClass]
public sealed class InteractiveCommandRegistryTests
{
    private static readonly string[] NoArguments = [];
    private static readonly string[] OneArgument = ["script.py"];

    private static readonly string[] AllBuiltIns =
        ["claude", "cmd", "codex", "powershell", "pwsh", "python", "python3", "ssh"];

    [TestMethod]
    public void BuiltInRulesStillApplyWithNoUserPrograms()
    {
        var registry = new InteractiveCommandRegistry();

        Assert.IsTrue(registry.IsInteractive("claude", NoArguments));
        Assert.IsTrue(registry.IsInteractive("ssh", OneArgument));
        Assert.IsFalse(registry.IsInteractive("vim", NoArguments));
    }

    [TestMethod]
    public void AUserProgramBecomesInteractive()
    {
        var registry = new InteractiveCommandRegistry();

        registry.ReplaceUserPrograms(["vim", "htop"]);

        Assert.IsTrue(registry.IsInteractive("vim", NoArguments));
        Assert.IsTrue(registry.IsInteractive("htop", NoArguments));
    }

    [TestMethod]
    public void AUserProgramIsInteractiveWhateverItsArguments()
    {
        // A user rule is a plain program name, so it is not argument-sensitive the way the built-in
        // Python rule is: `vim file.txt` is still an editor.
        var registry = new InteractiveCommandRegistry();
        registry.ReplaceUserPrograms(["vim"]);

        Assert.IsTrue(registry.IsInteractive("vim", OneArgument));
    }

    [TestMethod]
    public void UserProgramsAreMatchedCaseInsensitively()
    {
        var registry = new InteractiveCommandRegistry();
        registry.ReplaceUserPrograms(["Vim"]);

        Assert.IsTrue(registry.IsInteractive("VIM", NoArguments));
    }

    [TestMethod]
    public void ReplacingUserProgramsDropsThePreviousSet()
    {
        var registry = new InteractiveCommandRegistry();
        registry.ReplaceUserPrograms(["vim"]);

        registry.ReplaceUserPrograms(["htop"]);

        Assert.IsFalse(registry.IsInteractive("vim", NoArguments));
        Assert.IsTrue(registry.IsInteractive("htop", NoArguments));
    }

    [TestMethod]
    public void AUserProgramCannotDisableABuiltInRule()
    {
        // The user list only ever adds. Clearing it must leave every shipped rule intact.
        var registry = new InteractiveCommandRegistry();

        registry.ReplaceUserPrograms([]);

        Assert.IsTrue(registry.IsInteractive("pwsh", NoArguments));
    }

    [TestMethod]
    public void TheBuiltInListIsSortedAndComplete()
    {
        var builtIn = InteractiveCommandRegistry.BuiltInPrograms;

        CollectionAssert.AreEqual(AllBuiltIns, builtIn.ToArray());
    }

    [TestMethod]
    public void BuiltInMembershipIsReportedCaseInsensitively()
    {
        Assert.IsTrue(InteractiveCommandRegistry.IsBuiltIn("SSH"));
        Assert.IsTrue(InteractiveCommandRegistry.IsBuiltIn("python3"));
        Assert.IsFalse(InteractiveCommandRegistry.IsBuiltIn("vim"));
    }
}
