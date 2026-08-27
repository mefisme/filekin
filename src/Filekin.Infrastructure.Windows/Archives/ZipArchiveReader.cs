using System.IO.Compression;
using Filekin.Core.Archives;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>
/// Reads a zip's table of contents with <c>System.IO.Compression</c>.
///
/// Only the central directory is touched — no compressed payload is decoded — which is what lets the
/// <c>/unzip</c> preview appear at once even for a large archive. That speed is the reason the
/// preview can be the default without the command feeling slow.
/// </summary>
public sealed class ZipArchiveReader : IArchiveReader
{
    public bool CanRead(string path) => ArchiveFormats.IsSupported(path);

    public IReadOnlyList<ArchiveEntry> ReadEntries(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = new List<ArchiveEntry>(archive.Entries.Count);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A zip marks a directory by ending the name with '/', which leaves Name empty.
                var isDirectory = entry.Name.Length == 0;

                entries.Add(new ArchiveEntry(
                    entry.FullName,
                    entry.Length,
                    entry.CompressedLength,
                    entry.LastWriteTime,
                    isDirectory));
            }

            return entries;
        }
        catch (FileNotFoundException ex)
        {
            throw new ArchiveReadException($"{Path.GetFileName(path)} is no longer there.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new ArchiveReadException($"{Path.GetFileName(path)} is no longer there.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new ArchiveReadException($"{Path.GetFileName(path)} is not a readable zip file.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ArchiveReadException($"Windows would not let Filekin read {Path.GetFileName(path)}.", ex);
        }
        catch (IOException ex)
        {
            throw new ArchiveReadException($"Could not read {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }
}
