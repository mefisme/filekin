using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Immutable process launch for one Codex App Server. Coordination plans add a project-unique,
/// required Filekin MCP server through one-run config overrides and never write Codex configuration.
/// </summary>
internal sealed class CodexAppServerLaunchPlan
{
    private static readonly IReadOnlyList<string> InspectionArguments =
        Array.AsReadOnly(new[] { "app-server", "--stdio" });

    private static readonly string[] CoordinationTools =
    [
        "filekin_clock_in",
        "filekin_read_state",
        "filekin_send_message",
        "filekin_submit_handoff",
        "filekin_accept_handoff",
        "filekin_report_blocked",
        "filekin_report_usage_limit",
        "filekin_report_completed",
    ];

    private CodexAppServerLaunchPlan(
        string executablePath,
        IReadOnlyList<string> arguments,
        AgentMcpLaunchConfiguration? coordinationIdentity)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        CoordinationIdentity = coordinationIdentity;
    }

    public string ExecutablePath { get; }

    public IReadOnlyList<string> Arguments { get; }

    public AgentMcpLaunchConfiguration? CoordinationIdentity { get; }

    public static CodexAppServerLaunchPlan CreateInspection(string executablePath = "codex")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return new CodexAppServerLaunchPlan(
            executablePath,
            InspectionArguments,
            null);
    }

    public static CodexAppServerLaunchPlan CreateCoordination(
        AgentMcpLaunchConfiguration mcp,
        string executablePath = "codex")
    {
        ArgumentNullException.ThrowIfNull(mcp);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (mcp.Provider != AgentProvider.Codex)
        {
            throw new ArgumentException(
                "A Codex App Server requires the Codex Filekin MCP identity.",
                nameof(mcp));
        }

        if (mcp.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("The Filekin MCP project id cannot be empty.", nameof(mcp));
        }

        if (!Path.IsPathFullyQualified(mcp.ExecutablePath))
        {
            throw new ArgumentException(
                "The Filekin MCP executable path must be fully qualified.",
                nameof(mcp));
        }

        if (!Path.IsPathFullyQualified(mcp.WorkingDirectory))
        {
            throw new ArgumentException(
                "The Filekin MCP working directory must be fully qualified.",
                nameof(mcp));
        }

        var fixedIdentity = mcp with
        {
            ExecutablePath = Path.GetFullPath(mcp.ExecutablePath),
            WorkingDirectory = Path.GetFullPath(mcp.WorkingDirectory),
            Arguments = Array.AsReadOnly(mcp.Arguments.ToArray()),
        };
        var serverName = $"filekin_coordination_{mcp.ProjectId:N}";
        var prefix = $"mcp_servers.{serverName}";
        var arguments = new List<string>
        {
            "app-server",
            "--stdio",
        };

        AddOverride(arguments, $"{prefix}.command", TomlString(fixedIdentity.ExecutablePath));
        AddOverride(arguments, $"{prefix}.args", TomlStringArray(fixedIdentity.Arguments));
        AddOverride(arguments, $"{prefix}.cwd", TomlString(fixedIdentity.WorkingDirectory));
        AddOverride(arguments, $"{prefix}.enabled", "true");
        AddOverride(arguments, $"{prefix}.required", "true");
        AddOverride(arguments, $"{prefix}.enabled_tools", TomlStringArray(CoordinationTools));
        AddOverride(arguments, $"{prefix}.disabled_tools", "[]");

        return new CodexAppServerLaunchPlan(
            executablePath,
            Array.AsReadOnly(arguments.ToArray()),
            fixedIdentity);
    }

    private static void AddOverride(
        List<string> arguments,
        string key,
        string tomlValue)
    {
        arguments.Add("--config");
        arguments.Add($"{key}={tomlValue}");
    }

    private static string TomlString(string value) => JsonSerializer.Serialize(value);

    private static string TomlStringArray(IEnumerable<string> values) =>
        $"[{string.Join(',', values.Select(TomlString))}]";
}
