using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class InMemoryOperationJournalTests
{
    [TestMethod]
    public void MostRecentUndoableSkipsInformationalAndUndoneEntries()
    {
        var journal = new InMemoryOperationJournal();
        var older = Entry("unzip", canUndo: true);
        var informational = Entry("tidy", canUndo: false);
        var newer = Entry("zip", canUndo: true);
        journal.Record(older);
        journal.Record(informational);
        journal.Record(newer);

        Assert.AreEqual(newer, journal.MostRecentUndoable());

        journal.MarkUndone(newer.Id);

        Assert.AreEqual(older, journal.MostRecentUndoable());
    }

    [TestMethod]
    public void RecentIsNewestFirstAndHonoursTheRequestedCount()
    {
        var journal = new InMemoryOperationJournal();
        var first = Entry("first", canUndo: false);
        var second = Entry("second", canUndo: false);
        var third = Entry("third", canUndo: false);
        journal.Record(first);
        journal.Record(second);
        journal.Record(third);

        CollectionAssert.AreEqual(new[] { third, second }, journal.Recent(2).ToArray());
    }

    [TestMethod]
    public void RecordingBeyondTheRollingLimitDropsOnlyTheOldestEntries()
    {
        var journal = new InMemoryOperationJournal();
        var entries = Enumerable.Range(0, InMemoryOperationJournal.RetainedOperations + 3)
            .Select(index => Entry(index.ToString(System.Globalization.CultureInfo.InvariantCulture), canUndo: true))
            .ToArray();

        foreach (var entry in entries)
        {
            journal.Record(entry);
        }

        var recent = journal.Recent();
        Assert.HasCount(InMemoryOperationJournal.RetainedOperations, recent);
        Assert.AreEqual(entries[^1], recent[0]);
        Assert.AreEqual(entries[3], recent[^1]);
    }

    private static JournalEntry Entry(string kind, bool canUndo) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, kind, kind, "{}", canUndo);
}
