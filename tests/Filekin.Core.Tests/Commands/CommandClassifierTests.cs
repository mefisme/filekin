using Filekin.Core.Commands;

namespace Filekin.Core.Tests.Commands;

[TestClass]
public sealed class CommandClassifierTests
{
    private static CommandClassifier CreateClassifier()
    {
        return new CommandClassifier(new InteractiveCommandRegistry());
    }

    [TestMethod]
    public void LeadingSlashRoutesToAppCommand()
    {
        var classification = CreateClassifier().Classify("/history");

        Assert.AreEqual(CommandRoute.AppCommand, classification.Route);
    }

    [TestMethod]
    public void OrdinaryCommandRoutesToFiniteShell()
    {
        var classification = CreateClassifier().Classify("git status");

        Assert.AreEqual(CommandRoute.FiniteShell, classification.Route);
        Assert.AreEqual("git", classification.Executable);
    }

    [TestMethod]
    public void EmptyInputRoutesToFiniteShell()
    {
        var classification = CreateClassifier().Classify("   ");

        Assert.AreEqual(CommandRoute.FiniteShell, classification.Route);
        Assert.IsNull(classification.Executable);
    }

    [TestMethod]
    [DataRow("claude")]
    [DataRow("codex")]
    [DataRow("ssh server-name")]
    [DataRow("pwsh")]
    [DataRow("cmd")]
    public void KnownInteractiveToolsRouteToTerminal(string input)
    {
        var classification = CreateClassifier().Classify(input);

        Assert.AreEqual(CommandRoute.InteractiveTerminal, classification.Route);
    }

    [TestMethod]
    public void BarePythonRoutesToTerminalButPythonScriptIsFinite()
    {
        var classifier = CreateClassifier();

        Assert.AreEqual(CommandRoute.InteractiveTerminal, classifier.Classify("python").Route);
        Assert.AreEqual(CommandRoute.FiniteShell, classifier.Classify("python script.py").Route);
    }

    [TestMethod]
    public void ExecutablePathAndExtensionAreNormalized()
    {
        var classification = CreateClassifier().Classify(@"C:\Tools\claude.exe --resume");

        Assert.AreEqual(CommandRoute.InteractiveTerminal, classification.Route);
        Assert.AreEqual("claude", classification.Executable);
    }
}
