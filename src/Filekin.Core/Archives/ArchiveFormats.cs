namespace Filekin.Core.Archives;

/// <summary>
/// The archive formats Filekin can open.
///
/// Version one is zip only, because <c>System.IO.Compression</c> is the standard .NET API and
/// reading it needs no third-party dependency. 7z and rar would each mean shipping someone else's
/// library, which is a product decision rather than an implementation detail — so an unsupported
/// archive is refused with a clear message instead of being half-handled.
/// </summary>
public static class ArchiveFormats
{
    private static readonly string[] SupportedExtensions = [".zip"];

    /// <summary>Whether <paramref name="path"/> names a format this build can read.</summary>
    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        foreach (var supported in SupportedExtensions)
        {
            if (extension.Equals(supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="path"/> looks like an archive of any kind, supported or not. Used to
    /// tell an archive argument from a destination argument, and to give a better error than
    /// "not an archive" when someone points <c>/unzip</c> at a <c>.7z</c>.
    /// </summary>
    public static bool LooksLikeArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A human-readable list of what this build accepts, for error messages.</summary>
    public static string SupportedDescription => ".zip";
}
