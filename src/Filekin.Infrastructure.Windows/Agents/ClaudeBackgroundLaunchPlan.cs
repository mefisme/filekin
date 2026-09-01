using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// User-reviewable description of one Claude background turn. Creating a plan does not write Claude
/// settings, start a process, consume model usage, or grant a Filekin working-tree lease.
/// </summary>
public sealed record ClaudeBackgroundLaunchPlan
{
    internal ClaudeBackgroundLaunchPlan(
        string projectFolderPath,
        string displayName,
        string prompt,
        string mcpConfigurationJson,
        string statusLineCommand)
    {
        ProjectFolderPath = projectFolderPath;
        DisplayName = displayName;
        Prompt = prompt;
        McpConfigurationJson = mcpConfigurationJson;
        SettingsPreviewJson = CreateSettingsJson(statusLineCommand);
        ApprovalDescription =
            "Allow Claude background sessions for this Filekin agent project to use its shared checkout instead of a Claude worktree, to run Filekin's own status-line helper so Filekin can read this project's Claude usage windows, and to use Filekin's own coordination tools without asking each time.";
    }

    public string ProjectFolderPath { get; }

    public string DisplayName { get; }

    public string Prompt { get; }

    public string SettingsPreviewJson { get; }

    public string ApprovalDescription { get; }

    internal string McpConfigurationJson { get; }

    /// <summary>
    /// Call only after the app has confirmed the owner accepted <see cref="SettingsPreviewJson"/> for
    /// this Filekin agent project through its explicit setup command/action. The later project UI may
    /// persist that consent in Filekin state for subsequent coordinated sessions. Ordinary Filekin
    /// startup never creates or approves this plan. This method never persists Claude settings or
    /// starts Claude.
    /// </summary>
    public ApprovedClaudeBackgroundLaunch ApproveSharedCheckout() => new(this);

    internal static ClaudeBackgroundLaunchPlan Create(
        string projectFolderPath,
        string displayName,
        string prompt,
        AgentMcpLaunchConfiguration mcpServer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(mcpServer);

        var fullPath = Path.GetFullPath(projectFolderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"The agent project folder does not exist: {fullPath}");
        }

        ValidateMcpConfiguration(fullPath, mcpServer);
        var configurationJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["filekin"] = new
                {
                    type = "stdio",
                    command = Path.GetFullPath(mcpServer.ExecutablePath),
                    args = mcpServer.Arguments,
                },
            },
        });

        return new ClaudeBackgroundLaunchPlan(
            fullPath,
            displayName.Trim(),
            prompt,
            configurationJson,
            ClaudeStatusLineCommand.CreateShellCommand(
                mcpServer.ExecutablePath,
                new ClaudeStatusLineRequest(mcpServer.ProjectId, fullPath, mcpServer.Arguments[5])));
    }

    private static void ValidateMcpConfiguration(
        string projectFolderPath,
        AgentMcpLaunchConfiguration configuration)
    {
        if (configuration.Provider != AgentProvider.ClaudeCode)
        {
            throw new ArgumentException("The MCP launch configuration must identify Claude Code.", nameof(configuration));
        }

        if (!Path.IsPathFullyQualified(configuration.ExecutablePath) ||
            !string.Equals(
                Path.GetFileNameWithoutExtension(configuration.ExecutablePath),
                "Filekin.Mcp",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The MCP launch configuration must use the Filekin.Mcp executable.", nameof(configuration));
        }

        if (!PathsEqual(projectFolderPath, configuration.WorkingDirectory))
        {
            throw new ArgumentException("The MCP launch configuration belongs to another project folder.", nameof(configuration));
        }

        if (configuration.Arguments.Count != 6 ||
            configuration.Arguments[0] != "--project" ||
            !Guid.TryParse(configuration.Arguments[1], out var identifiedProject) ||
            identifiedProject != configuration.ProjectId ||
            configuration.Arguments[2] != "--provider" ||
            configuration.Arguments[3] != "claude" ||
            configuration.Arguments[4] != "--state-db" ||
            !Path.IsPathFullyQualified(configuration.Arguments[5]))
        {
            throw new ArgumentException("The Filekin MCP arguments are not the fixed project-scoped form.", nameof(configuration));
        }
    }

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Passed inline to one background session. Filekin never writes the user's Claude settings files,
    /// and the status line it sets is its own quota-reading helper, fixed to this project.
    /// </summary>
    /// <summary>The one permission rule Filekin adds: its own coordination tools, nothing else.</summary>
    private static readonly string[] FilekinToolsOnly = ["mcp__filekin__.*"];

    private static string CreateSettingsJson(string statusLineCommand) =>
        JsonSerializer.Serialize(new
        {
            // Filekin's own coordination tools are allowed, and nothing else is. Without this a
            // background session stops at a permission prompt on its very first call and cannot even
            // clock in, which no coordination can survive and no person can answer from Filekin. It
            // is not a permission bypass: file, command, and network permissions stay exactly as the
            // owner's own Claude settings have them, and Filekin still answers nothing on their
            // behalf. Codex is given the same narrow allow-list through its own launch config.
            permissions = new
            {
                allow = FilekinToolsOnly,
            },
            worktree = new
            {
                bgIsolation = "none",
            },
            statusLine = new
            {
                type = "command",
                command = statusLineCommand,
            },
            hooks = new
            {
                StopFailure = new[]
                {
                    new
                    {
                        matcher = "rate_limit",
                        hooks = new[]
                        {
                            new
                            {
                                type = "mcp_tool",
                                server = "filekin",
                                tool = "filekin_report_usage_limit",
                                input = new
                                {
                                    nativeSessionId = "${session_id}",
                                },
                                timeout = 10,
                            },
                        },
                    },
                },
            },
        });
}

/// <summary>Compile-time evidence that the owner accepted one plan's shared-checkout preview.</summary>
public sealed class ApprovedClaudeBackgroundLaunch
{
    internal ApprovedClaudeBackgroundLaunch(ClaudeBackgroundLaunchPlan plan)
    {
        Plan = plan;
    }

    internal ClaudeBackgroundLaunchPlan Plan { get; }
}
