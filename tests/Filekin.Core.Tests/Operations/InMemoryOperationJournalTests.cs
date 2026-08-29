using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class InMemoryOperationJournalTests
{
    [TestMethod]
    public async Task MostRecentUndoCandidateSkipsInformationalAndUndoneEntries()
    {
        var journal = new InMemoryOperationJournal();
        var older = Entry("unzip", OperationUndoState.Undoable);
        var informational = Entry("tidy", OperationUndoState.NotUndoable);
        var newer = Entry("zip", OperationUndoState.Undoable);
        await journal.RecordAsync(older);
        await journal.RecordAsync(informational);
        await journal.RecordAsync(newer);

        Assert.AreEqual(newer, await journal.MostRecentUndoCandidateAsync());

        await journal.TransitionUndoAsync(
            newer.Id,
            OperationUndoState.Undone,
            "Removed the created archive.");

        Assert.AreEqual(older, await journal.MostRecentUndoCandidateAsync());
    }

    [TestMethod]
    public async Task FindReturnsTheExactEntryRegardlessOfUndoState()
    {
        var journal = new InMemoryOperationJournal();
        var first = Entry("zip", OperationUndoState.Undoable);
        var second = Entry("tidy", OperationUndoState.NotUndoable);
        await journal.RecordAsync(first);
        await journal.RecordAsync(second);

        Assert.AreEqual(first, await journal.FindAsync(first.Id));
        Assert.AreEqual(second, await journal.FindAsync(second.Id));
        Assert.IsNull(await journal.FindAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task FailedAndPartialUndoAttemptsRemainCandidates()
    {
        var journal = new InMemoryOperationJournal();
        var failed = Entry("move", OperationUndoState.Undoable);
        await journal.RecordAsync(failed);

        await journal.TransitionUndoAsync(
            failed.Id,
            OperationUndoState.UndoFailed,
            "Destination is locked.");

        Assert.AreEqual(
            OperationUndoState.UndoFailed,
            (await journal.MostRecentUndoCandidateAsync())?.UndoState);

        await journal.TransitionUndoAsync(
            failed.Id,
            OperationUndoState.PartiallyUndone,
            "Restored 2 of 3 files; one was skipped.");

        Assert.AreEqual(
            OperationUndoState.PartiallyUndone,
            (await journal.MostRecentUndoCandidateAsync())?.UndoState);
    }

    [TestMethod]
    public async Task UnavailableUndoIsRetainedAsInformationalHistory()
    {
        var journal = new InMemoryOperationJournal();
        var entry = Entry("rename", OperationUndoState.Undoable);
        await journal.RecordAsync(entry);

        await journal.TransitionUndoAsync(
            entry.Id,
            OperationUndoState.Unavailable,
            "Undo is limited to the application session in which the operation ran.");

        Assert.IsNull(await journal.MostRecentUndoCandidateAsync());
        Assert.AreEqual(OperationUndoState.Unavailable, (await journal.RecentAsync())[0].UndoState);
    }

    [TestMethod]
    public async Task RestartDemotesCandidatesAndPreservesAnExistingOutcomeDetail()
    {
        var journal = new InMemoryOperationJournal();
        var available = Entry("rename", OperationUndoState.Undoable);
        var partial = Entry("move", OperationUndoState.PartiallyUndone) with
        {
            UndoStatusDetail = "Restored 2 of 3 files; one was skipped.",
        };
        await journal.RecordAsync(available);
        await journal.RecordAsync(partial);

        await journal.ReconcileAfterRestartAsync();

        var entries = await journal.RecentAsync();
        Assert.IsNull(await journal.MostRecentUndoCandidateAsync());
        Assert.AreEqual(OperationUndoState.Unavailable, entries[0].UndoState);
        Assert.AreEqual("Restored 2 of 3 files; one was skipped.", entries[0].UndoStatusDetail);
        Assert.AreEqual(OperationUndoState.Unavailable, entries[1].UndoState);
        Assert.AreEqual(
            OperationJournalPolicy.PreviousSessionUndoUnavailableDetail,
            entries[1].UndoStatusDetail);
    }

    [TestMethod]
    public async Task TerminalUndoStatesRejectFurtherTransitions()
    {
        var journal = new InMemoryOperationJournal();
        var entry = Entry("rename", OperationUndoState.Undoable);
        await journal.RecordAsync(entry);
        await journal.TransitionUndoAsync(
            entry.Id,
            OperationUndoState.Undone,
            "Restored the original name.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => journal.TransitionUndoAsync(
                entry.Id,
                OperationUndoState.UndoFailed,
                "A retry should not start."));
    }

    [TestMethod]
    public async Task FailurePartialAndUnavailableStatesRequireAnExplanation()
    {
        var journal = new InMemoryOperationJournal();
        var entry = Entry("move", OperationUndoState.Undoable);
        await journal.RecordAsync(entry);

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.TransitionUndoAsync(entry.Id, OperationUndoState.UndoFailed));
    }

    [TestMethod]
    public async Task TransitioningAnUnknownEntryFailsClosed()
    {
        var journal = new InMemoryOperationJournal();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => journal.TransitionUndoAsync(Guid.NewGuid(), OperationUndoState.Undone));
    }

    [TestMethod]
    public async Task RecentIsNewestFirstAndHonoursTheRequestedCount()
    {
        var journal = new InMemoryOperationJournal();
        var first = Entry("first", OperationUndoState.NotUndoable);
        var second = Entry("second", OperationUndoState.NotUndoable);
        var third = Entry("third", OperationUndoState.NotUndoable);
        await journal.RecordAsync(first);
        await journal.RecordAsync(second);
        await journal.RecordAsync(third);

        CollectionAssert.AreEqual(new[] { third, second }, (await journal.RecentAsync(2)).ToArray());
    }

    [TestMethod]
    public async Task RecordingBeyondTheRollingLimitDropsOnlyTheOldestEntries()
    {
        var journal = new InMemoryOperationJournal();
        var entries = Enumerable.Range(0, InMemoryOperationJournal.RetainedOperations + 3)
            .Select(index => Entry(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OperationUndoState.Undoable))
            .ToArray();

        foreach (var entry in entries)
        {
            await journal.RecordAsync(entry);
        }

        var recent = await journal.RecentAsync();
        Assert.HasCount(InMemoryOperationJournal.RetainedOperations, recent);
        Assert.AreEqual(entries[^1], recent[0]);
        Assert.AreEqual(entries[3], recent[^1]);
    }

    private static JournalEntry Entry(string kind, OperationUndoState undoState) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, kind, kind, "{}", undoState);
}
