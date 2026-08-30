namespace Filekin.Core.Operations;

/// <summary>
/// One recorded app-owned filesystem operation.
///
/// The payload is JSON rather than a live object on purpose. Keeping every entry as plain data means
/// the memory-backed journal and the durable SQLite journal hold the <em>same</em> rows. A closure or
/// live handle would not survive persistence or app restart.
/// </summary>
/// <param name="Id">Identity, stable across a later move to durable storage.</param>
/// <param name="PerformedAt">When the operation completed.</param>
/// <param name="Kind">The operation family, for example <c>unzip</c>. Selects the undo handler.</param>
/// <param name="Summary">One line for <c>/history</c> and the command-bar result, already written for a human.</param>
/// <param name="PayloadJson">Everything the undo handler needs, serialized.</param>
/// <param name="UndoState">The explicit lifecycle of Undo for this entry.</param>
/// <param name="UndoStatusDetail">A human-readable reason or result for the current Undo state.</param>
public sealed record JournalEntry(
    Guid Id,
    DateTimeOffset PerformedAt,
    string Kind,
    string Summary,
    string PayloadJson,
    OperationUndoState UndoState,
    string? UndoStatusDetail = null)
{
    /// <summary>
    /// Whether this entry remains a candidate for an Undo attempt. Failed and partial attempts are
    /// deliberately retained: an unsuccessful attempt must not silently consume the operation.
    /// Present filesystem safety is evaluated separately before an Undo handler runs.
    /// </summary>
    public bool CanAttemptUndo => UndoState is
        OperationUndoState.Undoable or
        OperationUndoState.UndoFailed or
        OperationUndoState.PartiallyUndone;

    /// <summary>Returns a copy after validating a durable Undo lifecycle transition.</summary>
    public JournalEntry TransitionUndo(
        OperationUndoState nextState,
        string? statusDetail = null)
    {
        if (!CanTransition(UndoState, nextState))
        {
            throw new InvalidOperationException(
                $"Undo cannot transition from {UndoState} to {nextState}.");
        }

        if (RequiresDetail(nextState) && string.IsNullOrWhiteSpace(statusDetail))
        {
            throw new ArgumentException(
                $"Undo state {nextState} requires a status detail.",
                nameof(statusDetail));
        }

        return this with
        {
            UndoState = nextState,
            UndoStatusDetail = string.IsNullOrWhiteSpace(statusDetail) ? null : statusDetail,
        };
    }

    private static bool CanTransition(OperationUndoState currentState, OperationUndoState nextState)
    {
        if (currentState is not (
                OperationUndoState.Undoable or
                OperationUndoState.UndoFailed or
                OperationUndoState.PartiallyUndone))
        {
            return false;
        }

        return nextState is
            OperationUndoState.Unavailable or
            OperationUndoState.Undone or
            OperationUndoState.UndoFailed or
            OperationUndoState.PartiallyUndone;
    }

    private static bool RequiresDetail(OperationUndoState state) => state is
        OperationUndoState.Unavailable or
        OperationUndoState.UndoFailed or
        OperationUndoState.PartiallyUndone;
}
