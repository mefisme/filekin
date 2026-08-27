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
    void Record(JournalEntry entry);

    /// <summary>
    /// The most recent entry that an undo handler can still reverse, or <c>null</c>. Does not
    /// remove it: undo may fail, and a failed undo must not silently consume the entry.
    /// </summary>
    JournalEntry? MostRecentUndoable();

    /// <summary>Marks <paramref name="id"/> as no longer undoable, after a successful undo.</summary>
    void MarkUndone(Guid id);

    /// <summary>
    /// The most recent entries, newest first, capped at <paramref name="count"/>. ARCHITECTURE.md
    /// sets the retained history at a rolling 50 operations.
    /// </summary>
    IReadOnlyList<JournalEntry> Recent(int count = 50);
}
