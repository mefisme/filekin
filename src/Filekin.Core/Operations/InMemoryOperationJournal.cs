namespace Filekin.Core.Operations;

/// <summary>
/// A journal that lives as long as the application does.
///
/// This is deliberately not the durable store ARCHITECTURE.md calls for. It exists so
/// <c>/unzip</c> can offer <c>[Undo]</c> on its result line the moment extraction finishes, which is
/// when an unwanted extraction is actually noticed — seconds later, not next week. When the SQLite
/// <c>state.db</c> is built, it implements this same interface and the callers do not change.
///
/// The rolling cap matches the retention ARCHITECTURE.md specifies, so behaviour does not shift
/// when the durable store arrives.
/// </summary>
public sealed class InMemoryOperationJournal : IOperationJournal
{
    /// <summary>The rolling retention ARCHITECTURE.md specifies for operation history.</summary>
    public const int RetainedOperations = 50;

    private readonly List<JournalEntry> _entries = [];
    private readonly Lock _gate = new();

    public Task RecordAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > RetainedOperations)
            {
                _entries.RemoveRange(0, _entries.Count - RetainedOperations);
            }
        }

        return Task.CompletedTask;
    }

    public Task<JournalEntry?> MostRecentUndoCandidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                if (_entries[index].CanAttemptUndo)
                {
                    return Task.FromResult<JournalEntry?>(_entries[index]);
                }
            }

            return Task.FromResult<JournalEntry?>(null);
        }
    }

    public Task TransitionUndoAsync(
        Guid id,
        OperationUndoState state,
        string? statusDetail = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            for (var index = 0; index < _entries.Count; index++)
            {
                if (_entries[index].Id == id)
                {
                    _entries[index] = _entries[index].TransitionUndo(state, statusDetail);
                    return Task.CompletedTask;
                }
            }

            throw new KeyNotFoundException($"Operation journal entry '{id:D}' does not exist.");
        }
    }

    public Task<IReadOnlyList<JournalEntry>> RecentAsync(
        int count = RetainedOperations,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var take = Math.Min(count, _entries.Count);
            var recent = new JournalEntry[take];
            for (var index = 0; index < take; index++)
            {
                recent[index] = _entries[_entries.Count - 1 - index];
            }

            return Task.FromResult<IReadOnlyList<JournalEntry>>(recent);
        }
    }
}
