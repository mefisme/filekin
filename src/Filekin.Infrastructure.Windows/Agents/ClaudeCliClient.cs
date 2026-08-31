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

    /// <param name="trustFolder">
    /// Set only when the owner has said this folder is safe to work in. It asks Claude for its own
    /// auto mode, which judges each action instead of prompting for every edit. It is never
    /// <c>bypassPermissions</c>: Claude still refuses or asks about genuinely risky work. Without it
    /// Filekin sends no permission mode at all and the owner's own Claude settings stay in charge.
    /// </param>
    public async Task<string> StartBackgroundSessionAsync(
        string folderPath,
        string displayName,
        string prompt,
        string mcpConfigurationJson,
        string settingsJson,
        bool trustFolder = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpConfigurationJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);

        List<string> arguments = ["--bg", "--name", displayName];
        if (trustFolder)
        {
            arguments.Add("--permission-mode");
            arguments.Add("auto");
        }

        arguments.AddRange([
            "--strict-mcp-config",
            "--mcp-config",
            mcpConfigurationJson,
            "--settings",
            settingsJson,
            prompt,
        ]);

        var output = await RunTextAsync(
                arguments,
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

    /// <summary>
    /// Reads the provider-supported recent output for one exact background session. It is returned as
    /// one text snapshot; callers must not parse it into invented tool lifecycle events.
    /// </summary>
    public async Task<string?> ReadBackgroundSessionLogsAsync(
        string folderPath,
        string nativeSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);
        var output = await RunTextAsync(["logs", nativeSessionId], fullPath, cancellationToken)
            .ConfigureAwait(false);
        return ClaudeCliProtocol.NormalizeBackgroundLogs(output);
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
