using Filekin.Core.Commands;
using Filekin.Core.Shell;
using Filekin.Core.Terminal;

namespace Filekin.Core.Tests.Commands;

[TestClass]
public sealed class CommandRouterTests
{
    private static readonly ShellLocation FilesLocation = new(@"D:\Projects\App", "FileSystem", @"D:\Projects\App");

    [TestMethod]
    public async Task FiniteCommandExecutesOnTheShellAndStartsNoTerminal()
    {
        var shell = new FakeShellBackend(new ShellExecutionResult([], [], FilesLocation, TerminalLaunchRequest: null));
        var terminalHost = new FakeTerminalHost();
        var router = CreateRouter(shell, terminalHost);

        var result = await router.RouteAsync("git status");

        Assert.AreEqual(CommandRoute.FiniteShell, result.Route);
        Assert.AreEqual("git status", shell.LastExecutedCommand);
        Assert.IsNotNull(result.ShellResult);
        Assert.IsNull(result.TerminalSession);
        Assert.AreEqual(0, terminalHost.StartCount);
    }

    [TestMethod]
    public async Task InteractiveCommandStartsATerminalWithoutExecutingOnTheShell()
    {
        var shell = new FakeShellBackend(new ShellExecutionResult([], [], FilesLocation, TerminalLaunchRequest: null));
        var terminalHost = new FakeTerminalHost();
        var router = CreateRouter(shell, terminalHost);

        var result = await router.RouteAsync("claude");

        Assert.AreEqual(CommandRoute.InteractiveTerminal, result.Route);
        Assert.IsNotNull(result.TerminalSession);
        Assert.IsNull(shell.LastExecutedCommand);
        Assert.AreEqual(1, terminalHost.StartCount);

        var request = terminalHost.LastRequest!;
        Assert.AreEqual("claude", request.Launch.CommandText);
        Assert.AreEqual(FilesLocation.PowerShellPath, request.Launch.InitialLocation.PowerShellPath);
        Assert.AreEqual("claude · App", request.Title);
    }

    [TestMethod]
    public async Task ProviderDelegationStartsATerminalForTheFiniteResult()
    {
        var providerLocation = new ShellLocation(@"HKLM:\", "Registry", fileSystemPath: null);
        var delegation = new ShellTerminalLaunchRequest(providerLocation);
        var shell = new FakeShellBackend(new ShellExecutionResult([], [], FilesLocation, delegation));
        var terminalHost = new FakeTerminalHost();
        var router = CreateRouter(shell, terminalHost);

        var result = await router.RouteAsync("cd HKLM:\\");

        Assert.AreEqual(CommandRoute.FiniteShell, result.Route);
        Assert.IsNotNull(result.ShellResult);
        Assert.IsNotNull(result.TerminalSession);
        Assert.AreEqual(1, terminalHost.StartCount);
        Assert.AreSame(delegation, terminalHost.LastRequest!.Launch);
    }

    [TestMethod]
    public async Task AppCommandIsReturnedWithoutExecutingAnything()
    {
        var shell = new FakeShellBackend(new ShellExecutionResult([], [], FilesLocation, TerminalLaunchRequest: null));
        var terminalHost = new FakeTerminalHost();
        var router = CreateRouter(shell, terminalHost);

        var result = await router.RouteAsync("/history");

        Assert.AreEqual(CommandRoute.AppCommand, result.Route);
        Assert.AreEqual("/history", result.AppCommandInput);
        Assert.IsNull(shell.LastExecutedCommand);
        Assert.AreEqual(0, terminalHost.StartCount);
    }

    private static CommandRouter CreateRouter(IShellBackend shell, ITerminalHost terminalHost)
    {
        return new CommandRouter(shell, terminalHost, new CommandClassifier(new InteractiveCommandRegistry()));
    }

    private sealed class FakeShellBackend : IShellBackend
    {
        private readonly ShellExecutionResult _result;

        public FakeShellBackend(ShellExecutionResult result)
        {
            _result = result;
        }

        public string? LastExecutedCommand { get; private set; }

        public Task<ShellExecutionResult> ExecuteAsync(string commandText, CancellationToken cancellationToken = default)
        {
            LastExecutedCommand = commandText;
            return Task.FromResult(_result);
        }

        public Task<ShellLocation> GetLocationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result.CurrentLocation);
        }

        public Task<ShellLocation> SetFileSystemLocationAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result.CurrentLocation);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTerminalHost : ITerminalHost
    {
        public int StartCount { get; private set; }

        public TerminalSessionRequest? LastRequest { get; private set; }

        public ITerminalSession Start(TerminalSessionRequest request)
        {
            StartCount++;
            LastRequest = request;
            return new FakeTerminalSession();
        }
    }

    private sealed class FakeTerminalSession : ITerminalSession
    {
        public int RootProcessId => 1;

        public bool HasExited => false;

        public int? ExitCode => null;

#pragma warning disable CS0067 // Events are part of the contract but unused by these routing tests.
        public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

        public event EventHandler<TerminalExitEventArgs>? Exited;
#pragma warning restore CS0067

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(string text, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public void Resize(TerminalSize size)
        {
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
