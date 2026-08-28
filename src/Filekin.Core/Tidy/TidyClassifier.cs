namespace Filekin.Core.Tidy;

/// <summary>
/// Maps a file name to the folder <c>/tidy</c> would move it into.
///
/// The mapping is a plain extension table on purpose. ARCHITECTURE.md Topic 5W requires Tidy to be
/// "deterministic and understandable rather than using opaque AI classification", and the
/// ENGINEERING-GUARDRAILS repeat it: no AI classification in v1. The table may grow without changing
/// the command contract.
///
/// Two rules are not extension lookups and matter more than the table:
///
/// <list type="bullet">
/// <item>A file that is still being written — a browser part-download — is never moved, because
/// moving it breaks the download in progress. <see cref="IsInProgressDownload"/>.</item>
/// <item>A file with no extension at all is left alone rather than swept into <c>Other</c>. Filekin
/// knows nothing about it, and the owner's decision covers unknown <em>types</em>, not unidentifiable
/// files.</item>
/// </list>
///
/// Project files follow their medium (owner decision, 2026-08-27): a <c>.psd</c> sits with the
/// photos, a <c>.prproj</c> with the videos. A project file with no obvious medium — <c>.sln</c>,
/// <c>.blend</c> — is not forced anywhere and lands in <c>Other</c> like any other unknown type.
/// </summary>
public static class TidyClassifier
{
    /// <summary>
    /// Extensions that mean "this file is still downloading". Moving one breaks the transfer, so
    /// Tidy leaves it exactly where it is and reports it as skipped.
    /// </summary>
    private static readonly HashSet<string> InProgressDownloads = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".temp", ".!ut",
    };

    private static readonly Dictionary<string, TidyCategory> Map = BuildMap();

    /// <summary>
    /// The category for <paramref name="fileName"/>, or <c>null</c> when the file must not be moved
    /// at all — no extension, or a download still in progress.
    /// </summary>
    public static TidyCategory? Classify(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName);
        if (extension.Length <= 1)
        {
            // "" or a bare "." — nothing to classify, so nothing to move.
            return null;
        }

        if (InProgressDownloads.Contains(extension))
        {
            return null;
        }

        return Map.TryGetValue(extension, out var category) ? category : TidyCategory.Other;
    }

    /// <summary>Whether <paramref name="fileName"/> looks like a transfer that has not finished.</summary>
    public static bool IsInProgressDownload(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return InProgressDownloads.Contains(Path.GetExtension(fileName));
    }

    private static Dictionary<string, TidyCategory> BuildMap()
    {
        var map = new Dictionary<string, TidyCategory>(StringComparer.OrdinalIgnoreCase);

        Add(map, TidyCategory.Documents,
            ".pdf", ".doc", ".docx", ".docm", ".odt", ".rtf", ".txt", ".md", ".markdown", ".tex",
            ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".tsv",
            ".ppt", ".pptx", ".pptm", ".odp",
            ".epub", ".mobi", ".azw", ".azw3", ".djvu", ".xps", ".pages", ".numbers", ".key");

        Add(map, TidyCategory.Photos,
            ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".avif",
            ".heic", ".heif", ".svg", ".ico", ".jfif",
            // Camera raw.
            ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".srw",
            // Image project files follow their medium.
            ".psd", ".psb", ".ai", ".xcf", ".cdr", ".afphoto", ".afdesign");

        Add(map, TidyCategory.Audio,
            ".mp3", ".wav", ".flac", ".aac", ".m4a", ".m4b", ".ogg", ".oga", ".opus", ".wma",
            ".aiff", ".aif", ".alac", ".ape", ".amr", ".mid", ".midi",
            // Audio project files.
            ".aup", ".aup3", ".flp", ".als", ".logicx", ".band", ".ptx", ".rpp");

        Add(map, TidyCategory.Videos,
            ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg",
            ".3gp", ".3g2", ".ts", ".m2ts", ".mts", ".vob", ".ogv", ".divx", ".asf",
            // Video project files.
            ".prproj", ".veg", ".kdenlive", ".fcpxml", ".aep", ".camproj");

        Add(map, TidyCategory.Archives,
            ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz",
            ".zst", ".lz", ".lzma", ".lz4", ".arj", ".ace", ".cab", ".z", ".zipx",
            // A disc image is a container of packaged contents (owner decision, 2026-08-27).
            ".iso", ".img", ".vhd", ".vhdx", ".dmg");

        Add(map, TidyCategory.Installers,
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msp", ".msu");

        return map;
    }

    private static void Add(Dictionary<string, TidyCategory> map, TidyCategory category, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            map[extension] = category;
        }
    }
}
