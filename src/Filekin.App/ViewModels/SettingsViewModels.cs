using System.Windows.Media;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Settings categories, in sidebar order. Each one owns a whole subject rather than a single
/// control, so a new preference joins an existing category instead of growing the rail
/// (UX-DESIGN.md — "bloated Settings screens" is an explicit anti-pattern).
/// </summary>
public enum SettingsCategory
{
    /// <summary>Theme and other appearance preferences.</summary>
    Appearance,

    /// <summary>Where the Files workspace opens at launch.</summary>
    Startup,

    /// <summary>Which programs the command bar routes into a hosted terminal tab.</summary>
    Terminal,

    /// <summary>Archive preview and collision defaults.</summary>
    Archives,

    /// <summary>The <c>/tidy</c> preview default.</summary>
    Tidy,

    /// <summary>The readable settings file itself.</summary>
    Advanced,
}

/// <summary>
/// One row in the Settings category rail. Deliberately text only: the rail is a few words, and a
/// decorative glyph beside each would be exactly the "random excessive icons" the visual language
/// rules out (UX-DESIGN.md).
/// </summary>
public sealed class SettingsCategoryViewModel(SettingsCategory key, string title, string summary)
    : ObservableObject
{
    private bool _isSelected;

    public SettingsCategory Key { get; } = key;

    public string Title { get; } = title;

    /// <summary>One line naming what the category holds, shown under its title in the panel.</summary>
    public string Summary { get; } = summary;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string AutomationName => $"{Title} settings";
}

/// <summary>
/// One choice in a single-select settings group — a theme, or a startup destination. Choosing a row
/// applies and persists immediately; Filekin has no Settings dialog with a Save button, so there is
/// never an unsaved state to lose.
/// </summary>
public sealed class SettingsOptionViewModel(
    string value,
    string title,
    string detail,
    bool requiresFolderPick = false) : ObservableObject
{
    private bool _isSelected;

    /// <summary>The durable value this row writes, e.g. <c>dark</c>, <c>home</c>, <c>location:projects</c>.</summary>
    public string Value { get; } = value;

    public string Title { get; } = title;

    /// <summary>The quiet explanation beside the title: a palette description, or a resolved path.</summary>
    public string Detail { get; } = detail;

    /// <summary>Whether choosing this row must first ask the user for a folder.</summary>
    public bool RequiresFolderPick { get; } = requiresFolderPick;

    /// <summary>A colour chip shown before the title, for the accent rows. Null for every other group.</summary>
    public Brush? Swatch { get; init; }

    public bool HasSwatch => Swatch is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    /// <summary>A filled or hollow mark — a radio group drawn in the command-bar font.</summary>
    public string Marker => _isSelected ? "●" : "○";

    public string AutomationName => string.IsNullOrEmpty(Detail) ? Title : $"{Title}. {Detail}";
}

/// <summary>
/// One program name in the interactive registry. Built-in rules are shown so the user can see what
/// is already covered before adding a duplicate, and can never be removed.
/// </summary>
public sealed record InteractiveProgramViewModel(string Name, bool IsBuiltIn)
{
    public string Origin => IsBuiltIn ? "built-in" : "added";

    public bool CanRemove => !IsBuiltIn;

    public string AutomationName => IsBuiltIn
        ? $"{Name}, a built-in interactive program"
        : $"{Name}, an interactive program you added";
}
