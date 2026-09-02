using System.Text.Json;
using Filekin.Core.Agents;

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

    /// <param name="workMode">
    /// The owner's answer for this folder, sent as Claude's own permission mode. Working on its own
    /// is <c>auto</c>, which judges each action instead of prompting for every edit; looking without
    /// touching is <c>plan</c>, which reads and thinks and writes nothing. It is never
    /// <c>bypassPermissions</c>: Claude still refuses or asks about genuinely risky work. For the
    /// owner's own settings Filekin sends no permission mode at all, and a session started with a
    /// mode keeps that mode until it ends, because there is no window to change it in.
    /// </param>
    /// <param name="model">
    /// The model the user chose, or <see langword="null"/> to leave the choice to Claude Code's own
    /// configuration. Filekin passes it for this session only and writes no setting.
    /// </param>
    public async Task<string> StartBackgroundSessionAsync(
        string folderPath,
        string displayName,
        string prompt,
        string mcpConfigurationJson,
        string settingsJson,
        AgentWorkMode workMode = AgentWorkMode.UseMyOwnSettings,
        string? model = null,
        string? effort = null,
        string? resumeSessionId = null,
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
        if (PermissionMode(workMode) is { } permissionMode)
        {
            arguments.Add("--permission-mode");
            arguments.Add(permissionMode);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(effort))
        {
            arguments.Add("--effort");
            arguments.Add(effort.Trim());
        }

        // Handoffs continue the same Claude conversation. The background worker may be restarted,
        // but --resume appends to the provider-owned session instead of throwing its context away.
        if (!string.IsNullOrWhiteSpace(resumeSessionId))
        {
            arguments.Add("--resume");
            arguments.Add(resumeSessionId.Trim());
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

    /// <summary>
    /// The background agents Claude reports for one folder, through its own documented
    /// <c>claude agents --json</c> interface. It reads nothing from a transcript and starts nothing.
    /// </summary>
    public async Task<IReadOnlyList<ClaudeBackgroundAgent>> ListBackgroundAgentsAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        _billingOverrideDetector.ThrowIfConfigured(fullPath);
        var output = await RunTextAsync(
                ["agents", "--json", "--cwd", fullPath],
                fullPath,
                cancellationToken)
            .ConfigureAwait(false);
        return ClaudeCliProtocol.ParseBackgroundAgents(output);
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

    /// <summary>
    /// Claude's own name for the owner's answer, or <see langword="null"/> to send nothing and leave
    /// the owner's own Claude settings in charge. Filekin only ever names these two modes: the ones
    /// it does not name either turn the permission system off or make no sense with no window to
    /// answer in.
    /// </summary>
    private static string? PermissionMode(AgentWorkMode workMode) => workMode switch
    {
        AgentWorkMode.WorkOnItsOwn => "auto",
        AgentWorkMode.LookDontTouch => "plan",
        _ => null,
    };

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
