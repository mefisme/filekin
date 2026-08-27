using Filekin.Core.Commands;
using Filekin.Infrastructure.Windows.Commands;

namespace Filekin.Infrastructure.Windows.Tests.Commands;

[TestClass]
public sealed class WindowsRunTargetResolverTests
{
    private string _root = null!;
    private string _pathFolder = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Filekin-Run-{Guid.NewGuid():N}");
        _pathFolder = Path.Combine(_root, "path");
        Directory.CreateDirectory(_pathFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void CurrentFolderWinsBeforePath()
    {
        var local = Path.Combine(_root, "tool.cmd");
        var onPath = Path.Combine(_pathFolder, "tool.cmd");
        File.WriteAllText(local, "@echo off");
        File.WriteAllText(onPath, "@echo off");
        var resolver = CreateResolver();

        var result = resolver.Resolve("tool", [], _root);

        Assert.IsTrue(string.Equals(local, result.LaunchTarget, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(RunTargetKind.Terminal, result.Kind);
    }

    [TestMethod]
    public void SimpleNameResolvesThroughPathAndPathExt()
    {
        var expected = Path.Combine(_pathFolder, "snapmap-midi.cmd");
        File.WriteAllText(expected, "@echo off");
        var resolver = CreateResolver();

        var result = resolver.Resolve("snapmap-midi", [], _root);

        Assert.IsTrue(string.Equals(expected, result.LaunchTarget, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(RunTargetKind.Terminal, result.Kind);
    }

    [TestMethod]
    public void RegisteredProgramRoutesToTerminalEvenWhenItCannotBeResolved()
    {
        var registry = new InteractiveCommandRegistry();
        registry.ReplaceUserPrograms(["private-tool"]);
        var resolver = new WindowsRunTargetResolver(registry, string.Empty, ".EXE");

        var result = resolver.Resolve("private-tool", [], _root);

        Assert.AreEqual("private-tool", result.LaunchTarget);
        Assert.AreEqual(RunTargetKind.Terminal, result.Kind);
    }

    [TestMethod]
    public void AssociatedDocumentRemainsAnExternalLaunch()
    {
        var document = Path.Combine(_root, "notes.txt");
        File.WriteAllText(document, "notes");
        var resolver = CreateResolver();

        var result = resolver.Resolve("notes.txt", [], _root);

        Assert.AreEqual(document, result.LaunchTarget);
        Assert.AreEqual(RunTargetKind.External, result.Kind);
        Assert.IsTrue(result.FoundOnDisk);
    }

    [TestMethod]
    public void FolderIsReportedSeparatelyInsteadOfOpeningExplorer()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "folder")).FullName;
        var resolver = CreateResolver();

        var result = resolver.Resolve("folder", [], _root);

        Assert.AreEqual(folder, result.LaunchTarget);
        Assert.AreEqual(RunTargetKind.Directory, result.Kind);
    }

    [TestMethod]
    public void WindowsConsoleExecutableRoutesToTerminal()
    {
        var commandPrompt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        var resolver = CreateResolver();

        var result = resolver.Resolve(commandPrompt, [], _root);

        Assert.AreEqual(RunTargetKind.Terminal, result.Kind);
    }

    [TestMethod]
    public void WindowsGuiExecutableRemainsAnExternalLaunch()
    {
        var notepad = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "notepad.exe");
        var resolver = CreateResolver();

        var result = resolver.Resolve(notepad, [], _root);

        Assert.AreEqual(RunTargetKind.External, result.Kind);
    }

    [TestMethod]
    public void PowerShellScriptRoutesToTerminalEvenWhenItIsNotOnPathExt()
    {
        var script = Path.Combine(_root, "build.ps1");
        File.WriteAllText(script, "Write-Output 'hello'");
        var resolver = CreateResolver();

        var result = resolver.Resolve("build.ps1", [], _root);

        Assert.AreEqual(script, result.LaunchTarget);
        Assert.AreEqual(RunTargetKind.Terminal, result.Kind);
    }

    [TestMethod]
    public void AnUnresolvableNameIsLaunchedAsTypedRatherThanRefused()
    {
        var resolver = CreateResolver();

        var result = resolver.Resolve("not-installed", [], _root);

        Assert.AreEqual("not-installed", result.LaunchTarget);
        Assert.AreEqual(RunTargetKind.External, result.Kind);
        Assert.IsFalse(result.FoundOnDisk);
    }

    private WindowsRunTargetResolver CreateResolver() =>
        new(new InteractiveCommandRegistry(), _pathFolder, ".EXE;.CMD");
}
