using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal interface IAgentUsageSourceFactory
{
    IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath);
}

internal sealed class NativeAgentUsageSourceFactory : IAgentUsageSourceFactory
{
    private readonly IAgentUsageObservationStore _observations;

    public NativeAgentUsageSourceFactory(IAgentUsageObservationStore observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        _observations = observations;
    }

    public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath) =>
        provider switch
        {
            AgentProvider.Codex => new CodexAgentUsageSource(),
            AgentProvider.ClaudeCode => new ClaudeAgentUsageSource(
                _observations,
                projectId,
                projectFolderPath),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
}
