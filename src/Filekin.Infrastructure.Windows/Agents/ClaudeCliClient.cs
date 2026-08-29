using System.Diagnostics;
using System.Text.Json;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Runs non-interactive, non-model Claude Code inspection commands. Session dispatch is deliberately
/// excluded until its background-session and worktree behavior is validated for Filekin's lease.
/// </summary>
internal sealed class ClaudeCliClient
{
    private readonly ClaudeBillingOverrideDetector _billingOverrideDetector;
    private readonly string _executable;

    public ClaudeCliClient(string executable = "claude")
        : this(executable, new ClaudeBillingOverrideDetector())
    {
    }

    internal ClaudeCliClient(
        string executable,
        ClaudeBillingOverrideDetector billingOverrideDetector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(billingOverrideDetector);
        _executable = executable;
        _billingOverrideDetector = billingOverrideDetector;
    }

    public async Task<ClaudeSubscriptionAccount> ReadAccountAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);

        var json = await RunJsonAsync(["auth", "status", "--json"], fullPath, cancellationToken)
            .ConfigureAwait(false);
        return ClaudeCliProtocol.ParseAccount(json);
    }

    public async Task<IReadOnlyList<ClaudeBackgroundSession>> ReadBackgroundSessionsAsync(
        string folderPath,
        bool includeCompleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);
        var arguments = new List<string>
        {
            "agents",
            "--json",
            "--cwd",
            fullPath,
        };
        if (includeCompleted)
        {
            arguments.Add("--all");
        }

        var json = await RunJsonAsync(arguments, fullPath, cancellationToken).ConfigureAwait(false);
        return ClaudeCliProtocol.ParseBackgroundSessions(json);
    }

    private async Task<JsonElement> RunJsonAsync(
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The installed Claude Code CLI did not start.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Claude Code exited with code {process.ExitCode}."
                    : $"Claude Code exited with code {process.ExitCode}: {error.Trim()}");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Claude Code returned invalid JSON.", exception);
        }
    }
}
