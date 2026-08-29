using Filekin.Core.Archives;

namespace Filekin.Core.Tests.Archives;

[TestClass]
public sealed class ExtractionBatchOutcomeTests
{
    [TestMethod]
    public void AggregatesEveryArchiveInOneUserOperation()
    {
        var batch = new ExtractionBatchOutcome(
        [
            new ExtractionOutcome("one.zip", "target", ["a", "b"], [], [], 1, ["one failed"]),
            new ExtractionOutcome("two.zip", "target", ["c"], ["folder"], [], 2, ["two failed"]),
        ]);

        Assert.IsTrue(batch.WroteAnything);
        Assert.AreEqual(3, batch.CreatedFileCount);
        Assert.AreEqual(3, batch.SkippedCount);
        string[] expectedFailures = ["one failed", "two failed"];
        CollectionAssert.AreEqual(expectedFailures, batch.Failures.ToArray());
    }

    [TestMethod]
    public void EmptyBatchDidNotWriteAnything()
    {
        var batch = new ExtractionBatchOutcome();

        Assert.IsFalse(batch.WroteAnything);
        Assert.AreEqual(0, batch.CreatedFileCount);
        Assert.AreEqual(0, batch.SkippedCount);
        Assert.AreEqual(0, batch.Failures.Count);
    }
}
