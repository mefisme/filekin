using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.App.Theming;
using Filekin.Core.Commands;
using Filekin.Infrastructure.Windows.Commands;
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
    private bool _previewArchives = true;
    private bool _previewTidy = true;
    private bool _overwriteArchiveCollisions;
    private IReadOnlyList<WindowsPathEntryViewModel> _userPathRows = [];
    private string _newUserPath = string.Empty;
    private int _pathRowsGeneration;
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
        new(SettingsCategory.Archives, "Archives", "Preview and existing-file defaults."),
        new(SettingsCategory.Tidy, "Tidy", "Whether /tidy shows its plan first."),
        new(SettingsCategory.Advanced, "Advanced", "The readable file behind these settings, and where Windows looks for programs."),
    ];

    public bool IsAppearanceCategory => _settingsCategory == SettingsCategory.Appearance;

    public bool IsStartupCategory => _settingsCategory == SettingsCategory.Startup;

    public bool IsTerminalCategory => _settingsCategory == SettingsCategory.Terminal;

    public bool IsArchivesCategory => _settingsCategory == SettingsCategory.Archives;

    public bool IsTidyCategory => _settingsCategory == SettingsCategory.Tidy;

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

    /// <summary>Whether archive commands normally stop at the preview sheet.</summary>
    public bool PreviewArchives
    {
        get => _previewArchives;
        private set => SetProperty(ref _previewArchives, value);
    }

    /// <summary>Whether <c>/tidy</c> normally stops at its plan.</summary>
    public bool PreviewTidy
    {
        get => _previewTidy;
        private set => SetProperty(ref _previewTidy, value);
    }

    /// <summary>Whether archive commands normally replace an existing destination file.</summary>
    public bool OverwriteArchiveCollisions
    {
        get => _overwriteArchiveCollisions;
        private set => SetProperty(ref _overwriteArchiveCollisions, value);
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

    public IReadOnlyList<WindowsPathEntryViewModel> UserPathRows
    {
        get => _userPathRows;
        private set => SetProperty(ref _userPathRows, value);
    }

    /// <summary>A folder or executable path typed/pasted into Advanced Settings.</summary>
    public string NewUserPath
    {
        get => _newUserPath;
        set => SetProperty(ref _newUserPath, value);
    }

    /// <summary>Drives the "nothing here yet" line; WPF has no inverse of its boolean converter.</summary>
    public bool HasNoUserPathRows => _userPathRows.Count == 0;

    /// <summary>The header status line, in the same slot as the Places and Drives counts.</summary>
    public string SettingsStatus => Path.GetFileName(_settings.SettingsPath);

    /// <summary>Opens Settings over the preserved Files workspace and rebuilds it from the file.</summary>
    public void OpenSettings()
    {
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();
        CloseAgents();
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
        OnPropertyChanged(nameof(IsArchivesCategory));
        OnPropertyChanged(nameof(IsTidyCategory));
        OnPropertyChanged(nameof(IsAdvancedCategory));
        OnPropertyChanged(nameof(SettingsCategoryTitle));
        OnPropertyChanged(nameof(SettingsCategorySummary));
    }

    public async Task AddTypedUserPathAsync()
    {
        var typed = _newUserPath.Trim().Trim('"');
        if (typed.Length == 0)
        {
            ReportSettings("Type or paste a folder, or choose Browse.", isError: true);
            return;
        }

        var expanded = Environment.ExpandEnvironmentVariables(typed);
        var directory = File.Exists(expanded) ? Path.GetDirectoryName(expanded) : expanded;
        if (directory is null)
        {
            ReportSettings("That executable does not have a containing folder.", isError: true);
            return;
        }

        await AddUserPathFromSettingsAsync(directory).ConfigureAwait(true);
    }

    public Task AddBrowsedUserPathAsync(string folder) => AddUserPathFromSettingsAsync(folder);

    public void RequestRemoveUserPathEntry(WindowsPathEntryViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        RequestConfirmation(
            $"Remove \"{row.Path}\" from your Windows user PATH? Commands in it may stop working by name.",
            () => EditUserPathFromSettingsAsync(() => _userPathEditor.Remove(row.Entry)));
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

    /// <summary>Persists whether <c>/unzip</c> and <c>/zip</c> normally show their preview.</summary>
    public Task SetArchivePreviewAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SaveArchiveSettingsAsync(
            _settings.Current.Archives with { PreviewBeforeExtracting = enabled },
            enabled ? "Archive commands will show a preview." : "Archive commands will run without a preview.",
            cancellationToken);

    /// <summary>Persists the default collision choice shared by <c>/unzip</c> and <c>/zip</c>.</summary>
    public Task SetArchiveOverwriteAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SaveArchiveSettingsAsync(
            _settings.Current.Archives with
            {
                WhenAFileExists = enabled ? CollisionPreference.Overwrite : CollisionPreference.Skip,
            },
            enabled
                ? "Archive commands will replace existing files after recycling the originals."
                : "Archive commands will leave existing files alone.",
            cancellationToken);

    /// <summary>
    /// Persists whether <c>/tidy</c> normally shows its plan. Reachable from two places on purpose:
    /// the Tidy settings panel, and the "Don't show this again" tick inside the plan itself. The tick
    /// would otherwise be a one-way door — once used, the surface carrying it never opens again
    /// (owner decision, 2026-08-27).
    /// </summary>
    public async Task SetTidyPreviewAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var result = await _settings
            .UpdateAsync(
                current => current with { Tidy = current.Tidy with { PreviewBeforeTidying = enabled } },
                cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        RebuildTidySettings();
        ReportSettings(
            result.Succeeded
                ? enabled ? "/tidy will show its plan first." : "/tidy will organize without showing a plan."
                : result.Message,
            isError: !result.Succeeded);
    }

    /// <summary>Opens settings.json in whatever the user has associated with it.</summary>
    public async Task OpenSettingsFileAsync()
    {
        var path = _settings.SettingsPath;
        if (!await Task.Run(() => File.Exists(path)).ConfigureAwait(true))
        {
            ReportSettings("settings.json has not been written yet.", isError: true);
            return;
        }

        var result = await Task.Run(() => FileLauncher.TryOpen(path)).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            ReportSettings($"Could not open settings.json: {result.Message}", isError: true);
        }
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

    private async Task SaveArchiveSettingsAsync(
        ArchiveSettings archives,
        string confirmation,
        CancellationToken cancellationToken)
    {
        var result = await _settings
            .UpdateAsync(current => current with { Archives = archives }, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            RebuildArchiveSettings();
            ReportSettings(result.Message, isError: true);
            return;
        }

        RebuildArchiveSettings();
        ReportSettings(confirmation, isError: false);
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
        RebuildArchiveSettings();
        RebuildTidySettings();
        RebuildWindowsPathSettings();
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

    private void RebuildTidySettings() =>
        PreviewTidy = _settings.Current.Tidy.PreviewBeforeTidying;

    private void RebuildArchiveSettings()
    {
        PreviewArchives = _settings.Current.Archives.PreviewBeforeExtracting;
        OverwriteArchiveCollisions =
            _settings.Current.Archives.WhenAFileExists == CollisionPreference.Overwrite;
    }

    private async Task AddUserPathFromSettingsAsync(string directory)
    {
        ReportPendingUserPathEdit();
        var result = await Task.Run(() => _userPathEditor.AddDirectory(directory)).ConfigureAwait(true);
        if (result.Succeeded)
        {
            NewUserPath = string.Empty;
        }

        ApplyUserPathEdit(result, rememberChange: true);
    }

    private async Task EditUserPathFromSettingsAsync(Func<WindowsUserPathEditResult> edit)
    {
        ReportPendingUserPathEdit();
        var result = await Task.Run(edit).ConfigureAwait(true);
        ApplyUserPathEdit(result, rememberChange: true);
    }

    /// <summary>
    /// Reading PATH means asking whether every listed folder still exists, and a single stale network
    /// entry can stall that question for seconds, so the read never runs on the UI thread. A later
    /// read always wins, because an earlier one describes PATH before the edit that replaced it.
    /// </summary>
    private void RebuildWindowsPathSettings()
    {
        var generation = ++_pathRowsGeneration;
        _ = RefreshWindowsPathRowsAsync(generation);
    }

    private async Task RefreshWindowsPathRowsAsync(int generation)
    {
        IReadOnlyList<WindowsPathEntry> entries;
        try
        {
            entries = await Task.Run(_userPathEditor.GetSnapshot).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (generation == _pathRowsGeneration)
            {
                ReportSettings($"Could not read Windows PATH: {ex.Message}", isError: true);
            }

            return;
        }

        if (generation != _pathRowsGeneration)
        {
            return;
        }

        UserPathRows = [.. entries.Select(entry => new WindowsPathEntryViewModel(entry))];
        OnPropertyChanged(nameof(HasNoUserPathRows));
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
