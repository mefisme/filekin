using System.Diagnostics;
using System.IO.Compression;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>
/// Writes the files an <see cref="ArchivePlan"/> describes.
///
/// Two rules make this safe enough to be the default. First, it writes exactly what the plan lists
/// and re-decides nothing, so the preview the user approved is the operation that runs. Second, a
/// file it replaces is not destroyed — the original goes to the Recycle Bin first, which is what lets
/// the whole extraction be undone afterwards and what makes <c>-overwrite</c> a survivable choice.
///
/// Every path it touches is recorded as it goes, including on cancellation, so a half-finished
/// extraction can still be undone completely.
/// </summary>
public sealed class ZipExtractor : IArchiveExtractor
{
    /// <summary>How often progress is published. Often enough to look live, rarely enough to be free.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly IFileSystemOperations _operations;

    public ZipExtractor(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public async Task<ExtractionOutcome> ExtractAsync(
        ArchivePlan plan,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        var replacedOriginals = new List<string>();
        var replacementEvidence = new List<ArchiveReplacementEvidence>();
        var failures = new List<string>();
        var skipped = 0;

        var filesTotal = plan.FileCount;
        var bytesTotal = plan.TotalBytes;
        var filesDone = 0;
        var bytesDone = 0L;
        var lastReport = Stopwatch.StartNew();

        using var archive = ZipFile.OpenRead(plan.ArchivePath);
        var byName = BuildLookup(archive);

        EnsureDirectory(plan.TargetRoot, createdDirectories);

        foreach (var planned in plan.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var absolute = Path.Combine(plan.TargetRoot, planned.RelativeTarget);

            if (planned.IsDirectory)
            {
                TryCreateDirectory(absolute, createdDirectories, failures);
                continue;
            }

            if (!byName.TryGetValue(planned.EntryPath, out var entry))
            {
                failures.Add($"{planned.EntryPath}: no longer in the archive.");
                continue;
            }

            var parent = Path.GetDirectoryName(absolute);
            if (parent is { Length: > 0 } && !TryCreateDirectory(parent, createdDirectories, failures))
            {
                continue;
            }

            if (File.Exists(absolute))
            {
                if (plan.CollisionPolicy == CollisionPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                if (!TryRecycleOriginal(absolute, replacedOriginals, replacementEvidence, failures))
                {
                    continue;
                }
            }

            // Recorded before the write so a cancelled or failed entry still leaves a path undo can
            // clean up, rather than an orphan Filekin no longer remembers making.
            createdFiles.Add(absolute);

            if (!await TryWriteEntryAsync(entry, absolute, failures, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            filesDone++;
            bytesDone += planned.Length;

            if (progress is not null && lastReport.Elapsed >= ProgressInterval)
            {
                lastReport.Restart();
                progress.Report(new ExtractionProgress(
                    filesDone, filesTotal, bytesDone, bytesTotal, planned.RelativeTarget));
            }
        }

        progress?.Report(new ExtractionProgress(
            filesDone, filesTotal, bytesDone, bytesTotal, string.Empty));

        var createdFileEvidence = new List<ArchiveOutputEvidence>();
        foreach (var path in createdFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            createdFileEvidence.Add(
                await ArchiveOutputEvidenceCapture.CaptureAsync(path).ConfigureAwait(false));
        }

        return new ExtractionOutcome(
            plan.ArchivePath,
            plan.TargetRoot,
            createdFiles,
            createdDirectories,
            replacedOriginals,
            skipped,
            failures,
            createdFileEvidence,
            replacementEvidence);
    }

    /// <summary>
    /// Indexes entries by their raw name so the plan can find them again. A malformed archive may
    /// repeat a name; the first wins, matching what the plan was built from.
    /// </summary>
    private static Dictionary<string, ZipArchiveEntry> BuildLookup(ZipArchive archive)
    {
        var byName = new Dictionary<string, ZipArchiveEntry>(archive.Entries.Count, StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            _ = byName.TryAdd(entry.FullName, entry);
        }

        return byName;
    }

    private static async Task<bool> TryWriteEntryAsync(
        ZipArchiveEntry entry,
        string absolute,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (var source = entry.Open())
            await using (var target = new FileStream(
                absolute, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            // Restored after the handle closes, so the archive's own timestamp survives the write.
            File.SetLastWriteTime(absolute, entry.LastWriteTime.LocalDateTime);
            return true;
        }
        catch (OperationCanceledException)
        {
            // The caller stops the loop; the partial file is already recorded for undo.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or InvalidDataException or PathTooLongException)
        {
            failures.Add($"{entry.FullName}: {ex.Message}");
            return false;
        }
    }

    private bool TryRecycleOriginal(
        string absolute,
        List<string> replaced,
        List<ArchiveReplacementEvidence> replacementEvidence,
        List<string> failures)
    {
        try
        {
            var outcome = _operations.Recycle(absolute);
            replaced.Add(absolute);
            replacementEvidence.Add(ArchiveReplacementEvidence.FromRecycleOutcome(outcome));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            failures.Add($"{Path.GetFileName(absolute)}: could not be replaced ({ex.Message}).");
            return false;
        }
    }

    private static bool TryCreateDirectory(string path, List<string> created, List<string> failures)
    {
        try
        {
            EnsureDirectory(path, created);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or PathTooLongException)
        {
            failures.Add($"{path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates <paramref name="path"/> and any missing parents, recording only the folders that did
    /// not already exist. Undo must never remove a folder that was the user's to begin with, so the
    /// distinction matters. Parents are recorded first, which leaves the list shallowest-first.
    /// </summary>
    private static void EnsureDirectory(string path, List<string> created)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is { Length: > 0 })
        {
            EnsureDirectory(parent, created);
        }

        _ = Directory.CreateDirectory(path);
        created.Add(path);
    }
}
