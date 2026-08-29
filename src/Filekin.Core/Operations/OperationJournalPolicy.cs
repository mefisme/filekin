namespace Filekin.Core.Operations;

/// <summary>Confirmed retention and session-safety rules shared by operation journal stores.</summary>
public static class OperationJournalPolicy
{
    public const int RetainedOperations = 50;

    public const string PreviousSessionUndoUnavailableDetail =
        "Undo is available only in the Filekin session that performed the operation.";
}
