using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal interface IAgentUsageSourceFactory
{
    IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath);
}

internal sealed class NativeAgentUsageSourceFactory : IAgentUsageSourceFactory
{
    private readonly Lazy<IAgentUsageSource> _codex = new(
        static () => new CodexAgentUsageSource(),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IAgentUsageObservationStore _observations;

    public NativeAgentUsageSourceFactory(IAgentUsageObservationStore observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        _observations = observations;
    }

    public IAgentUsageSource Create(AgentProvider provider, Guid projectId, string projectFolderPath) =>
        provider switch
        {
            // Codex allowance is account-level. One inspection App Server can safely multiplex
            // requests for every project, so creating one process per saved folder only makes
            // refresh and shutdown slower without producing different facts.
            AgentProvider.Codex => _codex.Value,
            AgentProvider.ClaudeCode => new ClaudeAgentUsageSource(
                _observations,
                projectFolderPath),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
}
