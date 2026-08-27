using System.Buffers;

namespace Filekin.Core.Archives;

/// <summary>
/// Turns an archive's table of contents into an <see cref="ArchivePlan"/>: where every entry lands,
/// what already exists there, and what is refused.
///
/// The default layout is the point of the whole command. PRODUCT.md names the problem — "archive
/// extraction can create unnecessary nested folders" — and asks the utility to avoid redundant
/// nesting when an archive already contains a wrapper directory. Stated positively, so the fast path
/// needs no thought: <b>extraction produces exactly one new folder in the destination</b>. An archive
/// carrying its own wrapper reuses it; an archive of loose files gets one named after the archive.
/// Neither sprays files into the destination, and neither doubles the folder up.
///
/// Every raw entry name is treated as hostile. Archive path traversal is listed under Security
/// Considerations in ARCHITECTURE.md, so a name is rejected syntactically and the path it resolves to
/// is then re-checked for containment. Both gates must pass.
/// </summary>
public static class ArchivePlanner
{
    /// <summary>
    /// Characters Windows will not accept in a file or folder name, plus the C0 control range.
    /// Judged by Windows rules whatever the host, because the archive is being written to a Windows
    /// filesystem and an entry name is whatever the archive's author chose to put there.
    /// </summary>
    private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create(
        BuildInvalidNameChars());

    private static string BuildInvalidNameChars()
    {
        var invalid = new char[7 + 32];
        "<>:\"|?*".CopyTo(invalid);
        for (var code = 0; code < 32; code++)
        {
            invalid[7 + code] = (char)code;
        }

        return new string(invalid);
    }

    /// <summary>
    /// Returns the archive's single wrapper directory, or <c>null</c> when the archive has loose
    /// entries at its root. A wrapper needs every entry to sit under one shared first segment; a
    /// single loose file at the root is enough to disqualify it.
    /// </summary>
    public static string? DetectWrapper(IReadOnlyList<ArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string? candidate = null;
        var sawContent = false;

        foreach (var entry in entries)
        {
            var normalized = NormalizeEntryPath(entry.Path);
            if (normalized.Length == 0)
            {
                continue;
            }

            var slash = normalized.IndexOf('/', StringComparison.Ordinal);

            // A loose file at the archive root means there is nothing to unwrap.
            if (slash < 0 && !entry.IsDirectory)
            {
                return null;
            }

            var first = slash < 0 ? normalized : normalized[..slash];
            if (candidate is null)
            {
                candidate = first;
            }
            else if (!candidate.Equals(first, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            sawContent = true;
        }

        return sawContent ? candidate : null;
    }

    /// <summary>
    /// The folder name Filekin proposes for <see cref="UnzipLayout.NewFolder"/>: the archive's own
    /// wrapper when it has one, otherwise the archive's file name without its extension.
    /// </summary>
    public static string DefaultFolderName(string archivePath, string? wrapperName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (wrapperName is { Length: > 0 })
        {
            return wrapperName;
        }

        var name = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(name) ? "Extracted" : name;
    }

    /// <summary>Builds the plan for one archive.</summary>
    /// <param name="archivePath">The archive being extracted.</param>
    /// <param name="destinationRoot">The folder the user chose. It need not exist yet.</param>
    /// <param name="entries">The archive's table of contents.</param>
    /// <param name="layout">Whether to create one folder, or extract straight into the destination.</param>
    /// <param name="collisions">What to do about files that are already there.</param>
    /// <param name="folderName">
    /// Overrides the proposed folder name for <see cref="UnzipLayout.NewFolder"/>. Ignored for
    /// <see cref="UnzipLayout.NoRoot"/>.
    /// </param>
    /// <param name="pathExists">
    /// Probe used to find collisions. Defaults to the real filesystem; tests supply their own.
    /// </param>
    public static ArchivePlan Create(
        string archivePath,
        string destinationRoot,
        IReadOnlyList<ArchiveEntry> entries,
        UnzipLayout layout = UnzipLayout.NewFolder,
        CollisionPolicy collisions = CollisionPolicy.Skip,
        string? folderName = null,
        Func<string, bool>? pathExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(entries);

        var wrapper = DetectWrapper(entries);
        var root = Path.GetFullPath(destinationRoot);

        string? resolvedFolder = null;
        if (layout == UnzipLayout.NewFolder)
        {
            var proposed = string.IsNullOrWhiteSpace(folderName)
                ? DefaultFolderName(archivePath, wrapper)
                : folderName;
            resolvedFolder = SanitizeFolderName(proposed, archivePath);
        }

        var targetRoot = resolvedFolder is { Length: > 0 } ? Path.Combine(root, resolvedFolder) : root;

        var planned = new List<PlannedEntry>();
        var rejected = new List<RejectedEntry>();
        var existing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exists = pathExists ?? (path => File.Exists(path) || Directory.Exists(path));

        foreach (var entry in entries)
        {
            var normalized = NormalizeEntryPath(entry.Path);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!IsSafeEntryPath(normalized, out var reason))
            {
                rejected.Add(new RejectedEntry(entry.Path, reason));
                continue;
            }

            var beneathWrapper = StripWrapper(normalized, wrapper);
            if (beneathWrapper.Length == 0)
            {
                // The wrapper's own directory entry: it becomes the folder, not a child of it.
                continue;
            }

            var relative = beneathWrapper.Replace('/', Path.DirectorySeparatorChar);

            string absolute;
            try
            {
                absolute = Path.GetFullPath(Path.Combine(targetRoot, relative));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                rejected.Add(new RejectedEntry(entry.Path, "Is not a usable Windows path."));
                continue;
            }

            // Second gate: whatever the name looked like, the path it resolves to must stay inside.
            if (!IsContained(absolute, targetRoot))
            {
                rejected.Add(new RejectedEntry(entry.Path, "Escapes the destination folder."));
                continue;
            }

            planned.Add(new PlannedEntry(entry.Path, relative, entry.Length, entry.IsDirectory));

            if (!entry.IsDirectory && seen.Add(absolute) && exists(absolute))
            {
                existing.Add(absolute);
            }
        }

        return new ArchivePlan(
            archivePath,
            root,
            layout,
            collisions,
            resolvedFolder,
            wrapper,
            planned,
            rejected,
            existing);
    }

    /// <summary>
    /// Reduces an entry name to the archive's own convention: forward slashes, no leading
    /// <c>./</c>, and no surrounding slashes, so a directory entry compares equal to its own name.
    /// </summary>
    private static string NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var text = path.Replace('\\', '/').Trim();

        while (text.StartsWith("./", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        return text.Trim('/');
    }

    private static bool IsSafeEntryPath(string normalized, out string reason)
    {
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
        {
            reason = "Names an absolute location.";
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is ".." or ".")
            {
                reason = "Points outside the archive.";
                return false;
            }

            if (segment.AsSpan().IndexOfAny(InvalidNameChars) >= 0)
            {
                reason = "Contains characters Windows cannot store.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static string StripWrapper(string normalized, string? wrapper)
    {
        if (wrapper is not { Length: > 0 })
        {
            return normalized;
        }

        if (normalized.Equals(wrapper, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized.StartsWith(wrapper + '/', StringComparison.OrdinalIgnoreCase)
            ? normalized[(wrapper.Length + 1)..]
            : normalized;
    }

    /// <summary>
    /// Keeps a proposed folder name to one creatable folder. A name that could not be created falls
    /// back to the archive's own name rather than failing the command the user actually asked for.
    /// </summary>
    private static string SanitizeFolderName(string proposed, string archivePath)
    {
        var trimmed = proposed.Trim().Trim('/', '\\').Trim();

        if (trimmed.Length == 0 ||
            trimmed is "." or ".." ||
            trimmed.Contains('/', StringComparison.Ordinal) ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed.AsSpan().IndexOfAny(InvalidNameChars) >= 0)
        {
            var fallback = Path.GetFileNameWithoutExtension(archivePath);
            return string.IsNullOrWhiteSpace(fallback) ? "Extracted" : fallback;
        }

        return trimmed;
    }

    private static bool IsContained(string absolute, string root)
    {
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return absolute.Equals(trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
               absolute.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
