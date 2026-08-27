using System.Diagnostics;
using Filekin.Core.Inspection;

namespace Filekin.Infrastructure.Windows.Inspection;

/// <summary>
/// Walks a folder or selection and totals size and item counts for the Info sheet.
///
/// Three rules keep the answer honest and the app responsive (DECISIONS.md, 2026-08-27):
/// reparse points are never followed, so a junction cannot make the walk count the same tree twice
/// or loop forever; a folder that refuses access is recorded rather than hidden, so a partial total
/// can say it is partial; and progress is reported on a timer rather than per file, so a tree with a
/// million entries does not flood the dispatcher with a million updates.
/// </summary>
public sealed class DirectoryAggregateScanner : IAggregateScanner
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    // IgnoreInaccessible is deliberately off: skipping silently would let /info present a total that
    // quietly omits whole trees. Filekin would rather say so.
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
    };

    public AggregateTotals Scan(
        IReadOnlyList<string> roots,
        bool countRootFoldersThemselves,
        Action<AggregateTotals> onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(onProgress);

        long bytes = 0;
        var files = 0;
        var folders = 0;
        var unreadable = false;

        var pending = new Stack<string>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(root))
                {
                    if (countRootFoldersThemselves)
                    {
                        folders++;
                    }

                    pending.Push(root);
                }
                else if (File.Exists(root))
                {
                    files++;
                    bytes += new FileInfo(root).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable = true;
            }
        }

        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        // A junction, symlink, or cloud placeholder recall point. Count the link
                        // itself, never what it points at.
                        files++;
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        folders++;
                        pending.Push(entry.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        files++;
                        bytes += file.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable = true;
            }

            if (clock.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = clock.Elapsed;
                onProgress(new AggregateTotals(bytes, files, folders, IsComplete: false, unreadable));
            }
        }

        return new AggregateTotals(bytes, files, folders, IsComplete: true, unreadable);
    }
}
