namespace Filekin.Core.Operations;

/// <summary>
/// One recorded app-owned filesystem operation.
///
/// The payload is JSON rather than a live object on purpose. ARCHITECTURE.md specifies a durable
/// rolling history in an embedded SQLite <c>state.db</c>, and that store does not exist yet. Keeping
/// every entry as plain data means the memory-backed journal shipped today and the SQLite journal
/// built later hold the <em>same</em> rows: swapping the implementation is additive rather than a
/// rewrite. A closure or a live handle would not survive that move.
/// </summary>
/// <param name="Id">Identity, stable across a later move to durable storage.</param>
/// <param name="PerformedAt">When the operation completed.</param>
/// <param name="Kind">The operation family, for example <c>unzip</c>. Selects the undo handler.</param>
/// <param name="Summary">One line for <c>/history</c> and the command-bar result, already written for a human.</param>
/// <param name="PayloadJson">Everything the undo handler needs, serialized.</param>
/// <param name="CanUndo">Whether an undo handler can reverse this entry.</param>
public sealed record JournalEntry(
    Guid Id,
    DateTimeOffset PerformedAt,
    string Kind,
    string Summary,
    string PayloadJson,
    bool CanUndo);
