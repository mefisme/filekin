using System.Text.Json;
using Filekin.Core.Commands.App;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Tests.Operations;

[TestClass]
public sealed class TossOperationHistoryTests
{
    [TestMethod]
    public void ExactTossIdentityCreatesOneRestorablePayload()
    {
        var result = AppCommandResult.Recycled(
            "Moved 2 items to the Recycle Bin",
            [Exact(@"D:\Work\a.txt", "bin:a"), Exact(@"D:\Work\folder", "bin:folder", isDirectory: true)]);

        var history = TossOperationHistory.TryCreate(new AppCommandExecutionDetail("toss", result));

        Assert.IsNotNull(history);
        Assert.IsTrue(history.CanRestore);
        Assert.IsNull(history.RestoreUnavailableReason);
        Assert.HasCount(2, history.Payload.Items);
        Assert.AreEqual("bin:a", history.Payload.Items[0].RecycleBinIdentity);
        Assert.IsTrue(history.Payload.Items[1].IsDirectory);
    }

    [TestMethod]
    public void PartialAliasBatchStaysOnePayloadWithItsFailure()
    {
        var failure = new AppCommandFailure(@"D:\Work\missing.txt", "Target not found");
        var result = AppCommandResult.Recycled(
            "1 moved to the Recycle Bin · 1 failed",
            [Exact(@"D:\Work\a.txt", "bin:a")],
            [failure]);

        var history = TossOperationHistory.TryCreate(new AppCommandExecutionDetail("delete", result));

        Assert.IsNotNull(history);
        Assert.IsTrue(history.CanRestore);
        Assert.HasCount(1, history.Payload.Items);
        Assert.HasCount(1, history.Payload.Failures);
        Assert.AreEqual(failure, history.Payload.Failures[0]);
    }

    [TestMethod]
    public void MissingExactIdentityCreatesInformationalHistoryWithReason()
    {
        var outcome = RecycleOutcome.Informational(
            @"D:\Work\a.txt",
            FileSystemEntryKind.File,
            "Windows did not return an exact item.");
        var result = AppCommandResult.Recycled("Moved a.txt to the Recycle Bin", [outcome]);

        var history = TossOperationHistory.TryCreate(new AppCommandExecutionDetail("trash", result));

        Assert.IsNotNull(history);
        Assert.IsFalse(history.CanRestore);
        StringAssert.Contains(history.RestoreUnavailableReason, "Restore is unavailable");
        StringAssert.Contains(history.RestoreUnavailableReason, "exact item");
        Assert.AreEqual(
            "Windows did not return an exact item.",
            history.Payload.Items[0].RestoreUnavailableReason);
    }

    [TestMethod]
    public void OneUnidentifiedSuccessMakesTheWholeInvocationInformational()
    {
        var result = AppCommandResult.Recycled(
            "Moved 2 items to the Recycle Bin",
            [
                Exact(@"D:\Work\a.txt", "bin:a"),
                RecycleOutcome.Informational(
                    @"D:\Work\b.txt",
                    FileSystemEntryKind.File,
                    "No exact item was returned."),
            ]);

        var history = TossOperationHistory.TryCreate(new AppCommandExecutionDetail("toss", result));

        Assert.IsNotNull(history);
        Assert.IsFalse(history.CanRestore);
        Assert.HasCount(2, history.Payload.Items);
    }

    [TestMethod]
    public void TossPayloadRoundTripsThroughJsonWithoutLosingOpaqueIdentity()
    {
        var payload = new TossOperationPayload(
            [
                new TossedItem(@"D:\Work\a.txt", "a.txt", false, "bin:a", null),
                new TossedItem(
                    @"D:\Work\folder",
                    "folder",
                    true,
                    null,
                    "Windows permanently deleted this item."),
            ],
            [new AppCommandFailure(@"D:\Work\missing.txt", "Target not found")]);

        var roundTripped = JsonSerializer.Deserialize<TossOperationPayload>(JsonSerializer.Serialize(payload));

        Assert.IsNotNull(roundTripped);
        CollectionAssert.AreEqual(payload.Items.ToArray(), roundTripped.Items.ToArray());
        CollectionAssert.AreEqual(payload.Failures.ToArray(), roundTripped.Failures.ToArray());
        Assert.AreEqual("bin:a", roundTripped.Items[0].ToRecycledItem().RecycleBinIdentity);
    }

    [TestMethod]
    public void UnrelatedCommandDoesNotCreateTossHistory()
    {
        var result = AppCommandResult.Ok("Copied a.txt", @"D:\Work\a.txt");

        var history = TossOperationHistory.TryCreate(new AppCommandExecutionDetail("copy", result));

        Assert.IsNull(history);
    }

    private static RecycleOutcome Exact(string path, string identity, bool isDirectory = false) =>
        RecycleOutcome.Restorable(new RecycledItem(
            Path.GetFileName(path),
            path,
            DateTime.Now,
            SizeBytes: null,
            isDirectory,
            identity));
}
