namespace Filekin.Core.Agents;

/// <summary>Transactional persistence boundary for app-owned agent coordination state.</summary>
public interface IAgentProjectStore
{
    Task SaveAsync(AgentProjectState state, CancellationToken cancellationToken = default);

    Task<AgentProjectState?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<AgentProjectState?> LoadByFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentProjectState>> LoadAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads, transitions, and saves one project inside one storage transaction. Implementations must
    /// serialize concurrent writers so two MCP processes cannot overwrite each other's state.
    /// </summary>
    Task<AgentProjectState> UpdateAsync(
        Guid projectId,
        Func<AgentProjectState, AgentProjectState> transition,
        CancellationToken cancellationToken = default);

    /// <summary>Clears every unverified persisted writer lease before native session reconciliation.</summary>
    Task<IReadOnlyList<AgentProjectState>> ReconcileAfterRestartAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes one project's app-owned coordination memory: the project row and everything
    /// that cascades from it (participants, lease, messages, handoffs, usage windows). Answers whether
    /// a row was actually there to delete. Account-scoped usage observations are untouched, because
    /// they describe the provider account, not this folder.
    /// </summary>
    Task<bool> RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}
