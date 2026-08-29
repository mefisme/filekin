namespace Filekin.Core.Agents;

/// <summary>Reads non-secret quota state from one provider's supported local interface.</summary>
public interface IAgentUsageSource
{
    AgentProvider Provider { get; }

    Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(CancellationToken cancellationToken = default);
}
