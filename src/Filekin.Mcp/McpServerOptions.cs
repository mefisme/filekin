using Filekin.Core.Agents;

namespace Filekin.Mcp;

public sealed record McpServerOptions(Guid ProjectId, AgentProvider Provider, string StateDatabasePath)
{
    public static McpServerOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? project = null;
        string? provider = null;
        string? stateDatabase = null;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for '{option}'.", nameof(args));
            }

            var value = args[++index];
            switch (option)
            {
                case "--project":
                    project = SetOnce(project, value, option);
                    break;
                case "--provider":
                    provider = SetOnce(provider, value, option);
                    break;
                case "--state-db":
                    stateDatabase = SetOnce(stateDatabase, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.", nameof(args));
            }
        }

        if (!Guid.TryParse(project, out var projectId) || projectId == Guid.Empty)
        {
            throw new ArgumentException("--project requires a non-empty GUID.", nameof(args));
        }

        var agentProvider = provider?.ToLowerInvariant() switch
        {
            "codex" => AgentProvider.Codex,
            "claude" or "claude-code" => AgentProvider.ClaudeCode,
            _ => throw new ArgumentException(
                "--provider must be 'codex' or 'claude'.",
                nameof(args)),
        };

        if (stateDatabase is not null && !Path.IsPathFullyQualified(stateDatabase))
        {
            throw new ArgumentException("--state-db requires a fully qualified path.", nameof(args));
        }

        var databasePath = stateDatabase is null
            ? Infrastructure.Windows.Agents.SqliteAgentProjectStore.DefaultDatabasePath
            : Path.GetFullPath(stateDatabase);

        return new McpServerOptions(projectId, agentProvider, databasePath);
    }

    private static string SetOnce(string? currentValue, string value, string option)
    {
        if (currentValue is not null)
        {
            throw new ArgumentException($"Option '{option}' may only be supplied once.");
        }

        return value;
    }
}
