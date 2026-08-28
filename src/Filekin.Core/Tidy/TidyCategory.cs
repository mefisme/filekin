namespace Filekin.Core.Tidy;

/// <summary>
/// The folders <c>/tidy</c> may create, and the order they are offered in.
///
/// The first six are the categories ARCHITECTURE.md Topic 5W names. <see cref="Other"/> is the
/// seventh, added by the owner decision of 2026-08-27 that supersedes "leave unknown/unclassified
/// file types in place": a folder with a dozen stragglers still loose does not read as tidied.
///
/// The names are the literal folder names on disk, so they are plain nouns rather than abbreviations
/// (UX-DESIGN.md — "Readability Over Abbreviation": <c>Other</c>, never <c>Misc</c>).
/// </summary>
public enum TidyCategory
{
    Documents,
    Photos,
    Audio,
    Videos,
    Archives,
    Installers,

    /// <summary>Everything with an extension Filekin does not recognize.</summary>
    Other,
}

/// <summary>The on-disk folder name for each category.</summary>
public static class TidyCategoryNames
{
    public static string FolderName(this TidyCategory category) => category switch
    {
        TidyCategory.Documents => "Documents",
        TidyCategory.Photos => "Photos",
        TidyCategory.Audio => "Audio",
        TidyCategory.Videos => "Videos",
        TidyCategory.Archives => "Archives",
        TidyCategory.Installers => "Installers",
        TidyCategory.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>Every category, in the order the preview lists them.</summary>
    public static IReadOnlyList<TidyCategory> All { get; } =
    [
        TidyCategory.Documents,
        TidyCategory.Photos,
        TidyCategory.Audio,
        TidyCategory.Videos,
        TidyCategory.Archives,
        TidyCategory.Installers,
        TidyCategory.Other,
    ];
}
