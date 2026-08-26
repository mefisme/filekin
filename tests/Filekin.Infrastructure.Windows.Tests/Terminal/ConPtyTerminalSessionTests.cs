using System.Text;
using Filekin.Core.Shell;
using Filekin.Core.Terminal;
using Filekin.Infrastructure.Windows.Terminal;

namespace Filekin.Infrastructure.Windows.Tests.Terminal;

[TestClass]
public sealed class ConPtyTerminalSessionTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void ResolveReturnsAnExistingPowerShellExecutable()
    {
        var executable = PowerShellExecutableLocator.Resolve();

        Assert.IsTrue(File.Exists(executable), $"Resolved executable should exist: {executable}");
    }

    [TestMethod]
    public async Task SessionRoundTripsInputToOutput()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest());
        var output = new OutputAccumulator(session);

        Assert.IsGreaterThan(0, session.RootProcessId);

        await session.WriteAsync("Write-Output 'FLKN_IN_OK'\r");

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_IN_OK", WaitTimeout),
            "The command output should be observed on the session output stream.");
    }

    [TestMethod]
    public async Task ConcurrentWritesReachTheShellInOrder()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest());
        var output = new OutputAccumulator(session);

        // A terminal surface sends one keystroke per call without awaiting the previous one. The
        // session must serialize those writes; concurrent writes to the input FileStream would
        // interleave and mangle the typed line.
        const string line = "Write-Output 'FLKN_ORDERED_INPUT'";
        var writes = line.Select(character => session.WriteAsync(character.ToString()).AsTask()).ToArray();
        await Task.WhenAll(writes);
        await session.WriteAsync("\r");

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_ORDERED_INPUT", WaitTimeout),
            "Characters written without awaiting should still reach the shell in order.");
    }

    [TestMethod]
    public async Task ResizeIsAcceptedAndTheSessionStaysUsable()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest());
        var output = new OutputAccumulator(session);

        // Resizing the live pseudoconsole must succeed against the root session (Resize throws on a
        // failing ResizePseudoConsole HRESULT). Whether the hosted PowerShell's RawUI reflects the
        // new size is environment-dependent: on a headless CI runner the native call succeeds yet the
        // child's RawUI.WindowSize stays at its initial value, while an interactive desktop does
        // observe the change (see HANDOFF.md — "ConPTY resize propagation"). This test therefore
        // asserts the boundary contract this type owns — the resize is accepted and the session keeps
        // working afterwards — rather than the child's RawUI, which cannot be observed reliably here.
        session.Resize(new TerminalSize(120, 40));

        await session.WriteAsync("Write-Output 'FLKN_AFTER_RESIZE'\r");

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_AFTER_RESIZE", WaitTimeout),
            "The session should keep working after a resize.");
    }

    [TestMethod]
    public async Task StartupCommandRunsAndTheShellRemainsInteractive()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest(startupCommand: "Write-Output 'FLKN_STARTUP'"));
        var output = new OutputAccumulator(session);

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_STARTUP", WaitTimeout),
            "The one-shot startup command should run.");

        // -NoExit should keep the shell alive so a further command still executes.
        await session.WriteAsync("Write-Output 'FLKN_AFTER'\r");

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_AFTER", WaitTimeout),
            "The PowerShell prompt should remain after the startup command returns.");
    }

    [TestMethod]
    public async Task OutputProducedBeforeFirstSubscriberIsReplayed()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest(startupCommand: "Write-Output 'FLKN_EARLY'"));

        // Let the startup command normally finish before the renderer attaches. ConPty output begins
        // racing as soon as Start creates the root process; the session boundary must retain those
        // first chunks so a terminal tab never opens with its prompt or startup output missing.
        await Task.Delay(TimeSpan.FromSeconds(1));
        var output = new OutputAccumulator(session);

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_EARLY", WaitTimeout),
            "Output emitted before the first subscriber should be replayed to that subscriber.");
    }

    [TestMethod]
    public async Task ExitEndsTheRootProcessAndRaisesExited()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest());
        _ = new OutputAccumulator(session);

        var exited = new TaskCompletionSource();
        session.Exited += (_, _) => exited.TrySetResult();

        await session.WriteAsync("exit\r");

        using var cancellation = new CancellationTokenSource(WaitTimeout);
        await session.WaitForExitAsync(cancellation.Token);
        await exited.Task.WaitAsync(WaitTimeout);

        Assert.IsTrue(session.HasExited);
        Assert.IsNotNull(session.ExitCode);
    }

    private static TerminalSessionRequest CreateRequest(string? startupCommand = null)
    {
        var directory = Path.GetTempPath();
        var location = new ShellLocation(directory, "FileSystem", directory);
        var launch = new ShellTerminalLaunchRequest(location, startupCommand);

        // Profile loading is disabled so the tests do not depend on the developer's PowerShell
        // profile; the production default remains profile-on.
        return new TerminalSessionRequest(
            launch,
            title: null,
            initialSize: new TerminalSize(80, 24),
            loadProfile: false);
    }

    private sealed class OutputAccumulator
    {
        private readonly object _gate = new();
        private readonly StringBuilder _text = new();

        public OutputAccumulator(ITerminalSession session)
        {
            session.OutputReceived += (_, e) =>
            {
                var decoded = Encoding.UTF8.GetString(e.Data.Span);
                lock (_gate)
                {
                    _text.Append(decoded);
                }
            };
        }

        public async Task<bool> WaitForAsync(string expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (_text.ToString().Contains(expected, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                await Task.Delay(50);
            }

            return false;
        }
    }
}
