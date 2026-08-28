using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filekin.Infrastructure.Windows.Settings;

/// <summary>The readable contents of <c>%AppData%\Filekin\settings.json</c>.</summary>
public sealed record FilekinSettings
{
    /// <summary>The ordered sidebar Locations, also the command-bar <c>@name</c> references.</summary>
    [JsonPropertyName("locations")]
    public List<SavedLocation> Locations { get; init; } = [];

    /// <summary>One of <see cref="ThemePreference"/>. Filekin's default theme is dark.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; init; } = ThemePreference.Dark;

    /// <summary>
    /// The accent colour name. The set Filekin ships is owned by the app layer; the store keeps only
    /// the shape (a short lower-case name), so an accent added in a newer build is not discarded by
    /// an older one that does not recognise it.
    /// </summary>
    [JsonPropertyName("accent")]
    public string Accent { get; init; } = "blue";

    /// <summary>Where the Files workspace opens at launch. Absent means the user's profile folder.</summary>
    [JsonPropertyName("openFilesAtLaunch")]
    public StartupLocation OpenFilesAtLaunch { get; init; } = new();

    /// <summary>
    /// Extra executable names that should open in a hosted terminal tab instead of running as a
    /// finite command. These add to the built-in rules; they never remove one.
    /// </summary>
    [JsonPropertyName("interactivePrograms")]
    public List<string> InteractivePrograms { get; init; } = [];

    /// <summary>How <c>/unzip</c> and <c>/zip</c> behave when the user does not say.</summary>
    [JsonPropertyName("archives")]
    public ArchiveSettings Archives { get; init; } = new();

    /// <summary>How <c>/tidy</c> behaves when the user does not say.</summary>
    [JsonPropertyName("tidy")]
    public TidySettings Tidy { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>
/// The archive-command defaults.
///
/// Both live here rather than being hardcoded because neither answer is right for everyone. The
/// owner overwrites existing files most of the time; someone else would rather never lose one. The
/// shipped defaults are the cautious pair, and the command switches override them for one command
/// without changing what is stored (owner decision, 2026-08-27).
/// </summary>
public sealed record ArchiveSettings
{
    /// <summary>
    /// Whether <c>/unzip</c> and <c>/zip</c> show what they will do before doing it. On by default:
    /// extraction writes many files at once and the preview is the only thing standing between the
    /// user and a wrong destination.
    /// </summary>
    [JsonPropertyName("previewBeforeExtracting")]
    public bool PreviewBeforeExtracting { get; init; } = true;

    /// <summary>One of <see cref="CollisionPreference"/>.</summary>
    [JsonPropertyName("whenAFileExists")]
    public string WhenAFileExists { get; init; } = CollisionPreference.Skip;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>
/// The <c>/tidy</c> defaults.
///
/// Only the preview toggle is stored. The category ticks in the preview are per run and deliberately
/// have no setting: a persisted category choice would quietly make Tidy do less than the user
/// expects, for a reason set days earlier and no longer visible (owner decision, 2026-08-27).
/// </summary>
public sealed record TidySettings
{
    /// <summary>
    /// Whether <c>/tidy</c> shows what it will do before doing it. On by default. ARCHITECTURE.md
    /// Topic 5X originally shipped Tidy with no preview at all; the owner superseded that on
    /// 2026-08-27 so Tidy matches <c>/unzip</c>, which the same user already knows.
    /// </summary>
    [JsonPropertyName("previewBeforeTidying")]
    public bool PreviewBeforeTidying { get; init; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>The two accepted <c>archives.whenAFileExists</c> values.</summary>
public static class CollisionPreference
{
    /// <summary>Leave the existing file alone. The shipped default.</summary>
    public const string Skip = "skip";

    /// <summary>Replace it, sending the original to the Recycle Bin first.</summary>
    public const string Overwrite = "overwrite";

    public static bool IsKnown(string value) => value is Skip or Overwrite;
}

/// <summary>
/// One ordered sidebar Location. <see cref="Name"/> is both the visible short name and the
/// case-insensitive command-bar reference name; the UI supplies the leading <c>@</c>.
/// </summary>
public sealed class SavedLocation
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>The three accepted <c>theme</c> values.</summary>
public static class ThemePreference
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    public static bool IsKnown(string value) =>
        value is Dark or Light or System;
}

/// <summary>The three accepted <c>openFilesAtLaunch.target</c> values.</summary>
public static class StartupTarget
{
    /// <summary>The current user's profile folder — the default when nothing is configured.</summary>
    public const string Home = "home";

    /// <summary>A saved Location, by name, so the target follows later path changes to it.</summary>
    public const string Location = "location";

    /// <summary>One explicitly chosen absolute filesystem folder.</summary>
    public const string Folder = "folder";

    public static bool IsKnown(string value) =>
        value is Home or Location or Folder;
}

/// <summary>
/// The startup-location preference. A configured target that is missing or temporarily unavailable
/// is preserved rather than erased; Filekin falls back to Home for that launch and says so
/// (ARCHITECTURE.md — "Startup Files Location").
/// </summary>
public sealed record StartupLocation
{
    [JsonPropertyName("target")]
    public string Target { get; init; } = StartupTarget.Home;

    /// <summary>The saved Location name when <see cref="Target"/> is <c>location</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The absolute folder when <see cref="Target"/> is <c>folder</c>.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record SettingsLoadResult(
    FilekinSettings Settings,
    bool FileExists,
    bool IsMalformed,
    IReadOnlyList<string> Warnings);

/// <summary>The outcome of one durable settings mutation.</summary>
public sealed record SettingsSaveResult(bool Succeeded, string Message)
{
    public static SettingsSaveResult Ok(string message = "") => new(Succeeded: true, message);

    public static SettingsSaveResult Fail(string message) => new(Succeeded: false, message);
}
