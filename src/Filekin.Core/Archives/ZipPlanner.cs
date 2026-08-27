namespace Filekin.Core.Archives;

/// <summary>
/// Works out what <c>/zip</c> will store and under what names.
///
/// Naming is the mirror of <see cref="ArchivePlanner"/>'s wrapper rule. A folder keeps its own name
/// as the archive's root, so unzipping the result gives the folder back rather than spilling its
/// contents into whatever folder you happened to be in. The <c>/zip</c> preview can turn that off,
/// and only for a single folder source: stripping the root from several sources would merge
/// unrelated trees into one namespace, which is a collision waiting to happen rather than a default.
///
/// Enumeration walks the real filesystem, so this is always called off the UI thread.
/// </summary>
public static class ZipPlanner
{
    /// <summary>The default archive name for what is being compressed.</summary>
    /// <param name="sources">The items being compressed.</param>
    /// <param name="currentFolder">The folder the archive lands in when no name is given.</param>
    public static string DefaultOutputPath(IReadOnlyList<string> sources, string currentFolder)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolder);

        // One item names the archive after itself; several name it after the folder they are in,
        // because no single source is a fair label for the rest.
        var name = sources.Count == 1
            ? Path.GetFileNameWithoutExtension(sources[0].TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileName(currentFolder.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Archive";
        }

        return Path.Combine(currentFolder, name + ".zip");
    }

    /// <summary>Builds the plan.</summary>
    /// <param name="sources">Absolute paths of the files and folders to compress.</param>
    /// <param name="outputPath">Where the archive will be written.</param>
    /// <param name="includeRoot">Whether a single folder source keeps its own name inside the archive.</param>
    /// <param name="collisions">What to do if the archive already exists.</param>
    public static ZipPlan Create(
        IReadOnlyList<string> sources,
        string outputPath,
        bool includeRoot = true,
        CollisionPolicy collisions = CollisionPolicy.Skip)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var entries = new List<ZipPlanEntry>();
        var skipped = new List<SkippedSource>();
        var output = Path.GetFullPath(outputPath);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var full = Path.GetFullPath(source);

            if (Directory.Exists(full))
            {
                // Stripping the root only makes sense for a lone folder: doing it to several
                // would merge unrelated trees into one namespace and collide.
                var stripRoot = !includeRoot && sources.Count == 1;
                AddFolder(full, stripRoot, output, entries, skipped, taken);
            }
            else if (File.Exists(full))
            {
                AddFile(full, Path.GetFileName(full), output, entries, skipped, taken);
            }
            else
            {
                skipped.Add(new SkippedSource(full, "No longer there."));
            }
        }

        return new ZipPlan(
            sources,
            output,
            includeRoot,
            collisions,
            entries,
            skipped,
            File.Exists(output));
    }

    private static void AddFolder(
        string folder,
        bool stripRoot,
        string output,
        List<ZipPlanEntry> entries,
        List<SkippedSource> skipped,
        HashSet<string> taken)
    {
        var rootName = Path.GetFileName(folder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var prefix = stripRoot || rootName.Length == 0 ? string.Empty : rootName + "/";

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,

                // A junction or symlink is stored as the one link it is, never walked into, so a
                // loop cannot turn one folder into an unbounded archive.
                AttributesToSkip = FileAttributes.ReparsePoint,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedSource(folder, ex.Message));
            return;
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            AddFile(file, prefix + relative, output, entries, skipped, taken);
        }
    }

    private static void AddFile(
        string file,
        string entryPath,
        string output,
        List<ZipPlanEntry> entries,
        List<SkippedSource> skipped,
        HashSet<string> taken)
    {
        // Never store the archive inside itself.
        if (file.Equals(output, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!taken.Add(entryPath))
        {
            skipped.Add(new SkippedSource(file, $"Another item is already stored as {entryPath}."));
            return;
        }

        try
        {
            entries.Add(new ZipPlanEntry(file, entryPath, new FileInfo(file).Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedSource(file, ex.Message));
        }
    }
}
