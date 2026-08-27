using System.Globalization;
using System.Reflection.PortableExecutable;
using Filekin.Core.FileSystem;
using Filekin.Core.Inspection;
using Filekin.Infrastructure.Windows.Inspection.Interop;

namespace Filekin.Infrastructure.Windows.Inspection;

/// <summary>
/// Builds the immediately readable part of an Info sheet from Windows metadata.
///
/// Type-specific rows come from the Windows Property System rather than from per-format parsers, so
/// a new codec or image format works without a Filekin change (DECISIONS.md, 2026-08-27). Nothing
/// here walks a tree or reads a whole file; recursive totals, hashes, and line counts arrive
/// separately. Empty values are never rendered as blank rows.
/// </summary>
public sealed class WindowsFileInspector : IFileInspector
{
    public InspectionResult Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (Directory.Exists(path))
            {
                return InspectFolder(new DirectoryInfo(path));
            }

            if (File.Exists(path))
            {
                return InspectFile(new FileInfo(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return InspectionResult.Failure(Path.GetFileName(path), ex.Message);
        }

        return InspectionResult.Failure(Path.GetFileName(path), "This item no longer exists.");
    }

    public InspectionResult InspectSelection(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return InspectionResult.Failure("Nothing selected", "Select something, then run /info.");
        }

        if (paths.Count == 1)
        {
            return Inspect(paths[0]);
        }

        var details = new List<InspectionDetail>();
        var directories = paths
            .Select(static path => Path.GetDirectoryName(path))
            .Where(static directory => !string.IsNullOrEmpty(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        details.Add(new InspectionDetail(
            "Location",
            directories.Count == 1 ? directories[0]! : $"{directories.Count} folders"));

        if (ModifiedRange(paths) is { } modified)
        {
            details.Add(new InspectionDetail("Modified", modified));
        }

        return new InspectionResult(
            InspectionKind.Selection,
            $"{paths.Count} selected items",
            singlePath: null,
            details,
            needsAggregate: true);
    }

    private static InspectionResult InspectFolder(DirectoryInfo folder)
    {
        var details = new List<InspectionDetail>
        {
            new("Type", ShellMetadataInterop.GetTypeName(folder.FullName) ?? "Folder"),
            new("Path", folder.FullName),
            new("Created", FormatTimestamp(folder.CreationTime)),
            new("Modified", FormatTimestamp(folder.LastWriteTime)),
        };

        return new InspectionResult(
            InspectionKind.Folder,
            folder.Name.Length > 0 ? folder.Name : folder.FullName,
            folder.FullName,
            details,
            needsAggregate: true);
    }

    private static InspectionResult InspectFile(FileInfo file)
    {
        var details = new List<InspectionDetail>
        {
            new("Type", DescribeType(file)),
            new("Size", ByteSize.Format(file.Length)),
            new("Path", file.FullName),
            new("Created", FormatTimestamp(file.CreationTime)),
            new("Modified", FormatTimestamp(file.LastWriteTime)),
        };

        details.AddRange(TypeSpecificDetails(file));

        var text = TextFileReader.Sniff(file.FullName);
        if (text is not null)
        {
            details.Add(new InspectionDetail("Encoding", text.EncodingName));
        }

        return new InspectionResult(
            InspectionKind.File,
            file.Name,
            file.FullName,
            details,
            needsAggregate: false,
            canCountLines: text is not null);
    }

    private static IEnumerable<InspectionDetail> TypeSpecificDetails(FileInfo file)
    {
        var extension = file.Extension;

        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var detail in ShortcutDetails(file.FullName))
            {
                yield return detail;
            }

            yield break;
        }

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (ReadArchitecture(file.FullName) is { } architecture)
            {
                yield return new InspectionDetail("Architecture", architecture);
            }
        }

        var store = ShellMetadataInterop.TryOpenPropertyStore(file.FullName);
        if (store is null)
        {
            yield break;
        }

        try
        {
            var width = ShellMetadataInterop.ReadUInt32(store, ShellMetadataInterop.ImageWidth);
            var height = ShellMetadataInterop.ReadUInt32(store, ShellMetadataInterop.ImageHeight);
            if (width is > 0 && height is > 0)
            {
                yield return new InspectionDetail(
                    "Dimensions",
                    $"{width.Value.ToString("N0", CultureInfo.CurrentCulture)} × {height.Value.ToString("N0", CultureInfo.CurrentCulture)}");
            }

            if (ShellMetadataInterop.ReadDuration(store, ShellMetadataInterop.MediaDuration) is { Ticks: > 0 } duration)
            {
                yield return new InspectionDetail("Duration", FormatDuration(duration));
            }

            if (Clean(ShellMetadataInterop.ReadString(store, ShellMetadataInterop.ProductName)) is { } product)
            {
                yield return new InspectionDetail("Product", product);
            }

            if (Clean(ShellMetadataInterop.ReadString(store, ShellMetadataInterop.FileVersion)) is { } version)
            {
                yield return new InspectionDetail("Version", version);
            }

            // "Company" is the name written inside the file, which anyone can write. It is
            // deliberately not labelled "Publisher": that word would imply Filekin verified a
            // signature, and it has not (DECISIONS.md, 2026-08-27). Windows Properties owns real
            // signature checks.
            if (Clean(ShellMetadataInterop.ReadString(store, ShellMetadataInterop.Company)) is { } company)
            {
                yield return new InspectionDetail("Company", company);
            }
        }
        finally
        {
            ShellMetadataInterop.Release(store);
        }
    }

    private static IEnumerable<InspectionDetail> ShortcutDetails(string path)
    {
        if (ShellLinkInterop.TryRead(path) is not { } link)
        {
            yield break;
        }

        if (Clean(link.Target) is { } target)
        {
            yield return new InspectionDetail("Target", target);
        }

        if (Clean(link.Arguments) is { } arguments)
        {
            yield return new InspectionDetail("Arguments", arguments);
        }

        if (Clean(link.WorkingDirectory) is { } workingDirectory)
        {
            yield return new InspectionDetail("Start in", workingDirectory);
        }
    }

    private static string? ReadArchitecture(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.CoffHeader.Machine switch
            {
                Machine.Amd64 => "x64",
                Machine.I386 => "x86",
                Machine.Arm64 => "ARM64",
                Machine.Arm => "ARM",
                var other => other.ToString(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    private static string DescribeType(FileInfo file)
    {
        var friendly = ShellMetadataInterop.GetTypeName(file.FullName);
        var extension = file.Extension;
        if (friendly is null)
        {
            return extension.Length > 0 ? extension.TrimStart('.').ToUpperInvariant() : "File";
        }

        // "Application" or "PDF Document" reads better with the extension beside it, but not when
        // the shell already spelled the extension out ("JPG File").
        return extension.Length > 0 &&
               !friendly.Contains(extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            ? $"{friendly} ({extension.ToLowerInvariant()})"
            : friendly;
    }

    private static string? ModifiedRange(IReadOnlyList<string> paths)
    {
        DateTime? oldest = null;
        DateTime? newest = null;

        foreach (var path in paths)
        {
            try
            {
                var written = Directory.Exists(path)
                    ? new DirectoryInfo(path).LastWriteTime
                    : new FileInfo(path).LastWriteTime;
                oldest = oldest is null || written < oldest ? written : oldest;
                newest = newest is null || written > newest ? written : newest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // One unreadable item must not remove the range for the rest.
            }
        }

        if (oldest is null || newest is null)
        {
            return null;
        }

        if (oldest.Value.Date == newest.Value.Date)
        {
            return FormatTimestamp(newest.Value);
        }

        // A range within one month reads as "Aug 12–26, 2026"; anything wider spells both dates.
        return oldest.Value.Year == newest.Value.Year && oldest.Value.Month == newest.Value.Month
            ? string.Format(
                CultureInfo.CurrentCulture,
                "{0:MMM d}–{1:d}, {1:yyyy}",
                oldest.Value,
                newest.Value)
            : $"{FormatDate(oldest.Value)} – {FormatDate(newest.Value)}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
        : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);

    private static string FormatTimestamp(DateTime value) =>
        value.ToString("MMM d, yyyy  h:mm tt", CultureInfo.CurrentCulture);

    private static string FormatDate(DateTime value) =>
        value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
