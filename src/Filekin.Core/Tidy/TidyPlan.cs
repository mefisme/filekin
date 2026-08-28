using Filekin.Core.FileSystem;

namespace Filekin.Core.Tidy;

/// <summary>One loose file and the category folder it would move into.</summary>
/// <param name="SourcePath">Where the file is now.</param>
/// <param name="Name">Its file name, for the preview and the result.</param>
/// <param name="Category">The folder it belongs to.</param>
public sealed record TidyItem(string SourcePath, string Name, TidyCategory Category);

/// <summary>
/// One category's share of a plan: the folder that would be created or reused, and what would go in
/// it. The preview shows one row per group, and its tick controls the whole group — the owner
/// confirmed on 2026-08-27 that the toggles are per category, never per file.
/// </summary>
public sealed record TidyGroup(TidyCategory Category, string DestinationPath, IReadOnlyList<TidyItem> Items)
{
    public int Count => Items.Count;
}

/// <summary>
/// A file Tidy deliberately will not move, and the reason a human needs to see. ARCHITECTURE.md
/// Topic 5W requires skipped, conflicting, and unclassified items to be reported clearly rather than
/// silently dropped.
/// </summary>
public sealed record TidySkip(string Name, string Reason);

/// <summary>
/// What <c>/tidy</c> proposes to do to one folder.
///
/// The plan is pure data and never touches the filesystem, so the preview surface, the result view,
/// and the tests all read the same object.
/// </summary>
public sealed record TidyPlan(string FolderPath, IReadOnlyList<TidyGroup> Groups, IReadOnlyList<TidySkip> Skipped)
{
    /// <summary>Total files across every group, ignoring which groups are currently ticked.</summary>
    public int FileCount => Groups.Sum(group => group.Count);

    public bool HasWork => FileCount > 0;
}
