using Filekin.Core.Commands.App;
using Filekin.Core.Shell;

namespace Filekin.Core.Tests.Commands.App;

[TestClass]
public sealed class AppCommandDispatcherTests
{
    private static readonly ShellLocation Location = new(@"D:\Work", "FileSystem", @"D:\Work");

    [TestMethod]
    public async Task KnownCommandIsDispatchedToItsHandler()
    {
        var command = new RecordingCommand("copy", AppCommandResult.Ok("done"));
        var dispatcher = new AppCommandDispatcher([command]);

        var result = await dispatcher.DispatchAsync("/copy a b", Location);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(command.LastContext);
        Assert.AreEqual("copy", command.LastContext!.Command.Name);
        Assert.AreEqual("a|b", string.Join('|', command.LastContext.Command.Arguments));
    }

    [TestMethod]
    public async Task UnknownCommandReturnsAnError()
    {
        var dispatcher = new AppCommandDispatcher([new RecordingCommand("copy", AppCommandResult.Ok("done"))]);

        var result = await dispatcher.DispatchAsync("/bogus", Location);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
        StringAssert.Contains(result.Message, "/bogus");
    }

    [TestMethod]
    public async Task BareSlashReturnsAnError()
    {
        var dispatcher = new AppCommandDispatcher([new RecordingCommand("copy", AppCommandResult.Ok("done"))]);

        var result = await dispatcher.DispatchAsync("/", Location);

        Assert.AreEqual(AppCommandOutcome.Error, result.Outcome);
    }

    [TestMethod]
    public void DuplicateRegistrationIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new AppCommandDispatcher(
            [
                new RecordingCommand("copy", AppCommandResult.Ok("a")),
                new RecordingCommand("copy", AppCommandResult.Ok("b")),
            ]));
    }

    [TestMethod]
    public async Task AnAliasDispatchesToTheSameHandlerAndKeepsTheTypedName()
    {
        var command = new RecordingCommand("toss", AppCommandResult.Ok("done")) { Aliases = ["trash", "delete"] };
        var dispatcher = new AppCommandDispatcher([command]);

        var result = await dispatcher.DispatchAsync("/delete a.txt", Location);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("delete", command.LastContext!.Command.Name);
    }

    [TestMethod]
    public void AnAliasThatCollidesWithAnotherCommandIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new AppCommandDispatcher(
            [
                new RecordingCommand("toss", AppCommandResult.Ok("a")) { Aliases = ["delete"] },
                new RecordingCommand("delete", AppCommandResult.Ok("b")),
            ]));
    }

    private sealed class RecordingCommand : IAppCommand
    {
        private readonly AppCommandResult _result;

        public RecordingCommand(string name, AppCommandResult result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }

        public IReadOnlyList<string> Aliases { get; init; } = [];

        public AppCommandContext? LastContext { get; private set; }

        public Task<AppCommandResult> ExecuteAsync(AppCommandContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.FromResult(_result);
        }
    }
}
