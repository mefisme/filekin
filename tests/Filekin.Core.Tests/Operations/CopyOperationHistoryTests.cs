using System.Text.Json;
using Filekin.Core.Commands.App;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class CopyOperationHistoryTests
{
    [TestMethod]
    public void SuccessfulCopyMapsTheAuthoritativeCreatedPaths()
    {
        var result = AppCommandResult.Ok(
            @"Copied 2 items → D:\Work\out",
            [@"D:\Work\out\a.txt", @"D:\Work\out\b.txt"],
            []);

        var history = CopyOperationHistory.TryCreate(
            new AppCommandExecutionDetail("copy", result));

        Assert.IsNotNull(history);
        Assert.AreEqual(result.Message, history.Summary);
        CollectionAssert.AreEqual(
            result.AffectedPaths.ToArray(),
            history.Payload.CreatedPaths.ToArray());
        Assert.IsEmpty(history.Payload.Failures);
    }

    [TestMethod]
    public void PartialCopyRemainsOneRecordWithSuccessesAndFailures()
    {
        var failure = new AppCommandFailure(@"D:\Work\b.txt", "Destination already exists.");
        var result = AppCommandResult.Partial(
            "1 copied · 1 failed",
            [@"D:\Work\out\a.txt"],
            [],
            [failure]);

        var history = CopyOperationHistory.TryCreate(
            new AppCommandExecutionDetail("copy", result));

        Assert.IsNotNull(history);
        Assert.HasCount(1, history.Payload.CreatedPaths);
        Assert.HasCount(1, history.Payload.Failures);
        Assert.AreEqual(failure, history.Payload.Failures[0]);
    }

    [TestMethod]
    public void PayloadRoundTripsThroughDurableJson()
    {
        var payload = new CopyOperationPayload(
            [@"D:\Work\out\a.txt"],
            [new AppCommandFailure(@"D:\Work\b.txt", "Destination already exists.")]);

        var restored = JsonSerializer.Deserialize<CopyOperationPayload>(
            JsonSerializer.Serialize(payload));

        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(payload.CreatedPaths.ToArray(), restored.CreatedPaths.ToArray());
        CollectionAssert.AreEqual(payload.Failures.ToArray(), restored.Failures.ToArray());
    }

    [TestMethod]
    public void CopyRefusalDoesNotCreateHistory()
    {
        var history = CopyOperationHistory.TryCreate(
            new AppCommandExecutionDetail("copy", AppCommandResult.Fail("Destination already exists.")));

        Assert.IsNull(history);
    }

    [TestMethod]
    public void UnknownWriteWithoutKnownSuccessDoesNotCreateHistory()
    {
        var history = CopyOperationHistory.TryCreate(
            new AppCommandExecutionDetail("copy", AppCommandResult.FailedWhileWriting("Copy failed.")));

        Assert.IsNull(history);
    }

    [TestMethod]
    public void AnotherAppCommandDoesNotCreateCopyHistory()
    {
        var history = CopyOperationHistory.TryCreate(
            new AppCommandExecutionDetail(
                "move",
                AppCommandResult.Ok("Moved a.txt", @"D:\Work\out\a.txt")));

        Assert.IsNull(history);
    }
}
