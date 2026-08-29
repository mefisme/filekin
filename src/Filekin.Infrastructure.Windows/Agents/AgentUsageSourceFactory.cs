using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal interface IAgentUsageSourceFactory
{
    IAgentUsageSource Create(AgentProvider provider, string projectFolderPath);
}

internal sealed class NativeAgentUsageSourceFactory : IAgentUsageSourceFactory
{
    public IAgentUsageSource Create(AgentProvider provider, string projectFolderPath) => provider switch
    {
        AgentProvider.Codex => new CodexAgentUsageSource(),
        AgentProvider.ClaudeCode => new ClaudeAgentUsageSource(projectFolderPath),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
