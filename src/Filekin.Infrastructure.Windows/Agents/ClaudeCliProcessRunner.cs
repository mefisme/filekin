using System.Diagnostics;

namespace Filekin.Infrastructure.Windows.Agents;

internal sealed record ClaudeCliProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IClaudeCliProcessRunner
{
    Task<ClaudeCliProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

internal sealed class ClaudeCliProcessRunner : IClaudeCliProcessRunner
{
    public async Task<ClaudeCliProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
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

        return new ClaudeCliProcessResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }
}
