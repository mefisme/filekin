namespace Filekin.Core.Operations;

/// <summary>The durable lifecycle of Undo for one app-owned filesystem operation.</summary>
public enum OperationUndoState
{
    /// <summary>The operation is history-only and has never supported Undo.</summary>
    NotUndoable = 0,

    /// <summary>The operation belongs to this session and may currently be reversed.</summary>
    Undoable = 1,

    /// <summary>Undo is no longer offered, for example because the application restarted.</summary>
    Unavailable = 2,

    /// <summary>The operation was reversed completely.</summary>
    Undone = 3,

    /// <summary>The most recent Undo attempt failed before completing and may be retried safely.</summary>
    UndoFailed = 4,

    /// <summary>Undo reversed only part of the operation and may have remaining reversible work.</summary>
    PartiallyUndone = 5,
}
