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
        // window size inside PowerShell until it observes the new dimensions rather than
        // sampling it once (a single early sample races the resize and never re-checks).
        await session.WriteAsync(
            "1..100 | ForEach-Object { $s=$Host.UI.RawUI.WindowSize; " +
            "if ($s.Width -eq 120 -and $s.Height -eq 40) { " +
            "Write-Output ('FLKN_SIZE:'+$s.Width+'x'+$s.Height); break }; " +
            "Start-Sleep -Milliseconds 100 }\r");

        Assert.IsTrue(
            await output.WaitForAsync("FLKN_SIZE:120x40", WaitTimeout),
            "The resized pseudoconsole dimensions should be visible to PowerShell RawUI.");
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
