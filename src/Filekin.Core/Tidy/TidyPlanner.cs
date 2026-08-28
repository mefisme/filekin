using Filekin.Core.FileSystem;

namespace Filekin.Core.Tidy;

/// <summary>
/// Turns a folder listing into a <see cref="TidyPlan"/>.
///
/// The conservative scope of ARCHITECTURE.md Topic 5W is enforced here rather than in the runner, so
/// the preview shows exactly what will happen:
///
/// <list type="bullet">
/// <item>only files sitting directly in the folder are considered — existing subfolders are left
/// alone and are never descended into;</item>
/// <item>a file whose category folder already exists reuses it (owner decision, 2026-08-27), which
/// is why the destination is computed here and not invented at move time;</item>
/// <item>a file that would collide with something already in the destination is skipped and
/// reported, never overwritten.</item>
/// </list>
///
/// Existence is asked of <see cref="IFileSystemOperations"/> rather than of <c>System.IO</c>
/// directly, so the planner stays a pure function of its two ports and is unit-testable without a
/// real folder.
/// </summary>
public sealed class TidyPlanner
{
    private readonly IDirectoryLister _lister;
    private readonly IFileSystemOperations _operations;

    public TidyPlanner(IDirectoryLister lister, IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(operations);
        _lister = lister;
        _operations = operations;
    }

    public TidyPlan Plan(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var grouped = new Dictionary<TidyCategory, List<TidyItem>>();
        var skipped = new List<TidySkip>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _lister.List(folderPath))
        {
            // Existing subfolder organization is left alone, including the category folders
            // themselves, so a second run never sweeps its own output.
            if (entry.IsDirectory)
            {
                continue;
            }

            if (TidyClassifier.IsInProgressDownload(entry.Name))
            {
                skipped.Add(new TidySkip(entry.Name, "still downloading"));
                continue;
            }

            if (TidyClassifier.Classify(entry.Name) is not { } category)
            {
                skipped.Add(new TidySkip(entry.Name, "no file type"));
                continue;
            }

            var target = Path.Combine(folderPath, category.FolderName(), entry.Name);

            if (_operations.GetKind(target) != FileSystemEntryKind.None)
            {
                skipped.Add(new TidySkip(entry.Name, $"already in {category.FolderName()}"));
                continue;
            }

            // Belt and braces: one listing cannot hold the same name twice, but a caller that hands
            // us a repeated entry must not produce two moves onto one path.
            if (!claimed.Add(target))
            {
                skipped.Add(new TidySkip(entry.Name, "duplicate name"));
                continue;
            }

            if (!grouped.TryGetValue(category, out var items))
            {
                items = [];
                grouped[category] = items;
            }

            items.Add(new TidyItem(entry.FullPath, entry.Name, category));
        }

        var groups = new List<TidyGroup>();
        foreach (var category in TidyCategoryNames.All)
        {
            if (grouped.TryGetValue(category, out var items) && items.Count > 0)
            {
                groups.Add(new TidyGroup(category, Path.Combine(folderPath, category.FolderName()), items));
            }
        }

        return new TidyPlan(folderPath, groups, skipped);
    }
}
