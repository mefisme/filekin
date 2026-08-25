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
    public async Task ResizeIsObservedByTheRootShell()
    {
        var host = new ConPtyTerminalHost();
        await using var session = host.Start(CreateRequest());
        var output = new OutputAccumulator(session);

        session.Resize(new TerminalSize(120, 40));

        // ConPTY delivers the resize to the child shell asynchronously, so poll the live
        // window width inside PowerShell until it observes the new value rather than sampling
        // it once (a single early sample races the resize and never re-checks). Width is the
        // reliable axis: the pseudoconsole reflows on columns and reports them consistently,
        // whereas the reported window Height is host-specific (a headless CI runner can report
        // a different buffer-derived height than an interactive desktop), so asserting an exact
        // Height would be environment-flaky without proving anything more about propagation.
        //
        // The report is tagged with a sentinel character built from its code point so the tag
        // never appears in the terminal's echo of this command line (only in the evaluated
        // output); waiting on the echo would otherwise race ahead of the real result. The full
        // observed size is always emitted so a mismatch is diagnosable rather than an opaque
        // timeout.
        await session.WriteAsync(
            "$t=[char]0xA7; $w=$null; for($i=0;$i -lt 100;$i++){ $w=$Host.UI.RawUI.WindowSize; " +
            "if($w.Width -eq 120){break}; Start-Sleep -Milliseconds 100 }; " +
            "$b=$Host.UI.RawUI.BufferSize; " +
            "Write-Output ($t+'win='+$w.Width+'x'+$w.Height+';buf='+$b.Width+'x'+$b.Height+';i='+$i)\r");

        Assert.IsTrue(
            await output.WaitForAsync("§win=", WaitTimeout),
            "PowerShell did not report any window size after the resize.");

        var reported = output.Snapshot();
        Assert.IsTrue(
            reported.Contains("§win=120x", StringComparison.Ordinal),
            $"The resized pseudoconsole width should be visible to PowerShell RawUI. Captured output:\n{reported}");
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

        public string Snapshot()
        {
            lock (_gate)
            {
                return _text.ToString();
            }
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
