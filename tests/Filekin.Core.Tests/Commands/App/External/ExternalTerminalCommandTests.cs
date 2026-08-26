using Filekin.Core.Commands.App;
using Filekin.Core.Commands.App.External;
using Filekin.Core.Shell;

namespace Filekin.Core.Tests.Commands.App.External;

[TestClass]
public sealed class ExternalTerminalCommandTests
{
    private static readonly ShellLocation FileSystemLocation = new(@"D:\Work", "FileSystem", @"D:\Work");
    private static readonly ShellLocation ProviderLocation = new(@"HKLM:\", "Registry", null);

    [TestMethod]
    public async Task BareExtOpensAnExternalTerminalAtTheCurrentFolder()
    {
        var launcher = new FakeLauncher();
        var command = new ExternalTerminalCommand(launcher);

        var result = await command.ExecuteAsync(Context([]));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Work", launcher.TerminalFolder);
        Assert.IsNull(launcher.External);
    }

    [TestMethod]
    public async Task ExtWithAProgramLaunchesItExternallyWithItsArguments()
    {
        var launcher = new FakeLauncher();
        var command = new ExternalTerminalCommand(launcher);

        var result = await command.ExecuteAsync(Context(["code", "."]));

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(launcher.TerminalFolder);
        Assert.IsNotNull(launcher.External);
        Assert.AreEqual(@"D:\Work", launcher.External.Value.Folder);
        Assert.AreEqual("code", launcher.External.Value.Program);
        var expectedArguments = new[] { "." };
        CollectionAssert.AreEqual(expectedArguments, launcher.External.Value.Arguments.ToArray());
    }

    [TestMethod]
    public async Task NonFilesystemLocationIsRejected()
    {
        var launcher = new FakeLauncher();
        var command = new ExternalTerminalCommand(launcher);

        var result = await command.ExecuteAsync(
            new AppCommandContext(ProviderLocation, new ParsedAppCommand("ext", [])));

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(launcher.TerminalFolder);
    }

    [TestMethod]
    public async Task LaunchFailureBecomesAnErrorResult()
    {
        var command = new ExternalTerminalCommand(new ThrowingLauncher());

        var result = await command.ExecuteAsync(Context([]));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "/ext failed");
    }

    [TestMethod]
    public async Task DispatcherRoutesExtToTheExternalTerminalCommand()
    {
        var launcher = new FakeLauncher();
        var dispatcher = new AppCommandDispatcher([new ExternalTerminalCommand(launcher)]);

        var result = await dispatcher.DispatchAsync("/ext", FileSystemLocation);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"D:\Work", launcher.TerminalFolder);
    }

    private static AppCommandContext Context(IReadOnlyList<string> arguments) =>
        new(FileSystemLocation, new ParsedAppCommand("ext", arguments));

    private sealed class FakeLauncher : IExternalLauncher
    {
        public string? TerminalFolder { get; private set; }

        public (string Folder, string Program, IReadOnlyList<string> Arguments)? External { get; private set; }

        public void OpenTerminal(string folderPath) => TerminalFolder = folderPath;

        public void OpenExternal(string folderPath, string program, IReadOnlyList<string> arguments) =>
            External = (folderPath, program, arguments);
    }

    private sealed class ThrowingLauncher : IExternalLauncher
    {
        public void OpenTerminal(string folderPath) =>
            throw new InvalidOperationException("no terminal");

        public void OpenExternal(string folderPath, string program, IReadOnlyList<string> arguments) =>
            throw new InvalidOperationException("no program");
    }
}
