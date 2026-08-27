namespace Filekin.App.ViewModels;

/// <summary>
/// One line in the archive preview: a file that will be written or stored, or a note about one that
/// will not be.
///
/// The preview is a read-only list, so this is a plain immutable row rather than an observable one.
/// Changing a control re-plans and rebuilds the rows, which keeps the list and the plan in step by
/// construction instead of by remembering to update both.
/// </summary>
public sealed class ArchiveRowViewModel
{
    private ArchiveRowViewModel(string path, string detail, ArchiveRowKind kind)
    {
        Path = path;
        Detail = detail;
        Kind = kind;
    }

    /// <summary>The path as it will appear, relative to the folder the content lands in.</summary>
    public string Path { get; }

    /// <summary>The size, or the reason this row is a warning.</summary>
    public string Detail { get; }

    public ArchiveRowKind Kind { get; }

    public bool IsWarning => Kind is ArchiveRowKind.Replaced or ArchiveRowKind.Refused;

    /// <summary>Read as one phrase by a screen reader, rather than two unrelated columns.</summary>
    public string AutomationName => Detail.Length == 0 ? Path : $"{Path}, {Detail}";

    public static ArchiveRowViewModel File(string path, string size) =>
        new(path, size, ArchiveRowKind.File);

    /// <summary>A file that is already there and will be replaced or left alone.</summary>
    public static ArchiveRowViewModel Replaced(string path, string detail) =>
        new(path, detail, ArchiveRowKind.Replaced);

    /// <summary>An entry Filekin will not write, and why. Shown rather than hidden.</summary>
    public static ArchiveRowViewModel Refused(string path, string reason) =>
        new(path, reason, ArchiveRowKind.Refused);

    /// <summary>The "and N more" line that caps a very long list.</summary>
    public static ArchiveRowViewModel More(int remaining) =>
        new($"and {remaining:N0} more", string.Empty, ArchiveRowKind.More);
}

/// <summary>What a preview row is telling the reader.</summary>
public enum ArchiveRowKind
{
    File,
    Replaced,
    Refused,
    More,
}
