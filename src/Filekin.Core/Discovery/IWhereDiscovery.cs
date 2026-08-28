namespace Filekin.Core.Discovery;

/// <summary>Discovers navigable filesystem locations belonging to one program or tool.</summary>
public interface IWhereDiscovery
{
    Task<WhereDiscoveryOutcome> DiscoverAsync(
        string query,
        IProgress<WhereDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
