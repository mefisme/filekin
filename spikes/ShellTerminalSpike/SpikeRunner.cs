using System.Diagnostics;
using System.Text.Json;

namespace Filekin.ShellTerminalSpike;

internal static class SpikeRunner
{
    public static async Task<int> RunAsync(string repositoryRoot)
    {
        var report = new SpikeReport
        {
            StartedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = repositoryRoot,
            OperatingSystem = Environment.OSVersion.VersionString,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        };

        Console.WriteLine("Filekin disposable PowerShell + ConPTY spike");
        Console.WriteLine($"Repository: {repositoryRoot}");
        Console.WriteLine();

        var originalProcessDirectory = Environment.CurrentDirectory;
        var spikeDirectory = Path.Combine(repositoryRoot, "spikes", "ShellTerminalSpike");

        try
        {
            using var backend = new PowerShellRunspaceBackend(repositoryRoot);
            RunRunspaceChecks(backend, repositoryRoot, spikeDirectory, originalProcessDirectory, report);
            RunNativeChecks(backend, report);
            RunRoutingChecks(report);
        }
        catch (Exception exception)
        {
            report.Add("runspace.setup", false, exception.ToString());
        }

        await RunConPtyChecksAsync(repositoryRoot, report);
        await RunUnexpectedInteractivityCheckAsync(repositoryRoot, report);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        var artifacts = Path.Combine(spikeDirectory, "artifacts");
        Directory.CreateDirectory(artifacts);
        var reportPath = Path.Combine(artifacts, "latest-results.json");
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"Result: {report.PassedCount} passed, {report.FailedCount} failed");
        Console.WriteLine($"Evidence: {reportPath}");
        return report.FailedCount == 0 ? 0 : 1;
    }

    private static void RunRunspaceChecks(
        PowerShellRunspaceBackend backend,
        string repositoryRoot,
        string spikeDirectory,
        string originalProcessDirectory,
        SpikeReport report)
    {
        backend.Execute("$x = 'hello'");
        var variable = backend.Execute("Write-Output $x");
        report.Check("runspace.variable-persistence", variable.StandardOutput.Contains("hello"), string.Join(" | ", variable.StandardOutput));

        backend.Execute("Set-Alias -Name spikeEcho -Value Write-Output");
        var alias = backend.Execute("spikeEcho 'alias-ok'");
        report.Check("runspace.alias-persistence", alias.StandardOutput.Contains("alias-ok"), string.Join(" | ", alias.StandardOutput));

        backend.Execute("function Get-SpikeState { 'function-ok' }");
        var function = backend.Execute("Get-SpikeState");
        report.Check("runspace.function-persistence", function.StandardOutput.Contains("function-ok"), string.Join(" | ", function.StandardOutput));

        backend.Execute("Import-Module Microsoft.PowerShell.Utility -Force");
        var module = backend.Execute("(Get-Module Microsoft.PowerShell.Utility).Name");
        report.Check(
            "runspace.module-persistence",
            module.StandardOutput.Contains("Microsoft.PowerShell.Utility"),
            string.Join(" | ", module.StandardOutput));

        backend.SetFilesystemLocation(spikeDirectory);
        var filesToShell = backend.GetLocation();
        report.Check(
            "location.files-to-powershell",
            filesToShell.IsFilesystem && PathsEqual(filesToShell.ProviderPath, spikeDirectory),
            $"provider={filesToShell.ProviderName}; path={filesToShell.ProviderPath}");

        report.Check(
            "location.process-cwd-unchanged",
            PathsEqual(Environment.CurrentDirectory, originalProcessDirectory),
            $"before={originalProcessDirectory}; after={Environment.CurrentDirectory}");

        var shellToFiles = backend.Execute("Set-Location ..");
        var expectedParent = Directory.GetParent(spikeDirectory)!.FullName;
        report.Check(
            "location.powershell-to-files",
            shellToFiles.Location.IsFilesystem && PathsEqual(shellToFiles.Location.ProviderPath, expectedParent),
            $"visual-update candidate={shellToFiles.Location.ProviderPath}");

        backend.SetFilesystemLocation(repositoryRoot);
        var nonFilesystem = backend.Execute("Set-Location HKLM:\\");
        report.Check(
            "location.non-filesystem-detection",
            !nonFilesystem.Location.IsFilesystem && nonFilesystem.Location.ProviderName.Equals("Registry", StringComparison.OrdinalIgnoreCase),
            $"provider={nonFilesystem.Location.ProviderName}; path={nonFilesystem.Location.CurrentPath}");

        // The specified production rule is to route this context away, then restore Files/runspace lockstep.
        backend.SetFilesystemLocation(repositoryRoot);
        var restored = backend.GetLocation();
        report.Check(
            "location.lockstep-restored-after-routing",
            restored.IsFilesystem && PathsEqual(restored.ProviderPath, repositoryRoot),
            $"provider={restored.ProviderName}; path={restored.ProviderPath}");
    }

    private static void RunNativeChecks(PowerShellRunspaceBackend backend, SpikeReport report)
    {
        var where = backend.Execute("where.exe git");
        report.Check(
            "native.where-git",
            where.NativeExitCode == 0 && where.StandardOutput.Any(line => line.Contains("git.exe", StringComparison.OrdinalIgnoreCase)),
            FormatNativeResult(where));

        var git = backend.Execute("git status");
        report.Check(
            "native.git-status-exit",
            git.NativeExitCode.HasValue,
            FormatNativeResult(git));

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
        var escaped = executable.Replace("'", "''", StringComparison.Ordinal);
        var probe = backend.Execute($"& '{escaped}' native-probe");
        report.Check(
            "native.stdout-capture",
            probe.StandardOutput.Any(line => line.Contains("__NATIVE_STDOUT__", StringComparison.Ordinal)),
            FormatNativeResult(probe));
        report.Check(
            "native.stderr-capture",
            probe.StandardError.Any(line => line.Contains("__NATIVE_STDERR__", StringComparison.Ordinal)),
            FormatNativeResult(probe));
        report.Check("native.exit-status", probe.NativeExitCode == 7, FormatNativeResult(probe));
    }

    private static void RunRoutingChecks(SpikeReport report)
    {
        report.Check(
            "routing.known-finite",
            CommandRouting.Classify("where.exe git") == RouteKind.FiniteRunspace,
            "where.exe git -> runspace/result path");
        report.Check(
            "routing.known-interactive",
            CommandRouting.Classify("python") == RouteKind.InteractiveTerminal,
            "python -> ConPTY PowerShell terminal path");
        report.Check(
            "routing.argument-sensitive-python",
            CommandRouting.Classify("python script.py") == RouteKind.FiniteRunspace,
            "python script.py -> runspace/result path");
    }

    private static async Task RunConPtyChecksAsync(string repositoryRoot, SpikeReport report)
    {
        var powerShell = ExecutableLocator.FindOnPath("pwsh.exe");
        if (powerShell is null)
        {
            report.Add("conpty.setup", false, "pwsh.exe was not found on PATH.");
            return;
        }

        try
        {
            await using var terminal = ConPtySession.StartPowerShell(powerShell, repositoryRoot);
            report.ConPtyRootProcessId = terminal.RootProcessId;

            var ready = await terminal.WaitForTextAsync("__CONPTY_READY__", TimeSpan.FromSeconds(10));
            report.Check("conpty.powershell-root-start", ready, $"rootPid={terminal.RootProcessId}");

            await terminal.WriteAsync("Write-Output '__CONPTY_INPUT_OK__'\r");
            report.Check(
                "conpty.input-output",
                await terminal.WaitForTextAsync("__CONPTY_INPUT_OK__", TimeSpan.FromSeconds(5)),
                "terminal surface -> ConPTY -> PowerShell -> output");

            terminal.Resize(100, 30);
            await terminal.WriteAsync("$s=$Host.UI.RawUI.WindowSize; Write-Output ('__SIZE__' + $s.Width + 'x' + $s.Height)\r");
            report.Check(
                "conpty.resize",
                await terminal.WaitForTextAsync("__SIZE__100x30", TimeSpan.FromSeconds(5)),
                "ResizePseudoConsole(100x30) observed by PowerShell RawUI");

            await terminal.WriteAsync("python -q\r");
            var pythonPrompt = await terminal.WaitForTextAsync(">>>", TimeSpan.FromSeconds(10));
            report.Check("conpty.interactive-child-start", pythonPrompt, "python -q launched inside root PowerShell");

            if (pythonPrompt)
            {
                await terminal.WriteAsync("print('__PYTHON_INTERACTIVE_OK__')\r");
                report.Check(
                    "conpty.interactive-child-io",
                    await terminal.WaitForTextAsync("__PYTHON_INTERACTIVE_OK__", TimeSpan.FromSeconds(5)),
                    "Python REPL accepted input and produced output");

                await terminal.WriteAsync("exit()\r");
                await Task.Delay(250);
                await terminal.WriteAsync("Write-Output '__BACK_TO_POWERSHELL__'\r");
                report.Check(
                    "conpty.child-exit-returns-to-powershell",
                    await terminal.WaitForTextAsync("__BACK_TO_POWERSHELL__", TimeSpan.FromSeconds(8)),
                    "interactive child exited; root PowerShell accepted another command");
            }

            await terminal.WriteAsync("exit\r");
            report.Check(
                "conpty.root-exit-ends-session",
                await terminal.WaitForRootExitAsync(TimeSpan.FromSeconds(8)),
                "root PowerShell exit ended the hosted process");

            report.ConPtyOutputTail = Tail(terminal.GetCapturedOutput(), 2500);
        }
        catch (Exception exception)
        {
            report.Add("conpty.setup", false, exception.ToString());
        }
    }

    private static async Task RunUnexpectedInteractivityCheckAsync(string repositoryRoot, SpikeReport report)
    {
        using var backend = new PowerShellRunspaceBackend(repositoryRoot);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
        var escaped = executable.Replace("'", "''", StringComparison.Ordinal);

        var invocation = Task.Run(() => backend.Execute($"& '{escaped}' unexpected-child"));
        var completed = await Task.WhenAny(invocation, Task.Delay(TimeSpan.FromSeconds(2))) == invocation;

        if (!completed)
        {
            backend.StopActivePipeline();
        }

        RunspaceCommandResult? result = null;
        try
        {
            result = await invocation.WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            // The helper self-terminates after five seconds, so this is an actual teardown failure.
        }

        var observation = completed
            ? "The unknown command completed because its stdin was EOF/redirected; no terminal capability was available."
            : "The unknown command blocked beyond the finite-path observation window; PowerShell pipeline Stop was required.";

        if (result is not null)
        {
            observation += $" Result: {FormatNativeResult(result)}";
        }

        report.UnexpectedInteractivityObservation = observation;
        report.Check(
            "unexpected-interactivity.investigated",
            result is not null,
            observation);
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (first is null || second is null)
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatNativeResult(RunspaceCommandResult result) =>
        $"exit={result.NativeExitCode?.ToString() ?? "null"}; " +
        $"stdout=[{string.Join(" | ", result.StandardOutput)}]; " +
        $"stderr=[{string.Join(" | ", result.StandardError)}]; " +
        $"stopped={result.Stopped}";

    private static string Tail(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[^maximumLength..];
}

internal sealed class SpikeReport
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public int? ConPtyRootProcessId { get; set; }
    public string? ConPtyOutputTail { get; set; }
    public string? UnexpectedInteractivityObservation { get; set; }
    public List<SpikeCheck> Checks { get; } = [];
    public int PassedCount => Checks.Count(check => check.Passed);
    public int FailedCount => Checks.Count(check => !check.Passed);

    public void Check(string name, bool passed, string evidence) => Add(name, passed, evidence);

    public void Add(string name, bool passed, string evidence)
    {
        Checks.Add(new SpikeCheck(name, passed, evidence));
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}: {evidence}");
    }
}

internal sealed record SpikeCheck(string Name, bool Passed, string Evidence);
