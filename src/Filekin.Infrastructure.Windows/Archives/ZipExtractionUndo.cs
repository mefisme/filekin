using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>
/// Reverses a completed extraction.
///
/// The order matters and is the whole trick. The new file and the original it replaced share a path,
/// so the new one is deleted first and only then is the original restored from the Recycle Bin —
/// restoring first would land on top of a file that is still there. Folders come last, deepest
/// first, and only when empty, so a folder that already held something of the user's survives.
/// </summary>
public sealed class ZipExtractionUndo : IExtractionUndo
{
    private readonly IRecycleBin _recycleBin;

    public ZipExtractionUndo(IRecycleBin recycleBin)
    {
        ArgumentNullException.ThrowIfNull(recycleBin);
        _recycleBin = recycleBin;
    }

    public Task<string> UndoAsync(ExtractionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // Every step is blocking shell or filesystem work, so the whole reversal is offloaded once
        // rather than interleaving awaits that would each hop threads.
        return Task.Run(() => Undo(outcome, cancellationToken), cancellationToken);
    }

    private string Undo(ExtractionOutcome outcome, CancellationToken cancellationToken)
    {
        var removedFiles = 0;
        var restored = 0;
        var problems = 0;

        foreach (var file in outcome.CreatedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                    removedFiles++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems++;
            }
        }

        restored = RestoreOriginals(outcome, cancellationToken, ref problems);
        RemoveEmptyDirectories(outcome, cancellationToken, ref problems);

        return Describe(outcome, removedFiles, restored, problems);
    }

    /// <summary>
    /// Puts back the files that were replaced. The bin is listed once and matched on original path;
    /// when the same path was deleted more than once, the newest entry is the one this extraction
    /// put there.
    /// </summary>
    private int RestoreOriginals(ExtractionOutcome outcome, CancellationToken cancellationToken, ref int problems)
    {
        if (outcome.ReplacedOriginals.Count == 0)
        {
            return 0;
        }

        var wanted = new HashSet<string>(outcome.ReplacedOriginals, StringComparer.OrdinalIgnoreCase);
        var restored = 0;

        List<RecycledItem> candidates;
        try
        {
            candidates = [.. _recycleBin.List().Where(item => wanted.Contains(item.OriginalPath))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problems += outcome.ReplacedOriginals.Count;
            return 0;
        }

        foreach (var group in candidates.GroupBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var newest = group.OrderByDescending(item => item.DeletedWhen ?? DateTime.MinValue).First();
            try
            {
                if (_recycleBin.Restore(newest))
                {
                    restored++;
                }
                else
                {
                    problems++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problems++;
            }
        }

        return restored;
    }

    private static void RemoveEmptyDirectories(
        ExtractionOutcome outcome,
        CancellationToken cancellationToken,
        ref int problems)
    {
        for (var index = outcome.CreatedDirectories.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folder = outcome.CreatedDirectories[index];
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems++;
            }
        }
    }

    private static string Describe(ExtractionOutcome outcome, int removedFiles, int restored, int problems)
    {
        var parts = new List<string> { $"Undid {outcome.ArchiveName}: removed {Count(removedFiles, "file")}" };

        if (restored > 0)
        {
            parts.Add($"put back {Count(restored, "replaced file")}");
        }

        if (problems > 0)
        {
            parts.Add($"{Count(problems, "item")} could not be reversed");
        }

        return string.Join(", ", parts) + ".";
    }

    private static string Count(int value, string noun) =>
        value == 1 ? $"1 {noun}" : $"{value} {noun}s";
}
