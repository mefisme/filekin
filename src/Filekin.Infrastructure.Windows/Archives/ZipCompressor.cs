using System.Diagnostics;
using System.IO.Compression;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>
/// Writes the archive a <see cref="ZipPlan"/> describes.
///
/// Two decisions keep this honest. The archive is built beside its destination under a temporary
/// name and moved into place only once it is complete, so a cancelled or failed <c>/zip</c> never
/// leaves a truncated file that still looks like a real archive. And an archive it replaces is
/// recycled <em>after</em> the new one is finished, never before — a failure part-way through must
/// not cost the user the file they already had.
/// </summary>
public sealed class ZipCompressor : IArchiveWriter
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly IFileSystemOperations _operations;

    public ZipCompressor(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public async Task<CompressionOutcome> CompressAsync(
        ZipPlan plan,
        IProgress<CompressionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.OutputExists && plan.CollisionPolicy == CollisionPolicy.Skip)
        {
            return new CompressionOutcome(
                plan.OutputPath, 0, 0, 0, null,
                [$"{plan.OutputName} already exists. Use -overwrite to replace it."]);
        }

        var parent = Path.GetDirectoryName(plan.OutputPath);
        if (parent is { Length: > 0 })
        {
            _ = Directory.CreateDirectory(parent);
        }

        var temporary = plan.OutputPath + ".filekin-part";
        var failures = new List<string>();
        var filesDone = 0;
        var bytesDone = 0L;

        try
        {
            await WriteArchiveAsync(plan, temporary, progress, failures, cancellationToken).ConfigureAwait(false);

            filesDone = plan.Entries.Count - failures.Count;
            bytesDone = plan.TotalBytes;

            string? replaced = null;
            if (File.Exists(plan.OutputPath))
            {
                _operations.Recycle(plan.OutputPath);
                replaced = plan.OutputPath;
            }

            File.Move(temporary, plan.OutputPath);

            return new CompressionOutcome(
                plan.OutputPath,
                filesDone,
                bytesDone,
                new FileInfo(plan.OutputPath).Length,
                replaced,
                failures);
        }
        catch
        {
            // A half-built archive is worse than none: it opens, and it lies about what is inside.
            TryDelete(temporary);
            throw;
        }
    }

    private static async Task WriteArchiveAsync(
        ZipPlan plan,
        string temporary,
        IProgress<CompressionProgress>? progress,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var filesDone = 0;
        var bytesDone = 0L;
        var lastReport = Stopwatch.StartNew();

        await using var stream = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var planned in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var entry = archive.CreateEntry(planned.EntryPath, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(planned.SourcePath);

                await using (var source = new FileStream(
                    planned.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
                await using (var target = entry.Open())
                {
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }

                filesDone++;
                bytesDone += planned.Length;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add($"{planned.EntryPath}: {ex.Message}");
            }

            if (progress is not null && lastReport.Elapsed >= ProgressInterval)
            {
                lastReport.Restart();
                progress.Report(new CompressionProgress(
                    filesDone, plan.FileCount, bytesDone, plan.TotalBytes, planned.EntryPath));
            }
        }

        progress?.Report(new CompressionProgress(
            filesDone, plan.FileCount, bytesDone, plan.TotalBytes, string.Empty));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do: the caller is already failing.
        }
    }
}
