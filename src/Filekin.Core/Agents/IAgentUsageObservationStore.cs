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
    /// provider. Returns <see langword="false"/> when an equal or newer observation already exists, so
    /// an out-of-order helper process cannot replace fresher facts with older ones.
    /// </summary>
    /// <param name="reportingProjectId">
    /// The project whose session reported this. It is where the reading came from, and it must exist,
    /// but it does not scope the reading: usage belongs to the account, so a second project does not
    /// get a second copy of the same fact.
    /// </param>
    Task<bool> RecordUsageObservationAsync(
        Guid reportingProjectId,
        AgentUsageSnapshot observation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what this provider last reported about the account, or <see langword="null"/> when it has
    /// reported nothing. One reading serves every project, because a five-hour window is spent by every
    /// session on the machine, not by one folder.
    /// </summary>
    Task<AgentUsageSnapshot?> ReadUsageObservationAsync(
        AgentProvider provider,
        CancellationToken cancellationToken = default);
}
