using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.App.Theming;
using Filekin.Core.Commands;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Settings surface. It is a rich view over the preserved Files workspace, exactly like
/// <c>/places</c> and <c>/drives</c>: the sidebar entry and the <c>/settings</c> command open the
/// same thing, Esc or Back dismisses it, and the command bar stays usable underneath.
///
/// There is no Save button anywhere here. Every choice writes <c>settings.json</c> the moment it is
/// made and reports the failure inline if that write does not succeed, so the running UI and the
/// durable file can never disagree.
/// </summary>
public sealed partial class ShellViewModel
{
    private bool _isSettingsOpen;
    private SettingsCategory _settingsCategory = SettingsCategory.Appearance;
    private IReadOnlyList<SettingsOptionViewModel> _themeOptions = [];
    private IReadOnlyList<SettingsOptionViewModel> _accentOptions = [];
    private IReadOnlyList<SettingsOptionViewModel> _startupOptions = [];
    private IReadOnlyList<InteractiveProgramViewModel> _interactiveProgramRows = [];
    private string _newProgramName = string.Empty;
    private string _settingsMessage = string.Empty;
    private bool _settingsMessageIsError;

    /// <summary>Whether the Settings surface (<c>/settings</c>) is showing over the Files hierarchy.</summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set
        {
            if (SetProperty(ref _isSettingsOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>
    /// The category rail. Each entry owns a subject, so a preference added later joins one of these
    /// rather than lengthening the rail.
    /// </summary>
    public IReadOnlyList<SettingsCategoryViewModel> SettingsCategories { get; } =
    [
        new(SettingsCategory.Appearance, "Appearance", "Theme and accent colour."),
        new(SettingsCategory.Startup, "Startup", "Where Files opens when Filekin starts."),
        new(SettingsCategory.Terminal, "Terminal", "Which programs open in a terminal tab."),
        new(SettingsCategory.Advanced, "Advanced", "The readable file behind these settings."),
    ];

    public bool IsAppearanceCategory => _settingsCategory == SettingsCategory.Appearance;

    public bool IsStartupCategory => _settingsCategory == SettingsCategory.Startup;

    public bool IsTerminalCategory => _settingsCategory == SettingsCategory.Terminal;

    public bool IsAdvancedCategory => _settingsCategory == SettingsCategory.Advanced;

    /// <summary>The selected category's own one-line description, shown at the top of its panel.</summary>
    public string SettingsCategorySummary =>
        SettingsCategories.First(category => category.Key == _settingsCategory).Summary;

    public string SettingsCategoryTitle =>
        SettingsCategories.First(category => category.Key == _settingsCategory).Title;

    /// <summary>Dark, Light, and Follow system. Dark is Filekin's default.</summary>
    public IReadOnlyList<SettingsOptionViewModel> ThemeOptions
    {
        get => _themeOptions;
        private set => SetProperty(ref _themeOptions, value);
    }

    /// <summary>The accent colours, each with a swatch drawn in the theme now on screen.</summary>
    public IReadOnlyList<SettingsOptionViewModel> AccentOptions
    {
        get => _accentOptions;
        private set => SetProperty(ref _accentOptions, value);
    }

    /// <summary>Home, every saved Location by its current <c>@name</c>, then "Choose folder…".</summary>
    public IReadOnlyList<SettingsOptionViewModel> StartupOptions
    {
        get => _startupOptions;
        private set => SetProperty(ref _startupOptions, value);
    }

    /// <summary>The built-in interactive rules, then the ones the user added.</summary>
    public IReadOnlyList<InteractiveProgramViewModel> InteractiveProgramRows
    {
        get => _interactiveProgramRows;
        private set => SetProperty(ref _interactiveProgramRows, value);
    }

    /// <summary>The program-name box in the Terminal category (two-way).</summary>
    public string NewProgramName
    {
        get => _newProgramName;
        set => SetProperty(ref _newProgramName, value);
    }

    /// <summary>The last inline settings result: a confirmation, or the reason a write failed.</summary>
    public string SettingsMessage
    {
        get => _settingsMessage;
        private set
        {
            if (SetProperty(ref _settingsMessage, value))
            {
                OnPropertyChanged(nameof(HasSettingsMessage));
            }
        }
    }

    public bool HasSettingsMessage => _settingsMessage.Length > 0;

    public bool SettingsMessageIsError
    {
        get => _settingsMessageIsError;
        private set => SetProperty(ref _settingsMessageIsError, value);
    }

    /// <summary>The readable settings file, shown and openable in the Advanced category.</summary>
    public string SettingsPath => _settings.SettingsPath;

    /// <summary>The header status line, in the same slot as the Places and Drives counts.</summary>
    public string SettingsStatus => Path.GetFileName(_settings.SettingsPath);

    /// <summary>Opens Settings over the preserved Files workspace and rebuilds it from the file.</summary>
    public void OpenSettings()
    {
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = true;
        SettingsMessage = string.Empty;
        RebuildSettings();
    }

    /// <summary>Closes Settings and returns to the preserved Files hierarchy.</summary>
    public void CloseSettings() => IsSettingsOpen = false;

    public void SelectSettingsCategory(SettingsCategory category)
    {
        if (_settingsCategory == category)
        {
            return;
        }

        _settingsCategory = category;
        SettingsMessage = string.Empty;
        foreach (var row in SettingsCategories)
        {
            row.IsSelected = row.Key == category;
        }

        OnPropertyChanged(nameof(IsAppearanceCategory));
        OnPropertyChanged(nameof(IsStartupCategory));
        OnPropertyChanged(nameof(IsTerminalCategory));
        OnPropertyChanged(nameof(IsAdvancedCategory));
        OnPropertyChanged(nameof(SettingsCategoryTitle));
        OnPropertyChanged(nameof(SettingsCategorySummary));
    }

    /// <summary>Applies and persists a theme choice. The window re-colours before the write returns.</summary>
    public async Task SelectThemeAsync(SettingsOptionViewModel option, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.Value == _settings.Current.Theme)
        {
            return;
        }

        // Applied first so the choice is visibly instant; a failed write reverts it below rather
        // than leaving the window showing a theme the file does not contain.
        var previous = _settings.Current.Theme;
        ThemeManager.Apply(option.Value, _settings.Current.Accent);

        var result = await _settings
            .UpdateAsync(current => current with { Theme = option.Value }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ThemeManager.Apply(previous, _settings.Current.Accent);
            ReportSettings(result.Message, isError: true);
            return;
        }

        RebuildThemeOptions();

        // The swatches are drawn in the theme now on screen, so they are re-tinted with it.
        RebuildAccentOptions();
        ReportSettings(string.Empty, isError: false);
    }

    /// <summary>Applies and persists an accent choice.</summary>
    public async Task SelectAccentAsync(SettingsOptionViewModel option, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.Value == _settings.Current.Accent)
        {
            return;
        }

        var previous = _settings.Current.Accent;
        ThemeManager.Apply(_settings.Current.Theme, option.Value);

        var result = await _settings
            .UpdateAsync(current => current with { Accent = option.Value }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ThemeManager.Apply(_settings.Current.Theme, previous);
            ReportSettings(result.Message, isError: true);
            return;
        }

        RebuildAccentOptions();
        ReportSettings(string.Empty, isError: false);
    }

    /// <summary>
    /// Applies and persists a startup choice. "Choose folder…" is not applied here: the view asks
    /// for a folder first and then calls <see cref="SetStartupFolderAsync"/>.
    /// </summary>
    public async Task SelectStartupOptionAsync(
        SettingsOptionViewModel option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.RequiresFolderPick)
        {
            return;
        }

        var startup = option.Value == StartupTarget.Home
            ? new StartupLocation()
            : new StartupLocation { Target = StartupTarget.Location, Name = LocationNameOf(option.Value) };

        await SaveStartupAsync(startup, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Persists an explicitly chosen startup folder.</summary>
    public async Task SetStartupFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!FilekinSettingsStore.TryNormalizeLocationPath(folder, out var fullPath))
        {
            ReportSettings("A startup folder must be an absolute filesystem path.", isError: true);
            return;
        }

        await SaveStartupAsync(
            new StartupLocation { Target = StartupTarget.Folder, Path = fullPath },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Registers the typed program name as interactive, and clears the box on success.</summary>
    public async Task AddInteractiveProgramAsync(CancellationToken cancellationToken = default)
    {
        var typed = _newProgramName.Trim();
        if (typed.Length == 0)
        {
            return;
        }

        if (!FilekinSettingsStore.TryNormalizeProgramName(typed, out var name, out var error))
        {
            ReportSettings($"'{typed}' {error}.", isError: true);
            return;
        }

        if (InteractiveCommandRegistry.IsBuiltIn(name))
        {
            ReportSettings($"{name} already opens in a terminal — it is a built-in rule.", isError: true);
            return;
        }

        var programs = _settings.Current.InteractivePrograms;
        if (programs.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ReportSettings($"{name} is already in the list.", isError: true);
            return;
        }

        var updated = new List<string>(programs) { name };
        var result = await _settings
            .UpdateAsync(current => current with { InteractivePrograms = updated }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ReportSettings(result.Message, isError: true);
            return;
        }

        NewProgramName = string.Empty;
        ApplyInteractivePrograms();
        RebuildInteractivePrograms();
        ReportSettings($"{name} now opens in a terminal tab.", isError: false);
    }

    /// <summary>Removes one user-added program. Built-in rules are never removable.</summary>
    public async Task RemoveInteractiveProgramAsync(
        InteractiveProgramViewModel program,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!program.CanRemove)
        {
            return;
        }

        var updated = _settings.Current.InteractivePrograms
            .Where(name => !string.Equals(name, program.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = await _settings
            .UpdateAsync(current => current with { InteractivePrograms = updated }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ReportSettings(result.Message, isError: true);
            return;
        }

        ApplyInteractivePrograms();
        RebuildInteractivePrograms();
        ReportSettings($"{program.Name} runs as an ordinary command again.", isError: false);
    }

    /// <summary>Opens settings.json in whatever the user has associated with it.</summary>
    public void OpenSettingsFile()
    {
        if (!File.Exists(_settings.SettingsPath))
        {
            ReportSettings("settings.json has not been written yet.", isError: true);
            return;
        }

        FileLauncher.Open(_settings.SettingsPath);
    }

    /// <summary>Navigates Files to the folder holding settings.json and closes Settings.</summary>
    public async Task RevealSettingsFolderAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(_settings.SettingsPath);
        if (folder is null || !Directory.Exists(folder))
        {
            ReportSettings("The settings folder does not exist yet.", isError: true);
            return;
        }

        await NavigateToAsync(folder, cancellationToken).ConfigureAwait(true);
        IsSettingsOpen = false;
    }

    /// <summary>
    /// Pushes the loaded preferences into the parts of the shell that are not view models: the
    /// palette and the live command classifier. Called once at startup and after every change.
    /// </summary>
    private void ApplyPreferences()
    {
        ThemeManager.Apply(_settings.Current.Theme, _settings.Current.Accent);
        ApplyInteractivePrograms();
    }

    /// <summary>
    /// Re-resolves a <c>system</c> theme after Windows changed its app mode. It is a no-op for an
    /// explicit dark or light preference, so the window never flips under a user who chose one.
    /// </summary>
    public void ReapplySystemTheme()
    {
        if (_settings.Current.Theme != ThemePreference.System)
        {
            return;
        }

        ThemeManager.Apply(ThemePreference.System, _settings.Current.Accent);
        if (_isSettingsOpen)
        {
            RebuildThemeOptions();
            RebuildAccentOptions();
        }
    }

    private void ApplyInteractivePrograms() =>
        _interactiveCommands.ReplaceUserPrograms(_settings.Current.InteractivePrograms);

    private async Task SaveStartupAsync(StartupLocation startup, CancellationToken cancellationToken)
    {
        var result = await _settings
            .UpdateAsync(current => current with { OpenFilesAtLaunch = startup }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ReportSettings(result.Message, isError: true);
            return;
        }

        RebuildStartupOptions();
        ReportSettings(StartupConfirmation(startup), isError: false);
    }

    private static string StartupConfirmation(StartupLocation startup) => startup.Target switch
    {
        StartupTarget.Location => $"Files will open at @{startup.Name}.",
        StartupTarget.Folder => $"Files will open at {startup.Path}.",
        _ => "Files will open at Home.",
    };

    private void RebuildSettings()
    {
        foreach (var row in SettingsCategories)
        {
            row.IsSelected = row.Key == _settingsCategory;
        }

        RebuildThemeOptions();
        RebuildAccentOptions();
        RebuildStartupOptions();
        RebuildInteractivePrograms();
    }

    private void RebuildAccentOptions()
    {
        var current = _settings.Current.Accent;
        AccentOptions =
        [
            .. AccentPalette.All.Select(accent => new SettingsOptionViewModel(
                accent.Value,
                accent.Title,
                detail: string.Empty)
            {
                Swatch = ThemeManager.SwatchFor(accent),
                IsSelected = accent.Value == current,
            }),
        ];
    }

    private void RebuildThemeOptions()
    {
        var current = _settings.Current.Theme;
        var following = ThemeManager.Resolve(ThemePreference.System) == ThemePreference.Light ? "light" : "dark";

        ThemeOptions =
        [
            Option(ThemePreference.Dark, "Dark", "Filekin's default palette."),
            Option(ThemePreference.Light, "Light", "The same palette on a light ground."),
            Option(ThemePreference.System, "Follow system", $"Windows is asking for {following} right now."),
        ];

        SettingsOptionViewModel Option(string value, string title, string detail) =>
            new(value, title, detail) { IsSelected = value == current };
    }

    private void RebuildStartupOptions()
    {
        var startup = _settings.Current.OpenFilesAtLaunch;
        var options = new List<SettingsOptionViewModel>
        {
            new(StartupTarget.Home, "Home", StartupLocationResolver.HomePath)
            {
                IsSelected = startup.Target == StartupTarget.Home,
            },
        };

        // Saved Locations are offered by their current @name: choosing one means the startup target
        // follows later path changes to that Location (UX-DESIGN.md — "Startup Files Location").
        foreach (var location in _locationCatalog.Locations)
        {
            options.Add(new SettingsOptionViewModel($"location:{location.Name}", $"@{location.Name}", location.Path)
            {
                IsSelected = startup.Target == StartupTarget.Location &&
                    string.Equals(startup.Name, location.Name, StringComparison.OrdinalIgnoreCase),
            });
        }

        var chosenFolder = startup.Target == StartupTarget.Folder ? startup.Path : null;
        options.Add(new SettingsOptionViewModel(
            StartupTarget.Folder,
            "Choose folder…",
            chosenFolder ?? "Any folder on this PC.",
            requiresFolderPick: true)
        {
            IsSelected = chosenFolder is not null,
        });

        StartupOptions = options;

        // A configured Location that has since been removed leaves nothing selected. Say so instead
        // of silently showing Home as if the user had chosen it.
        if (startup.Target == StartupTarget.Location && !options.Any(option => option.IsSelected))
        {
            ReportSettings(
                $"@{startup.Name} is no longer saved, so Filekin opens Home. Choose a new target to replace it.",
                isError: true);
        }
    }

    private void RebuildInteractivePrograms()
    {
        var rows = InteractiveCommandRegistry.BuiltInPrograms
            .Select(static name => new InteractiveProgramViewModel(name, IsBuiltIn: true))
            .ToList();

        rows.AddRange(_settings.Current.InteractivePrograms
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static name => new InteractiveProgramViewModel(name, IsBuiltIn: false)));

        InteractiveProgramRows = rows;
    }

    /// <summary>Turns a <c>location:name</c> option value back into the Location name.</summary>
    private static string LocationNameOf(string optionValue) =>
        optionValue["location:".Length..];

    private void ReportSettings(string message, bool isError)
    {
        SettingsMessageIsError = isError;
        SettingsMessage = message;
    }
}
