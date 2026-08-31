namespace Filekin.Core.Agents;

/// <summary>
/// Transactional storage for the newest non-secret quota observation a provider's own local interface
/// reported for one project. An observation carries quota windows only: it never carries lease, turn,
/// or native session identity, so a short-lived provider-side helper process can record one without
/// being able to change coordination state.
/// </summary>
public interface IAgentUsageObservationStore
{
    /// <summary>
    /// Stores <paramref name="observation"/> when it is newer than the stored observation for the same
    /// project and provider. Returns <see langword="false"/> when an equal or newer observation already
    /// exists, so an out-of-order helper process cannot replace fresher facts with older ones.
    /// </summary>
    Task<bool> RecordUsageObservationAsync(
        Guid projectId,
        AgentUsageSnapshot observation,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the stored observation, or <see langword="null"/> when the provider reported none.</summary>
    Task<AgentUsageSnapshot?> ReadUsageObservationAsync(
        Guid projectId,
        AgentProvider provider,
        CancellationToken cancellationToken = default);
}
