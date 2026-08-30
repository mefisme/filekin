using System.Text.Json;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Tests.Archives;

[TestClass]
public sealed class ArchiveUndoEvidenceTests
{
    [TestMethod]
    public void CapturedOutputEvidenceRoundTripsThroughExtractionPayload()
    {
        var output = ArchiveOutputEvidence.Captured(
            @"D:\Work\a.txt",
            length: 42,
            new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc),
            "AABBCC");
        var replacement = ExactReplacement(@"D:\Work\a.txt", "bin:a");
        var outcome = new ExtractionOutcome(
            "archive.zip",
            @"D:\Work",
            [output.Path],
            [],
            [replacement.OriginalPath],
            0,
            [],
            [output],
            [replacement]);

        var roundTripped = JsonSerializer.Deserialize<ExtractionOutcome>(JsonSerializer.Serialize(outcome));

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(output, roundTripped.CreatedFileEvidence.Single());
        Assert.AreEqual(replacement, roundTripped.ReplacementEvidence.Single());
    }

    [TestMethod]
    public void CompressionPayloadRetainsOutputAndReplacementEvidence()
    {
        var output = ArchiveOutputEvidence.Captured(
            @"D:\Work\archive.zip",
            length: 100,
            new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc),
            "DDEEFF");
        var replacement = ExactReplacement(output.Path, "bin:archive");
        var outcome = new CompressionOutcome(
            output.Path,
            filesStored: 3,
            bytesRead: 50,
            archiveBytes: 100,
            output.Path,
            [],
            output,
            replacement);

        var roundTripped = JsonSerializer.Deserialize<CompressionOutcome>(JsonSerializer.Serialize(outcome));

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(output, roundTripped.OutputEvidence);
        Assert.AreEqual(replacement, roundTripped.ReplacementEvidence);
    }

    [TestMethod]
    public void AbsentOutputEvidenceDoesNotPretendAFileWasWritten()
    {
        var evidence = ArchiveOutputEvidence.Absent(@"D:\Work\partial.txt");

        Assert.IsTrue(evidence.CanVerify);
        Assert.AreEqual(false, evidence.ExistedAtCompletion);
        Assert.IsNull(evidence.Sha256);
    }

    [TestMethod]
    public void UnavailableEvidenceCarriesAnExplicitReason()
    {
        var output = ArchiveOutputEvidence.Unavailable(@"D:\Work\a.txt", "File was locked.");
        var replacement = new ArchiveReplacementEvidence(
            @"D:\Work\old.txt",
            recycledItem: null,
            "Windows did not expose an exact identity.");

        Assert.IsFalse(output.CanVerify);
        Assert.AreEqual("File was locked.", output.UnavailableReason);
        Assert.IsFalse(replacement.CanRestore);
        StringAssert.Contains(replacement.RestoreUnavailableReason, "exact identity");
    }

    private static ArchiveReplacementEvidence ExactReplacement(string originalPath, string identity) =>
        new(
            originalPath,
            new RecycledItem(
                Path.GetFileName(originalPath),
                originalPath,
                DateTime.Now,
                SizeBytes: 12,
                IsDirectory: false,
                identity),
            restoreUnavailableReason: null);
}
