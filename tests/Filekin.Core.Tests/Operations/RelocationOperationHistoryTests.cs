using System.Text.Json;
using Filekin.Core.Commands.App;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class RelocationOperationHistoryTests
{
    [TestMethod]
    public void MoveMapsItsAuthoritativeRelocations()
    {
        var relocations = new[]
        {
            new PathRelocation(@"D:\Work\a.txt", @"D:\Work\out\a.txt"),
            new PathRelocation(@"D:\Work\b.txt", @"D:\Work\out\b.txt"),
        };
        var result = AppCommandResult.Ok("Moved 2 items", relocations.Select(item => item.DestinationPath).ToArray(), relocations);

        var history = RelocationOperationHistory.TryCreate(
            new AppCommandExecutionDetail("move", result));

        Assert.IsNotNull(history);
        Assert.AreEqual("move", history.Kind);
        Assert.AreEqual(result.Message, history.Summary);
        CollectionAssert.AreEqual(relocations, history.Payload.Relocations.ToArray());
    }

    [TestMethod]
    public void PartialMoveRemainsOnePayloadWithCommandFailures()
    {
        var relocation = new PathRelocation(@"D:\Work\a.txt", @"D:\Work\out\a.txt");
        var failure = new AppCommandFailure(@"D:\Work\b.txt", "The file is in use.");
        var result = AppCommandResult.Partial(
            "1 moved · 1 failed",
            [relocation.DestinationPath],
            [relocation],
            [failure]);

        var history = RelocationOperationHistory.TryCreate(
            new AppCommandExecutionDetail("move", result));

        Assert.IsNotNull(history);
        Assert.HasCount(1, history.Payload.Relocations);
        Assert.HasCount(1, history.Payload.Failures);
        Assert.AreEqual(failure, history.Payload.Failures[0]);
    }

    [TestMethod]
    public void RenameMapsAsItsOwnKind()
    {
        var relocation = new PathRelocation(@"D:\Work\a.txt", @"D:\Work\b.txt");
        var result = AppCommandResult.Ok("Renamed a.txt → b.txt", [relocation.DestinationPath], [relocation]);

        var history = RelocationOperationHistory.TryCreate(
            new AppCommandExecutionDetail("rename", result));

        Assert.IsNotNull(history);
        Assert.AreEqual("rename", history.Kind);
    }

    [TestMethod]
    public void PayloadRoundTripsThroughDurableJson()
    {
        var first = new PathRelocation(@"D:\Work\a.txt", @"D:\Work\out\a.txt");
        var second = new PathRelocation(@"D:\Work\b.txt", @"D:\Work\out\b.txt");
        var payload = new RelocationOperationPayload(
            [first, second],
            [new AppCommandFailure(@"D:\Work\c.txt", "The file is in use.")],
            [second]);

        var restored = JsonSerializer.Deserialize<RelocationOperationPayload>(
            JsonSerializer.Serialize(payload));

        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(payload.Relocations.ToArray(), restored.Relocations.ToArray());
        CollectionAssert.AreEqual(payload.Failures.ToArray(), restored.Failures.ToArray());
        CollectionAssert.AreEqual(
            payload.PendingRelocations.ToArray(),
            restored.PendingRelocations.ToArray());
    }

    [TestMethod]
    public void ErrorWithoutKnownRelocationDoesNotMap()
    {
        var history = RelocationOperationHistory.TryCreate(
            new AppCommandExecutionDetail("move", AppCommandResult.FailedWhileWriting("Move failed.")));

        Assert.IsNull(history);
    }

    [TestMethod]
    public void CopyDoesNotMapAsRelocationHistory()
    {
        var history = RelocationOperationHistory.TryCreate(
            new AppCommandExecutionDetail(
                "copy",
                AppCommandResult.Ok("Copied a.txt", @"D:\Work\out\a.txt")));

        Assert.IsNull(history);
    }
}
