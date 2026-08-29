using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Fixed process identity for one project-scoped Filekin MCP server. Producing this value does not
/// start a process or grant a working-tree lease.
/// </summary>
public sealed record AgentMcpLaunchConfiguration(
    AgentProvider Provider,
    Guid ProjectId,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal interface IAgentMcpLaunchConfigurationFactory
{
    AgentMcpLaunchConfiguration Create(AgentProjectState project, AgentProvider provider);
}

internal sealed class AgentMcpLaunchConfigurationFactory : IAgentMcpLaunchConfigurationFactory
{
    private readonly string _executablePath;
    private readonly string _stateDatabasePath;

    public AgentMcpLaunchConfigurationFactory(string executablePath, string stateDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDatabasePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The Filekin MCP executable path must be fully qualified.", nameof(executablePath));
        }

        if (!Path.IsPathFullyQualified(stateDatabasePath))
        {
            throw new ArgumentException("The state database path must be fully qualified.", nameof(stateDatabasePath));
        }

        _executablePath = Path.GetFullPath(executablePath);
        _stateDatabasePath = Path.GetFullPath(stateDatabasePath);
    }

    public AgentMcpLaunchConfiguration Create(AgentProjectState project, AgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(project);
        var providerArgument = provider switch
        {
            AgentProvider.Codex => "codex",
            AgentProvider.ClaudeCode => "claude",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        return new AgentMcpLaunchConfiguration(
            provider,
            project.Id,
            _executablePath,
            project.FolderPath,
            Array.AsReadOnly(new[]
            {
                "--project",
                project.Id.ToString("D"),
                "--provider",
                providerArgument,
                "--state-db",
                _stateDatabasePath,
            }));
    }
}
