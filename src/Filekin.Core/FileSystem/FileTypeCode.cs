namespace Filekin.Core.FileSystem;

/// <summary>
/// Maps a filesystem entry to the short terminal-style type code shown in the TYPE column
/// (UX-DESIGN.md — "File Representation": types are represented textually, e.g. <c>DIR</c>, <c>IMG</c>,
/// <c>ZIP</c>). This is a small deterministic lookup, not content inspection or AI classification
/// (ENGINEERING-GUARDRAILS.md). Directories are always <c>DIR</c>; recognized extensions map to a
/// readable family code; anything else falls back to its own uppercased extension, and an extensionless
/// file is <c>FILE</c>.
/// </summary>
public static class FileTypeCode
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sln"] = "SLN",
        ["csproj"] = "PROJ",
        ["props"] = "XML",
        ["targets"] = "XML",
        ["xml"] = "XML",
        ["md"] = "MD",
        ["markdown"] = "MD",
        ["txt"] = "TXT",
        ["json"] = "JSON",
        ["yml"] = "YAML",
        ["yaml"] = "YAML",
        ["cs"] = "CS",
        ["ps1"] = "PS1",
        ["psm1"] = "PS1",
        ["png"] = "IMG",
        ["jpg"] = "IMG",
        ["jpeg"] = "IMG",
        ["gif"] = "IMG",
        ["bmp"] = "IMG",
        ["webp"] = "IMG",
        ["svg"] = "IMG",
        ["wav"] = "AUD",
        ["mp3"] = "AUD",
        ["flac"] = "AUD",
        ["ogg"] = "AUD",
        ["mp4"] = "VID",
        ["mov"] = "VID",
        ["mkv"] = "VID",
        ["zip"] = "ZIP",
        ["7z"] = "ZIP",
        ["rar"] = "ZIP",
        ["tar"] = "ZIP",
        ["gz"] = "ZIP",
        ["exe"] = "EXE",
        ["msi"] = "EXE",
        ["dll"] = "DLL",
    };

    /// <summary>Returns the type code for a listed entry.</summary>
    public static string ForEntry(DirectoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return For(entry.Name, entry.IsDirectory);
    }

    /// <summary>Returns the type code for a name and directory flag.</summary>
    public static string For(string name, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (isDirectory)
        {
            return "DIR";
        }

        // A dotfile such as ".gitignore" or ".editorconfig" has a leading dot and no further extension;
        // treat it as configuration rather than rendering a long word from its whole name.
        if (name.StartsWith('.') && name.IndexOf('.', 1) < 0)
        {
            return "CFG";
        }

        var extension = Path.GetExtension(name).TrimStart('.');
        if (string.IsNullOrEmpty(extension))
        {
            return "FILE";
        }

        return ByExtension.TryGetValue(extension, out var code)
            ? code
            : extension.ToUpperInvariant();
    }
}
