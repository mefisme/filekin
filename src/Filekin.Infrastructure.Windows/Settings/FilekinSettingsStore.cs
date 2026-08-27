using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Filekin.Core;

namespace Filekin.Infrastructure.Windows.Settings;

/// <summary>
/// Loads and atomically replaces Filekin's readable JSON configuration. Unknown JSON fields are
/// retained so a save from an older build does not discard settings introduced by a newer build.
/// </summary>
public sealed partial class FilekinSettingsStore
{
    private static readonly HashSet<string> ReservedLocationNames =
        new(StringComparer.OrdinalIgnoreCase) { "thisfolder", "selection", "parent" };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public FilekinSettingsStore()
        : this(DefaultSettingsPath)
    {
    }

    public FilekinSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        if (!Path.IsPathFullyQualified(settingsPath))
        {
            throw new ArgumentException("The settings path must be fully qualified.", nameof(settingsPath));
        }

        SettingsPath = Path.GetFullPath(settingsPath);
    }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductIdentity.Name,
        "settings.json");

    public string SettingsPath { get; }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new SettingsLoadResult(new FilekinSettings(), FileExists: false, IsMalformed: false, []);
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<FilekinSettings>(json, ReadOptions) ?? new FilekinSettings();
            var warnings = new List<string>();
            var normalized = Normalize(settings, warnings);
            return new SettingsLoadResult(normalized, FileExists: true, IsMalformed: false, warnings);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return MalformedResult("settings.json is malformed; Filekin left it unchanged and loaded no Locations.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return MalformedResult($"Could not read settings.json: {ex.Message}");
        }
    }

    public async Task SaveAsync(FilekinSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var warnings = new List<string>();
        var normalized = Normalize(settings, warnings);
        if (warnings.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", warnings), nameof(settings));
        }

        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, WriteOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static SettingsLoadResult MalformedResult(string warning) =>
        new(new FilekinSettings(), FileExists: true, IsMalformed: true, [warning]);

    private static FilekinSettings Normalize(FilekinSettings settings, List<string> warnings)
    {
        var locations = new List<SavedLocation>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sourceLocations = settings.Locations ?? [];
        for (var index = 0; index < sourceLocations.Count; index++)
        {
            var location = sourceLocations[index];
            if (location is null)
            {
                warnings.Add($"Location {index + 1} is empty and was ignored.");
                continue;
            }

            var name = location.Name.Trim();
            var path = location.Path.Trim();
            var position = index + 1;

            if (!TryNormalizeLocationName(name, out name, out var nameError))
            {
                warnings.Add($"Location {position} {nameError} and was ignored.");
                continue;
            }

            if (!TryNormalizeLocationPath(path, out var fullPath))
            {
                warnings.Add($"Location {position} does not have an absolute filesystem path and was ignored.");
                continue;
            }

            if (!names.Add(name))
            {
                warnings.Add($"Location {position} repeats the name '{name}' and was ignored.");
                continue;
            }

            locations.Add(new SavedLocation
            {
                Name = name,
                Path = fullPath,
                AdditionalProperties = location.AdditionalProperties,
            });
        }

        return settings with
        {
            Locations = locations,
            Theme = NormalizeTheme(settings.Theme, warnings),
            Accent = NormalizeAccent(settings.Accent, warnings),
            OpenFilesAtLaunch = NormalizeStartupLocation(settings.OpenFilesAtLaunch, warnings),
            InteractivePrograms = NormalizeInteractivePrograms(settings.InteractivePrograms, warnings),
            Archives = NormalizeArchives(settings.Archives, warnings),
        };
    }

    private static ArchiveSettings NormalizeArchives(ArchiveSettings? archives, List<string> warnings)
    {
        archives ??= new ArchiveSettings();

        var choice = (archives.WhenAFileExists ?? string.Empty).Trim().ToLowerInvariant();
        if (choice.Length == 0)
        {
            return archives with { WhenAFileExists = CollisionPreference.Skip };
        }

        if (CollisionPreference.IsKnown(choice))
        {
            return archives with { WhenAFileExists = choice };
        }

        warnings.Add(
            $"'{archives.WhenAFileExists}' is not a known archive collision choice; Filekin skipped existing files.");
        return archives with { WhenAFileExists = CollisionPreference.Skip };
    }

    private static string NormalizeTheme(string theme, List<string> warnings)
    {
        var normalized = (theme ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return ThemePreference.Dark;
        }

        if (ThemePreference.IsKnown(normalized))
        {
            return normalized;
        }

        warnings.Add($"'{theme}' is not a known theme; Filekin used the dark theme.");
        return ThemePreference.Dark;
    }

    /// <summary>
    /// Validates the shape of an accent name, not its membership. The app layer owns the list of
    /// accents and falls back to blue for one it does not know, so a name written by a newer build
    /// survives a round trip through an older one.
    /// </summary>
    private static string NormalizeAccent(string accent, List<string> warnings)
    {
        var normalized = (accent ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return "blue";
        }

        if (AccentNamePattern().IsMatch(normalized))
        {
            return normalized;
        }

        warnings.Add($"'{accent}' is not a usable accent name; Filekin used blue.");
        return "blue";
    }

    /// <summary>
    /// Validates the shape of the startup preference only. A configured folder or Location that does
    /// not currently resolve stays configured: availability is decided at launch, not at load, so a
    /// removable or network target can come back (ARCHITECTURE.md — "Startup Files Location").
    /// </summary>
    private static StartupLocation NormalizeStartupLocation(StartupLocation? startup, List<string> warnings)
    {
        if (startup is null)
        {
            return new StartupLocation();
        }

        var target = (startup.Target ?? string.Empty).Trim().ToLowerInvariant();
        if (target.Length == 0)
        {
            target = StartupTarget.Home;
        }

        if (!StartupTarget.IsKnown(target))
        {
            warnings.Add($"'{startup.Target}' is not a known startup target; Filekin opened Home.");
            return new StartupLocation { AdditionalProperties = startup.AdditionalProperties };
        }

        switch (target)
        {
            case StartupTarget.Location:
                if (!TryNormalizeLocationName(startup.Name ?? string.Empty, out var name, out var nameError))
                {
                    warnings.Add($"The startup Location name {nameError}; Filekin opened Home.");
                    return new StartupLocation { AdditionalProperties = startup.AdditionalProperties };
                }

                return startup with { Target = target, Name = name, Path = null };

            case StartupTarget.Folder:
                if (!TryNormalizeLocationPath(startup.Path ?? string.Empty, out var path))
                {
                    warnings.Add("The startup folder is not an absolute filesystem path; Filekin opened Home.");
                    return new StartupLocation { AdditionalProperties = startup.AdditionalProperties };
                }

                return startup with { Target = target, Name = null, Path = path };

            default:
                return startup with { Target = StartupTarget.Home, Name = null, Path = null };
        }
    }

    private static List<string> NormalizeInteractivePrograms(List<string>? programs, List<string> warnings)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var program in programs ?? [])
        {
            if (!TryNormalizeProgramName(program ?? string.Empty, out var name, out var error))
            {
                warnings.Add($"Interactive program '{program}' {error} and was ignored.");
                continue;
            }

            if (seen.Add(name))
            {
                normalized.Add(name);
            }
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes a user-registered interactive program to the same form the classifier compares
    /// against: no directory, no extension. <c>C:\tools\vim.exe</c> and <c>vim</c> are one rule.
    /// </summary>
    public static bool TryNormalizeProgramName(string program, out string normalizedName, out string error)
    {
        normalizedName = program.Trim().Trim('"');
        if (normalizedName.Length == 0)
        {
            error = "is empty";
            return false;
        }

        try
        {
            normalizedName = Path.GetFileNameWithoutExtension(normalizedName);
        }
        catch (ArgumentException)
        {
            error = "is not a usable program name";
            return false;
        }

        if (!ProgramNamePattern().IsMatch(normalizedName))
        {
            error = "must be a program name using only letters, numbers, '.', '_', '+' or '-'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static bool TryNormalizeLocationName(string name, out string normalizedName, out string error)
    {
        normalizedName = name.Trim();
        if (!LocationNamePattern().IsMatch(normalizedName))
        {
            error = "must use only letters, numbers, '_' or '-'";
            return false;
        }

        if (ReservedLocationNames.Contains(normalizedName))
        {
            error = $"'{normalizedName}' is reserved";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is an absolute filesystem path, normalized. Public because the
    /// Settings surface validates a chosen startup folder against exactly the same rule the file does.
    /// </summary>
    public static bool TryNormalizeLocationPath(string path, out string fullPath)
    {
        try
        {
            if (path.Length > 0 && Path.IsPathFullyQualified(path))
            {
                fullPath = Path.GetFullPath(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        fullPath = string.Empty;
        return false;
    }

    [GeneratedRegex(@"^[\p{L}\p{Nd}_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LocationNamePattern();

    [GeneratedRegex(@"^[\p{L}\p{Nd}._+-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ProgramNamePattern();

    [GeneratedRegex(@"^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccentNamePattern();
}
