using System.Collections.Generic;

namespace Filekin.App.ViewModels;

/// <summary>
/// Static sample content for the Filekin shell window. This is design data only:
/// it lets the window render the real layout and visual language before any
/// filesystem, command-bar, or terminal behavior is wired in.
/// </summary>
public sealed class ShellViewModel
{
    public string ItemCount { get; } = "10 items";

    public string StatusSelection { get; } = "1 selected";

    public string StatusFree { get; } = "118.7 GB free (D:)";

    public IReadOnlyList<NavItem> Locations { get; } = new List<NavItem>
    {
        new("@", "Projects", IsActive: false, SymbolAccent: false),
        new("@", "Downloads", IsActive: false, SymbolAccent: false),
        new("@", "Music", IsActive: false, SymbolAccent: false),
        new("@", "GitHub", IsActive: true, SymbolAccent: false),
        new("@", "SnapMap", IsActive: false, SymbolAccent: false),
    };

    public IReadOnlyList<NavItem> Surfaces { get; } = new List<NavItem>
    {
        new("/", "places", IsActive: false, SymbolAccent: true),
        new("/", "drives", IsActive: false, SymbolAccent: true),
    };

    public IReadOnlyList<FileRow> Files { get; } = new List<FileRow>
    {
        new("DIR", ".github", IsDirectory: true, "2024-05-20 10:15", "—"),
        new("DIR", "docs", IsDirectory: true, "2024-05-20 09:41", "—"),
        new("DIR", "src", IsDirectory: true, "2024-05-20 09:42", "—"),
        new("DIR", "tests", IsDirectory: true, "2024-05-20 09:42", "—"),
        new("DIR", "tools", IsDirectory: true, "2024-05-20 09:43", "—"),
        new("CFG", ".gitignore", IsDirectory: false, "2024-05-19 16:18", "1 KB"),
        new("SLN", "Filekin.sln", IsDirectory: false, "2024-05-19 16:18", "2 KB"),
        new("MD", "README.md", IsDirectory: false, "2024-05-20 10:01", "3 KB"),
        new("XML", "Directory.Build.props", IsDirectory: false, "2024-05-19 16:18", "1 KB"),
        new("TXT", "LICENSE", IsDirectory: false, "2024-05-19 16:18", "2 KB"),
    };
}

public sealed record NavItem(string Symbol, string Name, bool IsActive, bool SymbolAccent);

public sealed record FileRow(string TypeCode, string Name, bool IsDirectory, string Modified, string Size);
