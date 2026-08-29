using System.Text.Json;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Runs the small set of native Claude Code commands used by Filekin. Billing overrides are checked
/// before every command; callers remain responsible for proving subscription authentication before
/// starting a model turn.
/// </summary>
internal sealed class ClaudeCliClient
{
    private readonly ClaudeBillingOverrideDetector _billingOverrideDetector;
    private readonly string _executable;
    private readonly IClaudeCliProcessRunner _processRunner;

    public ClaudeCliClient(string executable = "claude")
        : this(executable, new ClaudeBillingOverrideDetector(), new ClaudeCliProcessRunner())
    {
    }

    internal ClaudeCliClient(
        string executable,
        ClaudeBillingOverrideDetector billingOverrideDetector,
        IClaudeCliProcessRunner? processRunner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(billingOverrideDetector);
        _executable = executable;
        _billingOverrideDetector = billingOverrideDetector;
        _processRunner = processRunner ?? new ClaudeCliProcessRunner();
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

    public async Task<string> StartBackgroundSessionAsync(
        string folderPath,
        string displayName,
        string prompt,
        string mcpConfigurationJson,
        string settingsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpConfigurationJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);

        var output = await RunTextAsync(
                [
                    "--bg",
                    "--name",
                    displayName,
                    "--strict-mcp-config",
                    "--mcp-config",
                    mcpConfigurationJson,
                    "--settings",
                    settingsJson,
                    prompt,
                ],
                fullPath,
                cancellationToken)
            .ConfigureAwait(false);
        return ClaudeCliProtocol.ParseBackgroundLaunchId(output);
    }

    public async Task StopBackgroundSessionAsync(
        string folderPath,
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);
        await RunTextAsync(["stop", nativeSessionId], fullPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<JsonElement> RunJsonAsync(
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var output = await RunTextAsync(arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

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

    private async Task<string> RunTextAsync(
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
                _executable,
                arguments,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"Claude Code exited with code {result.ExitCode}."
                    : $"Claude Code exited with code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput;
    }
}
