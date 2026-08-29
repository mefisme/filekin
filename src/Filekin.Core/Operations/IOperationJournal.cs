namespace Filekin.Core.Operations;

/// <summary>
/// The record of what Filekin itself changed on disk.
///
/// This is the seam <c>/history</c> and <c>/undo</c> will sit on. Only operations Filekin owns and
/// performed are recorded; ordinary shell commands are not, because their side effects cannot be
/// inferred or reversed (ARCHITECTURE.md — Topic 4A).
/// </summary>
public interface IOperationJournal
{
    /// <summary>Adds <paramref name="entry"/> as the most recent operation.</summary>
    Task RecordAsync(JournalEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent entry whose lifecycle still permits an Undo attempt, or <c>null</c>. The Undo
    /// coordinator must reevaluate present filesystem safety before running a handler. Reading the
    /// candidate does not remove it: a failed attempt must not silently consume the entry.
    /// </summary>
    Task<JournalEntry?> MostRecentUndoCandidateAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds one retained operation by its stable identity, or <c>null</c>.</summary>
    Task<JournalEntry?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transactionally records an Undo result or makes an Undo candidate unavailable. Implementations
    /// must reject invalid lifecycle transitions and unknown entry identities.
    /// </summary>
    Task TransitionUndoAsync(
        Guid id,
        OperationUndoState state,
        string? statusDetail = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent entries, newest first, capped at <paramref name="count"/>. ARCHITECTURE.md
    /// sets the retained history at a rolling 50 operations.
    /// </summary>
    Task<IReadOnlyList<JournalEntry>> RecentAsync(
        int count = OperationJournalPolicy.RetainedOperations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every persisted Undo promise left by an earlier application process while retaining
    /// the operation as informational history. The application calls this once during startup.
    /// </summary>
    Task ReconcileAfterRestartAsync(CancellationToken cancellationToken = default);
}
