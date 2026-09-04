using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Filekin.Core.Commands;
using Filekin.Core.Commands.References;
using Filekin.Core.FileSystem;
using Filekin.Core.Navigation;
using Filekin.Core.Operations;
using Filekin.Infrastructure.Windows.FileSystem;
using Filekin.Infrastructure.Windows.Navigation;
using Filekin.Infrastructure.Windows.References;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Files shell view model. It owns the current filesystem location, the listing shown in the Files
/// hierarchy, the active sort, the current selection, and the command bar. Filesystem enumeration and
/// command execution run off the UI thread (DECISIONS.md, 2026-08-24 — "UI Thread Must Remain
/// Responsive"); the listing is rebuilt on navigation, re-sort, and after a command that changed it.
///
/// Sidebar <see cref="Locations"/> are loaded from the readable user settings and share their names
/// with command-bar <c>@</c> references. All three built-in <see cref="Surfaces"/> — Places, Drives,
/// and Recycle Bin — are real rich views over the preserved Files state.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan TerminalFallbackDelay = TimeSpan.FromSeconds(2);

    private readonly IDirectoryLister _lister;
    private readonly CommandExecutor _executor;
    private readonly UserSettingsService _settings = new();
    private readonly InteractiveCommandRegistry _interactiveCommands = new();
    private readonly SettingsBackedLocationCatalog _locationCatalog;
    private readonly WindowsRecycleBin _recycleBin = new();
    private readonly IPlacesProvider _placesProvider = new WindowsPlacesProvider();
    private readonly IDrivesProvider _drivesProvider = new WindowsDrivesProvider();
    private readonly List<string> _history = [];
    /// <summary>The widest a tab title is ever drawn, however much room the strip has.</summary>
    private const double NaturalTabTitleWidth = 180;

    /// <summary>A tab's own furniture: its icon, its close button, and the padding around them.</summary>
    private const double TabFurnitureWidth = 78;

    /// <summary>What the strip holds besides tabs: the Files tab, the new-terminal button, dividers.</summary>
    private const double StripFurnitureWidth = 190;

    /// <summary>Narrower than this and a title says less than the icon beside it.</summary>
    private const double ShortestUsefulTitleWidth = 34;

    private readonly Dispatcher _dispatcher;
    private double _tabStripWidth;
    private double _tabTitleMaxWidth = NaturalTabTitleWidth;
    private bool _areTabTitlesShowing = true;
    private int _historyIndex;
    private string _historyDraft = string.Empty;

    private bool _isRecycleBinOpen;
    private IReadOnlyList<RecycledItemViewModel> _recycledItems = [];
    private List<RecycledItemViewModel> _selectedRecycledItems = [];
    private string _recycleBinStatus = string.Empty;
    private bool _isPlacesOpen;
    private IReadOnlyList<PlaceItemViewModel> _places = [];
    private string _placesStatus = string.Empty;
    private bool _isDrivesOpen;
    private IReadOnlyList<DriveItemViewModel> _drives = [];
    private string _drivesStatus = string.Empty;

    private bool _isConfirming;
    private string _confirmPrompt = string.Empty;
    private Func<Task>? _pendingConfirmAction;
    private Action? _pendingConfirmCancelAction;

    private IReadOnlyList<DirectoryEntry> _entries = [];
    private List<string> _selectionPaths = [];
    private string? _currentPath;

    private IReadOnlyList<FileRowViewModel> _files = [];
    private string _itemCount = string.Empty;
    private string _statusSelection = string.Empty;
    private string _statusFree = string.Empty;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private bool _sortDescending;

    private string _commandInput = string.Empty;
    private bool _isBusy;
    private bool _resultVisible;
    private string _resultGlyph = string.Empty;
    private CommandResultSeverity _resultSeverity = CommandResultSeverity.Info;
    private string _resultText = string.Empty;
    private bool _hasExpandableOutput;
    private string _outputText = string.Empty;
    private CancellationTokenSource? _activeCommandCancellation;
    private bool _terminalFallbackAccepted;
    private bool _isTerminalFallbackConfirmation;
    private bool _isFilesWorkspaceSelected = true;
    private AgentProjectTabViewModel? _selectedAgentProjectTab;
    private TerminalTabViewModel? _selectedTerminal;
    private bool _isLocationEditorOpen;
    private string _locationEditorTitle = string.Empty;
    private string _locationEditorName = string.Empty;
    private string _locationEditorPath = string.Empty;
    private string _locationEditorError = string.Empty;
    private string? _editingLocationName;

    public ShellViewModel()
        : this(new FileSystemDirectoryLister())
    {
    }

    public ShellViewModel(IDirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(lister);
        _lister = lister;

        // One settings owner for the whole shell: the Location catalog and the Settings surface
        // both read and write through it, so neither can clobber the other's half of the file.
        _locationCatalog = new SettingsBackedLocationCatalog(_settings);
        _executor = new CommandExecutor(
            new CompositeNamedLocationResolver(_locationCatalog, new WindowsKnownFolderLocations()),
            _locationCatalog,
            _locationCatalog,
            _interactiveCommands);

        // Captured at construction (on the UI thread) so terminal output is always marshalled to the
        // window's dispatcher, whichever thread later starts a session.
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public IReadOnlyList<FileRowViewModel> Files
    {
        get => _files;
        private set => SetProperty(ref _files, value);
    }

    public ObservableCollection<PathSegmentViewModel> PathSegments { get; } = [];

    /// <summary>Live hosted terminals. The Files workspace is permanent and is not in this collection.</summary>
    /// <remarks>
    /// This is every terminal, in the order they were opened, and it stays the one place they live.
    /// The tab strip shows them grouped — see <see cref="PlainTerminals"/> — but a second collection
    /// that owned some of them would be two truths about one tab.
    /// </remarks>
    public ObservableCollection<TerminalTabViewModel> TerminalTabs { get; } = [];

    /// <summary>
    /// The terminals the person opened for themselves, which are the ones no agent project owns.
    /// They sit in their own group at the end of the strip, after every project and its CLI tabs.
    /// </summary>
    public ObservableCollection<TerminalTabViewModel> PlainTerminals { get; } = [];

    /// <summary>Whether the strip needs the divider that separates agent groups from these.</summary>
    public bool HasPlainTerminals => PlainTerminals.Count > 0;

    /// <summary>
    /// Rebuilds the strip's grouping: each project tab keeps its own agent CLI tabs, and everything
    /// no project owns is the person's own.
    /// </summary>
    /// <remarks>
    /// It rebuilds rather than patches. The grouping is small, it changes only when a tab opens,
    /// closes, or stops being an agent's, and a patched version of this would be one more thing that
    /// can quietly disagree with <see cref="TerminalTabs"/>.
    /// </remarks>

    /// <summary>
    /// How wide a tab's title may be. Tabs shrink evenly as the strip fills, the way a browser's do,
    /// so opening one more never pushes the last one off the end.
    /// </summary>
    public double TabTitleMaxWidth
    {
        get => _tabTitleMaxWidth;
        private set => SetProperty(ref _tabTitleMaxWidth, value);
    }

    /// <summary>
    /// Whether tabs still have room for words. Below this the strip is down to icons, and the tab
    /// being read keeps its title anyway: a strip of unlabelled squares is not something to navigate.
    /// </summary>
    public bool AreTabTitlesShowing
    {
        get => _areTabTitlesShowing;
        private set => SetProperty(ref _areTabTitlesShowing, value);
    }

    /// <summary>
    /// Works out the share of the strip each tab may take. Called when the strip is resized and
    /// whenever the tabs change, because both move the share.
    /// </summary>
    /// <remarks>
    /// This replaces a horizontal scrollbar under the tabs. A bar there is a poor way to find a tab:
    /// it hides the thing being looked for, and it appears exactly when there is least room for it.
    /// Shrinking keeps every tab on screen and reachable by the pointer without scrolling first.
    ///
    /// The strip can still be scrolled by wheel or keyboard once even the icons overflow, which is
    /// far more tabs than fit on any window this is drawn on; it simply has no visible bar.
    /// </remarks>
    internal void MeasureTabStrip(double availableWidth)
    {
        if (availableWidth > 0)
        {
            _tabStripWidth = availableWidth;
        }

        var tabs = AgentProjectTabs.Count + TerminalTabs.Count;
        if (tabs == 0 || _tabStripWidth <= 0)
        {
            TabTitleMaxWidth = NaturalTabTitleWidth;
            AreTabTitlesShowing = true;
            return;
        }

        var share = Math.Max(0, _tabStripWidth - StripFurnitureWidth) / tabs;
        var title = Math.Clamp(share - TabFurnitureWidth, 0, NaturalTabTitleWidth);

        // A title clipped to two or three characters says less than the icon beside it and still
        // costs the width. Past that point the strip is honestly better off as icons.
        AreTabTitlesShowing = title >= ShortestUsefulTitleWidth;
        TabTitleMaxWidth = AreTabTitlesShowing ? title : 0;
    }

    private void RegroupTerminals()
    {
        // Ahead of the early return below: the strip's share moves with the tab count, and that
        // count has changed even when the grouping has not.
        MeasureTabStrip(0);
        foreach (var project in AgentProjectTabs)
        {
            var owned = TerminalTabs
                .Where(terminal => terminal.OwningProjectId == project.Project?.Id)
                .ToArray();
            if (project.Project is null || !project.CliTabs.SequenceEqual(owned))
            {
                project.CliTabs.Clear();
                foreach (var terminal in owned)
                {
                    project.CliTabs.Add(terminal);
                }
            }
        }

        var open = AgentProjectTabs
            .Select(project => project.Project?.Id)
            .Where(id => id is not null)
            .ToHashSet();
        var plain = TerminalTabs
            .Where(terminal => terminal.OwningProjectId is not { } owner || !open.Contains(owner))
            .ToArray();
        if (PlainTerminals.SequenceEqual(plain))
        {
            return;
        }

        PlainTerminals.Clear();
        foreach (var terminal in plain)
        {
            PlainTerminals.Add(terminal);
        }

        OnPropertyChanged(nameof(HasPlainTerminals));
    }

    /// <summary>
    /// Every workspace in the order the strip draws it: Files, then each agent project followed by
    /// the CLI tabs it owns, then the person's own terminals.
    /// </summary>
    /// <remarks>
    /// Ctrl+Tab reads this same list. Two orders drawn from the same tabs is how a keyboard walk
    /// starts skipping about the strip for no reason a person can see.
    /// </remarks>
    internal List<object> WorkspaceStrip()
    {
        RegroupTerminals();
        var strip = new List<object>();
        foreach (var project in AgentProjectTabs)
        {
            strip.Add(project);
            strip.AddRange(project.CliTabs);
        }

        strip.AddRange(PlainTerminals);
        return strip;
    }

    /// <summary>Persistent control-center tasks, one per exact agent-project folder.</summary>
    public ObservableCollection<AgentProjectTabViewModel> AgentProjectTabs { get; } = [];

    public bool IsFilesWorkspaceSelected
    {
        get => _isFilesWorkspaceSelected;
        private set
        {
            if (SetProperty(ref _isFilesWorkspaceSelected, value))
            {
                OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
                OnPropertyChanged(nameof(IsAgentsWorkspaceSelected));
                OnPropertyChanged(nameof(IsFilesOrAgentsWorkspaceSelected));
            }
        }
    }

    public bool IsTerminalWorkspaceSelected => !IsFilesWorkspaceSelected && SelectedTerminal is not null;

    public bool IsFilesOrAgentsWorkspaceSelected =>
        IsFilesWorkspaceSelected || IsAgentsWorkspaceSelected;

    public bool IsAgentsWorkspaceSelected =>
        !IsFilesWorkspaceSelected && SelectedAgentProjectTab is not null;

    public AgentProjectTabViewModel? SelectedAgentProjectTab
    {
        get => _selectedAgentProjectTab;
        private set
        {
            if (SetProperty(ref _selectedAgentProjectTab, value))
            {
                OnPropertyChanged(nameof(IsAgentsWorkspaceSelected));
                OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
                OnPropertyChanged(nameof(IsFilesOrAgentsWorkspaceSelected));
            }
        }
    }

    public TerminalTabViewModel? SelectedTerminal
    {
        get => _selectedTerminal;
        private set
        {
            if (SetProperty(ref _selectedTerminal, value))
            {
                OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
                OnPropertyChanged(nameof(IsAgentsWorkspaceSelected));
            }
        }
    }

    public string ItemCount
    {
        get => _itemCount;
        private set => SetProperty(ref _itemCount, value);
    }

    public string StatusSelection
    {
        get => _statusSelection;
        private set
        {
            if (SetProperty(ref _statusSelection, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>
    /// Selection feedback for the visible surface. Recycle Bin action selection is reported while
    /// that rich view is open; otherwise this is the underlying filesystem selection count.
    /// </summary>
    public string WorkspaceSelectionStatus => IsAgentsWorkspaceSelected
        ? AgentsStatus
        : _isRecycleBinOpen
            ? RecycleBinSelectionStatus()
            : _isPlacesOpen
                ? _placesStatus
                : _isDrivesOpen
                    ? _drivesStatus
                    : _isSettingsOpen
                        ? SettingsStatus
                        : _isInfoOpen
                            ? _infoStatus
                            : _isWhereOpen
                                ? _whereStatus
                                : _isArchiveOpen
                                    ? _archiveSummary
                                    : _statusSelection;

    public string StatusFree
    {
        get => _statusFree;
        private set => SetProperty(ref _statusFree, value);
    }

    /// <summary>The current folder, shown quietly as the command-bar prompt (UX-DESIGN.md).</summary>
    public string PromptPath => _currentPath ?? string.Empty;

    /// <summary>Copies the command bar's filesystem context without requiring text selection.</summary>
    public void CopyPromptPathToClipboard()
    {
        if (PromptPath.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(PromptPath);
            ShowNotice("Copied current path.");
        }
        catch (COMException)
        {
            ShowNotice("The clipboard is busy. Try copying the path again.");
        }
    }

    /// <summary>Shows compact product identity without replacing the current Files surface.</summary>
    public void ShowAbout() =>
        ShowNotice("Filekin · Keyboard-first Windows file manager + terminal · GPLv3");

    /// <summary>The command-bar input text (two-way).</summary>
    public string CommandInput
    {
        get => _commandInput;
        set => SetProperty(ref _commandInput, value);
    }

    /// <summary>True while a command is running, so the bar ignores re-entry.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool ResultVisible
    {
        get => _resultVisible;
        private set => SetProperty(ref _resultVisible, value);
    }

    public string ResultGlyph
    {
        get => _resultGlyph;
        private set => SetProperty(ref _resultGlyph, value);
    }

    public CommandResultSeverity ResultSeverity
    {
        get => _resultSeverity;
        private set => SetProperty(ref _resultSeverity, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>True when there is substantial output behind a <c>View</c> affordance.</summary>
    public bool HasExpandableOutput
    {
        get => _hasExpandableOutput;
        private set => SetProperty(ref _hasExpandableOutput, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    /// <summary>Whether the Recycle Bin view (<c>/recycle</c>) is showing over the Files hierarchy.</summary>
    public bool IsRecycleBinOpen
    {
        get => _isRecycleBinOpen;
        private set
        {
            if (SetProperty(ref _isRecycleBinOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>Whether the short system-folder view (<c>/places</c>) is showing.</summary>
    public bool IsPlacesOpen
    {
        get => _isPlacesOpen;
        private set
        {
            if (SetProperty(ref _isPlacesOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>Whether the assigned-drive view (<c>/drives</c>) is showing.</summary>
    public bool IsDrivesOpen
    {
        get => _isDrivesOpen;
        private set
        {
            if (SetProperty(ref _isDrivesOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>Whether the Files hierarchy (headers + list) is shown; hidden while a rich view is open.</summary>
    public bool IsFilesContentVisible =>
        !_isRecycleBinOpen && !_isPlacesOpen && !_isDrivesOpen && !_isSettingsOpen && !_isInfoOpen &&
        !_isWhereOpen && !_isArchiveOpen && !_isTidyOpen && !_isAgentsOpen && !_isAgentProjectsOpen;

    public IReadOnlyList<PlaceItemViewModel> Places
    {
        get => _places;
        private set => SetProperty(ref _places, value);
    }

    public string PlacesStatus
    {
        get => _placesStatus;
        private set
        {
            if (SetProperty(ref _placesStatus, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    public IReadOnlyList<DriveItemViewModel> Drives
    {
        get => _drives;
        private set => SetProperty(ref _drives, value);
    }

    public string DrivesStatus
    {
        get => _drivesStatus;
        private set
        {
            if (SetProperty(ref _drivesStatus, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    public IReadOnlyList<RecycledItemViewModel> RecycledItems
    {
        get => _recycledItems;
        private set => SetProperty(ref _recycledItems, value);
    }

    /// <summary>Whether the bin holds anything — gates the "Empty Recycle Bin" action.</summary>
    public bool HasRecycledItems => _recycledItems.Count > 0;

    /// <summary>Whether the Recycle Bin action bar has one or more rows to act on.</summary>
    public bool HasSelectedRecycledItems => _selectedRecycledItems.Count > 0;

    /// <summary>Whether an in-app "are you sure?" is waiting for a Y/N answer (shown below the command bar).</summary>
    public bool IsConfirming
    {
        get => _isConfirming;
        private set => SetProperty(ref _isConfirming, value);
    }

    /// <summary>The question shown in the in-app confirm strip.</summary>
    public string ConfirmPrompt
    {
        get => _confirmPrompt;
        private set => SetProperty(ref _confirmPrompt, value);
    }

    /// <summary>
    /// Asks the user, in-app, before running an irreversible <paramref name="onYes"/> action. The view
    /// shows <paramref name="prompt"/> below the command bar; Y runs it, N cancels — never an OS dialog.
    /// </summary>
    public void RequestConfirmation(string prompt, Func<Task> onYes, Action? onNo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(onYes);

        _pendingConfirmAction = onYes;
        _pendingConfirmCancelAction = onNo;
        ConfirmPrompt = prompt;
        IsConfirming = true;
    }

    /// <summary>Answers the pending confirm with "yes": runs the action and clears the strip.</summary>
    public async Task ConfirmYesAsync()
    {
        if (!_isConfirming)
        {
            return;
        }

        var action = _pendingConfirmAction;
        DismissConfirmation();
        if (action is not null)
        {
            await action().ConfigureAwait(true);
        }
    }

    /// <summary>Dismisses the pending confirm without doing anything (N or Esc).</summary>
    public void CancelConfirmation()
    {
        var onNo = _pendingConfirmCancelAction;
        DismissConfirmation();
        onNo?.Invoke();
    }

    private void DismissConfirmation()
    {
        _pendingConfirmAction = null;
        _pendingConfirmCancelAction = null;
        ConfirmPrompt = string.Empty;
        IsConfirming = false;
    }

    /// <summary>Asks before emptying the whole Recycle Bin (irreversible).</summary>
    public void RequestEmptyRecycleBin()
    {
        var count = _recycledItems.Count;
        var noun = count == 1 ? "1 item" : $"{count} items";
        RequestConfirmation($"Empty the Recycle Bin? {noun} deleted for good.", EmptyRecycleBinAsync);
    }

    /// <summary>Asks before permanently deleting the selected items (irreversible).</summary>
    public void RequestDeleteForever(IReadOnlyList<RecycledItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var selected = items.ToList();
        var prompt = selected.Count == 1
            ? $"Delete \"{selected[0].Name}\" for good?"
            : $"Delete {selected.Count} selected items for good?";
        RequestConfirmation(prompt, () => DeleteForeverAsync(selected));
    }

    public string RecycleBinStatus
    {
        get => _recycleBinStatus;
        private set => SetProperty(ref _recycleBinStatus, value);
    }

    // Sort-direction carets: the active column shows an up/down arrow, the others are blank
    // (DECISIONS.md, 2026-08-25 — a small caret on the active column shows the direction).
    public string TypeCaret => CaretFor(FileSortColumn.Type);

    public string NameCaret => CaretFor(FileSortColumn.Name);

    public string ModifiedCaret => CaretFor(FileSortColumn.Modified);

    public string SizeCaret => CaretFor(FileSortColumn.Size);

    public bool IsLocationEditorOpen
    {
        get => _isLocationEditorOpen;
        private set => SetProperty(ref _isLocationEditorOpen, value);
    }

    public string LocationEditorTitle
    {
        get => _locationEditorTitle;
        private set => SetProperty(ref _locationEditorTitle, value);
    }

    public string LocationEditorName
    {
        get => _locationEditorName;
        set => SetProperty(ref _locationEditorName, value);
    }

    public string LocationEditorPath
    {
        get => _locationEditorPath;
        set => SetProperty(ref _locationEditorPath, value);
    }

    public string LocationEditorError
    {
        get => _locationEditorError;
        private set => SetProperty(ref _locationEditorError, value);
    }

    public bool CanRemoveEditedLocation => _editingLocationName is not null;

    /// <summary>The ordered user-defined <c>@</c> Locations loaded from <c>settings.json</c>.</summary>
    public ObservableCollection<NavItem> Locations { get; } = [];

    public void BeginAddLocation()
    {
        _editingLocationName = null;
        LocationEditorTitle = "ADD LOCATION";
        LocationEditorName = string.Empty;
        LocationEditorPath = _currentPath ?? string.Empty;
        LocationEditorError = string.Empty;
        IsLocationEditorOpen = true;
        OnPropertyChanged(nameof(CanRemoveEditedLocation));
    }

    public void BeginEditLocation(NavItem location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.TargetPath is null)
        {
            return;
        }

        _editingLocationName = location.Name;
        LocationEditorTitle = "EDIT LOCATION";
        LocationEditorName = location.Name;
        LocationEditorPath = location.TargetPath;
        LocationEditorError = string.Empty;
        IsLocationEditorOpen = true;
        OnPropertyChanged(nameof(CanRemoveEditedLocation));
    }

    public void CancelLocationEditor()
    {
        _editingLocationName = null;
        LocationEditorError = string.Empty;
        IsLocationEditorOpen = false;
        OnPropertyChanged(nameof(CanRemoveEditedLocation));
    }

    public async Task SaveEditedLocationAsync(CancellationToken cancellationToken = default)
    {
        var result = _editingLocationName is null
            ? await _locationCatalog.AddAsync(
                LocationEditorName,
                ResolveLocationEditorPath(LocationEditorPath),
                cancellationToken).ConfigureAwait(true)
            : await _locationCatalog.UpdateAsync(
                _editingLocationName,
                LocationEditorName,
                ResolveLocationEditorPath(LocationEditorPath),
                cancellationToken).ConfigureAwait(true);

        ApplyLocationEditResult(result);
    }

    public async Task RemoveEditedLocationAsync(CancellationToken cancellationToken = default)
    {
        if (_editingLocationName is null)
        {
            return;
        }

        var result = await _locationCatalog.RemoveAsync(_editingLocationName, cancellationToken).ConfigureAwait(true);
        ApplyLocationEditResult(result);
    }

    public async Task RemoveLocationAsync(NavItem location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        var result = await _locationCatalog.RemoveAsync(location.Name, cancellationToken).ConfigureAwait(true);
        ApplyLocationEditResult(result);
    }

    /// <summary>
    /// The built-in Filekin surfaces: <c>/places</c>, <c>/drives</c>, <c>/recycle</c>, and — once any
    /// folder has agents set up — <c>/projects</c>. Each opens the same rich view as its slash
    /// command; the sidebar entry is a button, not a persistent selection.
    /// </summary>
    public ObservableCollection<NavItem> Surfaces { get; } =
    [
        new("/", "places", IsActive: false, SymbolAccent: true),
        new("/", "drives", IsActive: false, SymbolAccent: true),
        new("/", "recycle", IsActive: false, SymbolAccent: true),
    ];

    /// <summary>
    /// Publishes the surface list for what exists now. Only <c>/projects</c> comes and goes: it is
    /// worth a sidebar entry once there is a project to list, and is nothing but a dead end before
    /// that. It is added last so the three that are always there never move under the pointer.
    /// </summary>
    private void ShowSurfaces()
    {
        var listed = Surfaces.Any(surface => surface.Name == "projects");
        if (HasAgentProjects && !listed)
        {
            Surfaces.Add(new NavItem("/", "projects", IsActive: false, SymbolAccent: true));
        }
        else if (!HasAgentProjects && listed)
        {
            foreach (var surface in Surfaces.Where(surface => surface.Name == "projects").ToArray())
            {
                Surfaces.Remove(surface);
            }
        }
    }

    /// <summary>The workspace state intrinsic <c>@</c> references resolve against: current folder and selection.</summary>
    public ReferenceContext BuildReferenceContext() => new(_currentPath, _selectionPaths);

    /// <summary>Runs the current command-bar line and applies its adaptive result.</summary>
    public async Task ExecuteCommandAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var input = _commandInput.Trim();
        if (input.Length == 0)
        {
            return;
        }

        // Captured once: navigation can move Files while the command is in flight, and the whole
        // execution — including a later terminal relaunch — must stay anchored to the folder the
        // user typed the command in.
        if (_currentPath is not { } commandFolder)
        {
            ShowNotice("The command bar needs a filesystem folder.");
            return;
        }

        AddToHistory(input);
        CommandInput = string.Empty;
        IsBusy = true;
        ShowRunning();

        var referenceContext = BuildReferenceContext();
        using var commandCancellation = new CancellationTokenSource();
        _activeCommandCancellation = commandCancellation;
        _terminalFallbackAccepted = false;

        try
        {
            var execution = _executor.ExecuteAsync(
                input,
                referenceContext,
                commandFolder,
                commandCancellation.Token);

            await OfferTerminalFallbackIfStillRunningAsync(
                execution,
                input,
                referenceContext,
                commandFolder,
                commandCancellation).ConfigureAwait(true);

            var outcome = await execution.ConfigureAwait(true);
            DismissTerminalFallbackConfirmation();
            if (outcome.AppCommandExecution is { } appCommandExecution)
            {
                string? warning = null;
                if (TossOperationHistory.TryCreate(appCommandExecution) is { } tossHistory)
                {
                    warning = await TryRecordOperationAsync(
                            "toss",
                            tossHistory.Summary,
                            tossHistory.Payload,
                            tossHistory.CanRestore,
                            undoStatusDetail: tossHistory.RestoreUnavailableReason)
                        .ConfigureAwait(true);
                }
                else if (CopyOperationHistory.TryCreate(appCommandExecution) is { } copyHistory)
                {
                    warning = await TryRecordOperationAsync(
                            "copy",
                            copyHistory.Summary,
                            copyHistory.Payload,
                            canUndo: false)
                        .ConfigureAwait(true);
                }
                else if (RelocationOperationHistory.TryCreate(appCommandExecution) is { } relocationHistory)
                {
                    warning = await TryRecordOperationAsync(
                            relocationHistory.Kind,
                            relocationHistory.Summary,
                            relocationHistory.Payload,
                            canUndo: true)
                        .ConfigureAwait(true);
                }

                if (warning is not null)
                {
                    outcome = outcome.AppendText(warning);
                }
            }

            ApplyLocations(_locationCatalog.Locations);
            ApplyResult(outcome);

            if (ApplyTerminalLaunches(outcome))
            {
                return;
            }

            if (outcome.InfoTargets is { } infoTargets)
            {
                await OpenInfoAsync(infoTargets).ConfigureAwait(true);
                return;
            }

            if (outcome.WhereRequest is { } whereRequest)
            {
                await OpenWhereAsync(whereRequest).ConfigureAwait(true);
                return;
            }

            if (outcome.UnzipRequest is { } unzipRequest)
            {
                if (IsArchiveBusy)
                {
                    ApplyResult(CommandExecutionOutcome.Notice(
                        "An archive operation is already running. Use View or Stop before starting another."));
                    return;
                }

                CloseArchive();
                CloseTidy();
                await OpenUnzipAsync(unzipRequest).ConfigureAwait(true);
                return;
            }

            if (outcome.ZipRequest is { } zipRequest)
            {
                if (IsArchiveBusy)
                {
                    ApplyResult(CommandExecutionOutcome.Notice(
                        "An archive operation is already running. Use View or Stop before starting another."));
                    return;
                }

                CloseArchive();
                CloseTidy();
                await OpenZipAsync(zipRequest).ConfigureAwait(true);
                return;
            }

            if (outcome.TidyRequest is { } tidyRequest)
            {
                if (IsTidyBusy)
                {
                    ApplyResult(CommandExecutionOutcome.Notice(
                        "A tidy is already running. Wait for it to finish before starting another."));
                    return;
                }

                await OpenTidyAsync(tidyRequest).ConfigureAwait(true);
                return;
            }

            if (outcome.OpensRecycleBin)
            {
                await OpenRecycleBinAsync().ConfigureAwait(true);
                return;
            }

            if (outcome.OpensPlaces)
            {
                await OpenPlacesAsync().ConfigureAwait(true);
                return;
            }

            if (outcome.OpensDrives)
            {
                await OpenDrivesAsync().ConfigureAwait(true);
                return;
            }

            if (outcome.OpensSettings)
            {
                OpenSettings();
                return;
            }

            if (outcome.OpensAgents)
            {
                await OpenAgentsAsync().ConfigureAwait(true);
                return;
            }

            if (outcome.OpensAgentProjects)
            {
                await OpenAgentProjectsAsync().ConfigureAwait(true);
                return;
            }

            if (outcome.RemoveAgentProjectTarget is { } removeTarget)
            {
                var removeResult = await RemoveAgentProjectByCommandAsync(removeTarget)
                    .ConfigureAwait(true);
                ApplyResult(removeResult);
                return;
            }

            // Decide where Files should sit after the command: a cd moves us; otherwise re-list the
            // current folder if it may have changed. The move is measured against the folder the
            // command ran in, so navigating elsewhere mid-command is not undone by a command that
            // never changed the shell location.
            string? destination = null;
            if (outcome.NewFolderPath is { } newFolder &&
                !string.Equals(newFolder, commandFolder, StringComparison.OrdinalIgnoreCase))
            {
                destination = newFolder;
            }
            else if (outcome.RefreshListing)
            {
                destination = _currentPath;
            }

            // If the destination no longer exists — e.g. the current folder was moved or deleted with
            // @thisfolder — fall back to the nearest existing ancestor, down to the drive root.
            if (destination is not null && !Directory.Exists(destination))
            {
                destination = NearestExistingAncestor(destination);
            }

            if (destination is not null)
            {
                await NavigateToAsync(destination).ConfigureAwait(true);
            }

            // The command bar remains usable in rich views. A finite PowerShell command can mutate
            // the Recycle Bin (notably Clear-RecycleBin -Force), so keep the visible lens current
            // instead of disabling the command surface or showing stale rows.
            if (_isRecycleBinOpen)
            {
                await RefreshRecycleBinAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_terminalFallbackAccepted)
        {
            DismissTerminalFallbackConfirmation();
            RelaunchInTerminal(input, referenceContext, commandFolder);
        }
        catch (OperationCanceledException)
        {
            DismissTerminalFallbackConfirmation();
            ApplyResult(CommandExecutionOutcome.Notice("Command stopped."));
        }
#pragma warning disable CA1031 // The command bar must never crash the shell on an unexpected failure.
        catch (Exception ex)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, $"Command failed: {ex.Message}"));
        }
#pragma warning restore CA1031
        finally
        {
            if (ReferenceEquals(_activeCommandCancellation, commandCancellation))
            {
                _activeCommandCancellation = null;
            }

            _terminalFallbackAccepted = false;
            IsBusy = false;
        }
    }

    /// <summary>Stops the finite command currently occupying the command bar.</summary>
    public void CancelActiveCommand() => _activeCommandCancellation?.Cancel();

    /// <summary>
    /// Watches a still-running command for <see cref="TerminalFallbackDelay"/> and then offers the
    /// one-time fresh-relaunch prompt. Only a concrete Windows console target is ever offered, and
    /// nothing is offered once the command has finished on its own or the user has already stopped it.
    /// </summary>
    private async Task OfferTerminalFallbackIfStillRunningAsync(
        Task<CommandExecutionOutcome> execution,
        string input,
        ReferenceContext referenceContext,
        string commandFolder,
        CancellationTokenSource commandCancellation)
    {
        // Recognizing the target walks PATH and reads PE headers, so it never runs on the UI thread.
        var offersFallback = await Task.Run(
            () => _executor.ShouldOfferTerminalFallback(input, referenceContext, commandFolder))
            .ConfigureAwait(true);
        if (!offersFallback || execution.IsCompleted || commandCancellation.IsCancellationRequested)
        {
            return;
        }

        // The delay observes the same token, so Esc ends the wait immediately instead of leaving a
        // prompt to appear seconds after the user stopped the command.
        await Task.WhenAny(
            execution,
            Task.Delay(TerminalFallbackDelay, commandCancellation.Token)).ConfigureAwait(true);

        if (execution.IsCompleted || commandCancellation.IsCancellationRequested)
        {
            return;
        }

        RequestTerminalFallback(input, commandCancellation);
    }

    /// <summary>
    /// Starts the stopped command again as a fresh process in a hosted terminal. This runs inside a
    /// catch block of the command-bar entry point, so a launch failure is reported inline rather than
    /// thrown into the async event handler that started the command.
    /// </summary>
    private void RelaunchInTerminal(string input, ReferenceContext referenceContext, string commandFolder)
    {
        try
        {
            var outcome = _executor.StartInTerminal(input, referenceContext, commandFolder);
            ApplyResult(outcome);
            _ = ApplyTerminalLaunches(outcome);
        }
#pragma warning disable CA1031 // A relaunch failure is an ordinary command error, never a shell crash.
        catch (Exception ex)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error,
                $"Could not run {CommandLabel(input)} in a terminal: {ex.Message}"));
        }
#pragma warning restore CA1031
    }

    private void RequestTerminalFallback(string input, CancellationTokenSource commandCancellation)
    {
        _isTerminalFallbackConfirmation = true;
        RequestConfirmation(
            $"{CommandLabel(input)} is still running. Run it again in a terminal tab?",
            () =>
            {
                _isTerminalFallbackConfirmation = false;
                _terminalFallbackAccepted = true;
                commandCancellation.Cancel();
                return Task.CompletedTask;
            },
            () =>
            {
                _isTerminalFallbackConfirmation = false;
                ShowRunningWithStopHint(input);
            });
    }

    private void DismissTerminalFallbackConfirmation()
    {
        if (!_isTerminalFallbackConfirmation)
        {
            return;
        }

        _isTerminalFallbackConfirmation = false;
        DismissConfirmation();
    }

    private bool ApplyTerminalLaunches(CommandExecutionOutcome outcome)
    {
        foreach (var terminal in outcome.TerminalLaunches)
        {
            AddTerminal(terminal.Title, terminal.Session);
        }

        return outcome.TerminalLaunches.Count > 0;
    }

    private static string CommandLabel(string input)
    {
        var firstSpace = input.IndexOfAny([' ', '\t', '\r', '\n']);
        return firstSpace < 0 ? input : input[..firstSpace];
    }

    /// <summary>Opens an external terminal at the current folder (the GUI half of the escape hatch).</summary>
    public void OpenExternalTerminal()
    {
        if (_currentPath is null)
        {
            return;
        }

        try
        {
            _executor.ExternalLauncher.OpenTerminal(_currentPath);
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Success, "Opened an external terminal here."));
        }
        catch (InvalidOperationException ex)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, ex.Message));
        }
    }

    /// <summary>Starts a plain hosted PowerShell at the current Files location.</summary>
    public void OpenPowerShellTab()
    {
        if (_currentPath is null)
        {
            return;
        }

        try
        {
            var outcome = _executor.StartPowerShell(_currentPath);
            _ = ApplyTerminalLaunches(outcome);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, ex.Message));
            SelectFilesWorkspace();
        }
    }

    public void SelectFilesWorkspace()
    {
        SaveSelectedAgentProjectTabState();
        IsFilesWorkspaceSelected = true;
        IsAgentsOpen = false;
        SelectedAgentProjectTab = null;
        SelectedTerminal = null;
        foreach (var project in AgentProjectTabs)
        {
            project.IsSelected = false;
        }

        foreach (var terminal in TerminalTabs)
        {
            terminal.IsSelected = false;
        }
    }

    public void SelectTerminal(TerminalTabViewModel terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (!TerminalTabs.Contains(terminal))
        {
            return;
        }

        SaveSelectedAgentProjectTabState();
        IsFilesWorkspaceSelected = false;
        IsAgentsOpen = false;
        SelectedAgentProjectTab = null;
        SelectedTerminal = terminal;
        foreach (var project in AgentProjectTabs)
        {
            project.IsSelected = false;
        }

        foreach (var candidate in TerminalTabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, terminal);
        }
    }

    /// <summary>
    /// Moves one workspace forward or back for Ctrl+Tab. The order matches the tab strip: the
    /// permanent Files workspace first, then agent projects and live terminals, cycling at both
    /// ends. A coordinated agent session is a terminal tab like any other.
    /// </summary>
    public void SelectAdjacentWorkspace(bool forward)
    {
        if (AgentProjectTabs.Count == 0 && TerminalTabs.Count == 0)
        {
            return;
        }

        var strip = WorkspaceStrip();
        var count = strip.Count + 1;
        object? selected = SelectedAgentProjectTab is { } project
            ? project
            : SelectedTerminal;
        var current = IsFilesWorkspaceSelected || selected is null
            ? 0
            : strip.IndexOf(selected) + 1;
        var next = (((current + (forward ? 1 : -1)) % count) + count) % count;
        SelectWorkspaceAt(next);
    }

    private void SelectWorkspaceAt(int index)
    {
        var strip = WorkspaceStrip();
        if (index <= 0 || index > strip.Count)
        {
            SelectFilesWorkspace();
            return;
        }

        switch (strip[index - 1])
        {
            case AgentProjectTabViewModel project:
                SelectAgentProjectTab(project);
                break;
            case TerminalTabViewModel terminal:
                SelectTerminal(terminal);
                break;
        }
    }

    public async Task CloseTerminalAsync(TerminalTabViewModel terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var index = TerminalTabs.IndexOf(terminal);
        if (index < 0)
        {
            return;
        }

        terminal.RootShellExited -= OnTerminalRootShellExited;
        terminal.AgentProcessExited -= OnAgentProcessExited;
        TerminalTabs.RemoveAt(index);
        RegroupTerminals();
        if (ReferenceEquals(SelectedTerminal, terminal))
        {
            if (TerminalTabs.Count == 0)
            {
                SelectFilesWorkspace();
            }
            else
            {
                SelectTerminal(TerminalTabs[Math.Min(index, TerminalTabs.Count - 1)]);
            }
        }

        var agentSession = terminal.AgentSession;
        await terminal.DisposeAsync().ConfigureAwait(true);
        if (agentSession is not null)
        {
            await RefreshAfterAgentProcessEndedAsync(agentSession).ConfigureAwait(true);
        }
    }

    internal void AddTerminal(
        string title,
        Filekin.Core.Terminal.ITerminalSession session,
        AgentTerminalIdentity? agentSession = null,
        IAsyncDisposable? agentSessionLifetime = null)
    {
        var uniqueTitle = DisambiguateTerminalTitle(title);
        var terminal = new TerminalTabViewModel(
            uniqueTitle,
            session,
            _dispatcher,
            agentSession,
            agentSessionLifetime);
        terminal.RootShellExited += OnTerminalRootShellExited;
        terminal.AgentProcessExited += OnAgentProcessExited;
        TerminalTabs.Add(terminal);
        RegroupTerminals();
        SelectTerminal(terminal);
    }

    private string DisambiguateTerminalTitle(string title)
    {
        if (!TerminalTabs.Any(tab => string.Equals(tab.Title, title, StringComparison.OrdinalIgnoreCase)))
        {
            return title;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{title} · {suffix}";
            if (!TerminalTabs.Any(tab => string.Equals(tab.Title, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private async void OnTerminalRootShellExited(object? sender, Filekin.Core.Terminal.TerminalExitEventArgs e)
    {
        if (sender is TerminalTabViewModel terminal)
        {
            await CloseTerminalAsync(terminal).ConfigureAwait(true);
        }
    }

    private async void OnAgentProcessExited(object? sender, EventArgs e)
    {
        if (sender is not TerminalTabViewModel terminal || !TerminalTabs.Contains(terminal))
        {
            return;
        }

        var identity = await terminal.CompleteAgentProcessAsync().ConfigureAwait(true);
        RegroupTerminals();
        if (identity is not null)
        {
            await RefreshAfterAgentProcessEndedAsync(identity).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the Recycle Bin view and loads its contents.</summary>
    public async Task OpenRecycleBinAsync()
    {
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();
        IsAgentsOpen = false;
        IsAgentProjectsOpen = false;
        IsRecycleBinOpen = true;
        await RefreshRecycleBinAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the short common-folder/cloud-root view and refreshes its destinations.</summary>
    public async Task OpenPlacesAsync(CancellationToken cancellationToken = default)
    {
        IsRecycleBinOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();
        IsAgentsOpen = false;
        IsAgentProjectsOpen = false;
        IsPlacesOpen = true;
        await RefreshPlacesAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the assigned-drive view and refreshes its capacity information.</summary>
    public async Task OpenDrivesAsync(CancellationToken cancellationToken = default)
    {
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        CloseTidy();
        IsAgentsOpen = false;
        IsAgentProjectsOpen = false;
        IsDrivesOpen = true;
        await RefreshDrivesAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Refreshes the filesystem state owned by the Files workspace and its currently visible rich
    /// view. Window activation calls this today; future real tab activation should call the same
    /// boundary when the user returns to Files after working in a terminal tab.
    /// </summary>
    public async Task<WorkspaceRefreshResult> RefreshWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_isBusy)
        {
            return default;
        }

        var filesChanged = await RefreshFilesAsync(cancellationToken).ConfigureAwait(true);

        // At most one rich view is open at a time, so the short-circuit only ever runs one refresh.
        var richViewChanged =
            (_isRecycleBinOpen && await RefreshRecycleBinAsync().ConfigureAwait(true)) ||
            (_isPlacesOpen && await RefreshPlacesAsync(cancellationToken).ConfigureAwait(true)) ||
            (_isDrivesOpen && await RefreshDrivesAsync(cancellationToken).ConfigureAwait(true)) ||
            (_isAgentProjectsOpen && await RefreshAgentProjectsAsync(cancellationToken).ConfigureAwait(true));

        return new WorkspaceRefreshResult(filesChanged, richViewChanged);
    }

    /// <summary>Closes the Recycle Bin view and returns to the Files hierarchy.</summary>
    public void CloseRecycleBin() => IsRecycleBinOpen = false;

    /// <summary>Closes Places and returns to the preserved Files hierarchy.</summary>
    public void ClosePlaces() => IsPlacesOpen = false;

    /// <summary>Closes Drives and returns to the preserved Files hierarchy.</summary>
    public void CloseDrives() => IsDrivesOpen = false;

    /// <summary>Navigates to a Places row and dismisses the temporary surface on success.</summary>
    public async Task NavigateToPlaceAsync(PlaceItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await NavigateToAsync(item.Path, cancellationToken).ConfigureAwait(true);
        if (string.Equals(_currentPath, Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase))
        {
            IsPlacesOpen = false;
        }
    }

    /// <summary>
    /// Navigates to a drive root and dismisses the temporary surface on success. An unavailable
    /// drive is a visible row but never a navigation action.
    /// </summary>
    public async Task NavigateToDriveAsync(DriveItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsAvailable)
        {
            return;
        }

        await NavigateToAsync(item.Root, cancellationToken).ConfigureAwait(true);
        if (string.Equals(_currentPath, Path.GetFullPath(item.Root), StringComparison.OrdinalIgnoreCase))
        {
            IsDrivesOpen = false;
        }
    }

    /// <summary>Restores the selected recycled items, then refreshes both affected surfaces once.</summary>
    public async Task RestoreAsync(IReadOnlyList<RecycledItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var selected = items.ToList();
        var restored = await Task.Run(() =>
        {
            var restoredItems = new List<RecycledItemViewModel>();
            foreach (var item in selected)
            {
                if (_recycleBin.Restore(item.Item))
                {
                    restoredItems.Add(item);
                }
            }

            return restoredItems;
        }).ConfigureAwait(true);

        await RefreshRecycleBinAsync().ConfigureAwait(true);

        // If an item came back into the folder Files is showing, re-list it so it reappears when the
        // rich view closes. The location itself remains unchanged.
        if (_currentPath is not null && restored.Any(item =>
                string.Equals(
                    Path.GetDirectoryName(item.Item.OriginalPath),
                    _currentPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            await NavigateToAsync(_currentPath).ConfigureAwait(true);
        }
    }

    /// <summary>Permanently deletes the selected recycled items, then refreshes the view once.</summary>
    public async Task DeleteForeverAsync(IReadOnlyList<RecycledItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var selected = items.ToList();
        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                _ = _recycleBin.DeleteForever(item.Item);
            }
        }).ConfigureAwait(true);
        await RefreshRecycleBinAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Permanently empties the whole Recycle Bin, then refreshes the view. The caller confirms first —
    /// this cannot be undone.
    /// </summary>
    public async Task EmptyRecycleBinAsync()
    {
        try
        {
            await Task.Run(_recycleBin.Empty).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Surface a shell failure as a status line, never crash the view.
        catch (Exception)
        {
            RecycleBinStatus = "Could not empty the Recycle Bin";
        }
#pragma warning restore CA1031

        await RefreshRecycleBinAsync().ConfigureAwait(true);
    }

    /// <summary>Reloads Places. Returns whether the published rows actually changed.</summary>
    private async Task<bool> RefreshPlacesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlaceLocation> locations;
        try
        {
            locations = await Task.Run(_placesProvider.GetPlaces, cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // One discovery failure must not crash the Files workspace.
        catch (Exception)
        {
            locations = [];
        }
#pragma warning restore CA1031

        var items = new List<PlaceItemViewModel>(locations.Count);
        PlaceKind? previousKind = null;
        foreach (var location in locations)
        {
            items.Add(new PlaceItemViewModel(location, location.Kind != previousKind));
            previousKind = location.Kind;
        }

        // Rebinding an unchanged list would throw away the row the keyboard is on, so only publish
        // when the destinations really differ.
        if (_places.Select(static place => place.Place).SequenceEqual(locations))
        {
            return false;
        }

        Places = items;
        PlacesStatus = items.Count switch
        {
            0 => "No places available",
            1 => "1 destination",
            var count => $"{count} destinations",
        };
        return true;
    }

    /// <summary>Reloads Drives. Returns whether the published rows actually changed.</summary>
    private async Task<bool> RefreshDrivesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DriveLocation> drives;
        try
        {
            drives = await Task.Run(_drivesProvider.GetDrives, cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // One enumeration failure must not crash the Files workspace.
        catch (Exception)
        {
            drives = [];
        }
#pragma warning restore CA1031

        if (_drives.Select(static drive => drive.Drive).SequenceEqual(drives))
        {
            return false;
        }

        Drives = [.. drives.Select(static drive => new DriveItemViewModel(drive))];
        var available = Drives.Count(static drive => drive.IsAvailable);
        DrivesStatus = Drives.Count switch
        {
            0 => "No drives found",
            var count when count == available => count == 1 ? "1 drive" : $"{count} drives",
            var count => $"{count} drives · {count - available} unavailable",
        };
        return true;
    }

    private async Task<bool> RefreshRecycleBinAsync()
    {
        IReadOnlyList<RecycledItem> items;
        try
        {
            items = await Task.Run(_recycleBin.List).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Never let a shell-enumeration failure crash the view.
        catch (Exception)
        {
            items = [];
        }
#pragma warning restore CA1031

        var refreshedItems = items
            .OrderByDescending(static i => i.DeletedWhen ?? DateTime.MinValue)
            .Select(static i => new RecycledItemViewModel(i))
            .ToList();

        var changed = !RecycledItems.Select(static item => item.Item)
            .SequenceEqual(refreshedItems.Select(static item => item.Item));
        if (changed)
        {
            RecycledItems = refreshedItems;
            SetRecycleBinSelection([]);
            OnPropertyChanged(nameof(HasRecycledItems));
        }

        RecycleBinStatus = refreshedItems.Count switch
        {
            0 => "Recycle Bin is empty",
            1 => "1 item",
            var n => $"{n} items",
        };

        return changed;
    }

    /// <summary>Recalls the previous entered command into the input (Up arrow).</summary>
    public void RecallPreviousCommand()
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex >= _history.Count)
        {
            _historyDraft = CommandInput;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandInput = _history[_historyIndex];
    }

    /// <summary>Recalls the next entered command, or clears to a fresh line (Down arrow).</summary>
    public void RecallNextCommand()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
        CommandInput = _historyIndex >= _history.Count ? _historyDraft : _history[_historyIndex];
    }

    private void AddToHistory(string input)
    {
        if (_history.Count == 0 || !string.Equals(_history[^1], input, StringComparison.Ordinal))
        {
            _history.Add(input);
        }

        _historyIndex = _history.Count;
        _historyDraft = string.Empty;
    }

    private void ShowRunning()
    {
        SetArchiveUndoResultAssociation(matches: false);
        ResultVisible = true;
        ResultSeverity = CommandResultSeverity.Info;
        ResultGlyph = "…";
        ResultText = "Running…";
        HasExpandableOutput = false;
        OutputText = string.Empty;
    }

    private void ShowRunningWithStopHint(string input)
    {
        ResultVisible = true;
        ResultSeverity = CommandResultSeverity.Info;
        ResultGlyph = "…";
        ResultText = $"{CommandLabel(input)} is still running · Esc to stop";
        HasExpandableOutput = false;
        OutputText = string.Empty;
    }

    private void ShowNotice(string text) =>
        ApplyResult(CommandExecutionOutcome.Notice(text));

    private void ApplyResult(CommandExecutionOutcome outcome)
    {
        SetArchiveUndoResultAssociation(matches: false);
        if (outcome.Display == CommandResultDisplay.None)
        {
            ResultVisible = false;
            return;
        }

        ResultVisible = true;
        ResultSeverity = outcome.Severity;
        ResultGlyph = GlyphFor(outcome.Severity, outcome.Display);
        ResultText = outcome.Text;
        HasExpandableOutput = outcome.Display == CommandResultDisplay.Summary;
        OutputText = outcome.FullOutput ?? string.Empty;
    }

    private static string GlyphFor(CommandResultSeverity severity, CommandResultDisplay display)
    {
        if (display == CommandResultDisplay.Notice)
        {
            return "›";
        }

        return severity switch
        {
            CommandResultSeverity.Success => "✓",
            CommandResultSeverity.Warning => "⚠",
            CommandResultSeverity.Error => "✕",
            _ => "›",
        };
    }

    public async ValueTask DisposeAsync()
    {
        CloseWhere();
        AgentProjectTabs.Clear();
        foreach (var terminal in TerminalTabs.ToArray())
        {
            terminal.RootShellExited -= OnTerminalRootShellExited;
            terminal.AgentProcessExited -= OnAgentProcessExited;
            await terminal.DisposeAsync().ConfigureAwait(false);
        }

        TerminalTabs.Clear();
        try
        {
            await _executor.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _journal.Dispose();
            await DisposeAgentsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads settings, applies the preferences that shape the whole shell — theme and the interactive
    /// registry — then opens the configured startup folder.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var notices = new List<string>();
        try
        {
            // Undo promises are process-scoped. Reconcile the durable journal before Filekin can
            // perform any new app-owned filesystem mutation in this process.
            await Task.Run(
                    () => _journal.ReconcileAfterRestartAsync(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(true);
        }
#pragma warning disable CA1031 // History failure must not prevent plain file-manager use.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkOperationJournalUnavailable();
            notices.Add(OperationJournalUnavailableMessage(ex));
        }
#pragma warning restore CA1031

        var settingsResult = await _locationCatalog.InitializeAsync(cancellationToken).ConfigureAwait(true);
        ApplyLocations(_locationCatalog.Locations);
        ApplyPreferences();

        notices.AddRange(settingsResult.Warnings);

        // Resolved after the catalog is published so an @Location startup target sees its real path.
        var startup = StartupLocationResolver.Resolve(_settings.Current.OpenFilesAtLaunch, _locationCatalog);
        if (startup.Notice is { } notice)
        {
            notices.Add(notice);
        }

        await NavigateToAsync(startup.Path, cancellationToken).ConfigureAwait(true);

        // Read-only, and it never creates the state database: somebody who has never used an agent
        // must not gain one by starting Filekin.
        await ReadAgentProjectCountAsync(cancellationToken).ConfigureAwait(true);

        if (notices.Count > 0)
        {
            ShowNotice(string.Join(Environment.NewLine, notices));
        }
    }

    /// <summary>Navigates to a saved sidebar Location.</summary>
    public async Task NavigateToLocationAsync(NavItem location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.TargetPath is not { } path)
        {
            return;
        }

        await NavigateToAsync(path, cancellationToken).ConfigureAwait(true);
        if (string.Equals(_currentPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            IsRecycleBinOpen = false;
            IsPlacesOpen = false;
            IsDrivesOpen = false;
            IsSettingsOpen = false;
            CloseInfo();
            CloseWhere();
            CloseArchive();
            CloseTidy();
        }
    }

    /// <summary>Navigates the Files hierarchy to <paramref name="path"/> and lists it.</summary>
    public async Task NavigateToAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);

        IReadOnlyList<DirectoryEntry> entries;
        try
        {
            entries = await Task.Run(() => _lister.List(fullPath), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Stay at the current location and report rather than throwing into the UI.
            ItemCount = ex is UnauthorizedAccessException ? "Access denied" : "Location unavailable";
            return;
        }

        _currentPath = fullPath;
        _entries = entries;
        ClearSelection();
        RebuildFiles();
        RebuildPathSegments(fullPath);
        UpdateFreeSpace(fullPath);
        UpdateActiveLocation(fullPath);
        OnPropertyChanged(nameof(PromptPath));
    }

    private void ApplyLocations(IReadOnlyList<NamedLocation> savedLocations)
    {
        Locations.Clear();
        foreach (var location in savedLocations)
        {
            Locations.Add(new NavItem(
                "@",
                location.Name,
                IsActive: false,
                SymbolAccent: false,
                TargetPath: location.Path));
        }

        if (_currentPath is { } currentPath)
        {
            UpdateActiveLocation(currentPath);
        }
    }

    private string ResolveLocationEditorPath(string path)
    {
        try
        {
            return _currentPath is { } currentPath
                ? Path.GetFullPath(path, currentPath)
                : path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private void ApplyLocationEditResult(UserLocationEditResult result)
    {
        if (!result.Succeeded)
        {
            LocationEditorError = result.Message;
            return;
        }

        ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Success, result.Message));
        ApplyLocations(_locationCatalog.Locations);
        CancelLocationEditor();
    }

    private void UpdateActiveLocation(string currentPath)
    {
        for (var index = 0; index < Locations.Count; index++)
        {
            var location = Locations[index];
            var isActive = string.Equals(location.TargetPath, currentPath, StringComparison.OrdinalIgnoreCase);
            if (location.IsActive != isActive)
            {
                Locations[index] = location with { IsActive = isActive };
            }
        }
    }

    private async Task<bool> RefreshFilesAsync(CancellationToken cancellationToken)
    {
        if (_currentPath is not { } path)
        {
            return false;
        }

        IReadOnlyList<DirectoryEntry> entries;
        try
        {
            entries = await Task.Run(() => _lister.List(path), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ItemCount = ex is UnauthorizedAccessException ? "Access denied" : "Location unavailable";
            return false;
        }

        // Navigation or a command may have changed the location while enumeration was in flight.
        if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        UpdateFreeSpace(path);
        if (ListingsMatch(_entries, entries))
        {
            return false;
        }

        _entries = entries;
        RebuildFiles();
        return true;
    }

    /// <summary>Opens a directory row, or launches a file through its Windows association.</summary>
    public async Task ActivateAsync(FileRowViewModel row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsDirectory)
        {
            await NavigateToAsync(row.FullPath, cancellationToken).ConfigureAwait(true);
            return;
        }

        var result = await Task.Run(() => FileLauncher.TryOpen(row.FullPath), cancellationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error,
                $"Could not open {row.Name}: {result.Message}"));
        }
    }

    /// <summary>Navigates to the parent directory, if there is one.</summary>
    public async Task NavigateUpAsync(CancellationToken cancellationToken = default)
    {
        if (_currentPath is null)
        {
            return;
        }

        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            await NavigateToAsync(parent.FullName, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Sorts by <paramref name="column"/>. Clicking the already-active column reverses direction;
    /// switching columns starts ascending. Selection is not preserved across a re-sort.
    /// </summary>
    public void SortBy(FileSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        RebuildFiles();
        OnPropertyChanged(nameof(TypeCaret));
        OnPropertyChanged(nameof(NameCaret));
        OnPropertyChanged(nameof(ModifiedCaret));
        OnPropertyChanged(nameof(SizeCaret));
    }

    /// <summary>Updates the current selection from the Files list and refreshes the status count.</summary>
    public void SetSelection(IReadOnlyList<FileRowViewModel> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        _selectionPaths = selected.Select(static r => r.FullPath).ToList();
        StatusSelection = _selectionPaths.Count switch
        {
            0 => string.Empty,
            1 => "1 selected",
            var n => $"{n} selected",
        };
    }

    /// <summary>
    /// Tracks action selection inside the Recycle Bin rich view. This controls only its Restore/Delete
    /// action bar and deliberately does not change the filesystem paths behind <c>@selection</c>.
    /// </summary>
    public void SetRecycleBinSelection(IReadOnlyList<RecycledItemViewModel> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        _selectedRecycledItems = selected.ToList();
        OnPropertyChanged(nameof(HasSelectedRecycledItems));
        OnPropertyChanged(nameof(WorkspaceSelectionStatus));
    }

    private string RecycleBinSelectionStatus() => _selectedRecycledItems.Count switch
    {
        0 => string.Empty,
        1 => "1 selected · Recycle Bin",
        var n => $"{n} selected · Recycle Bin",
    };

    private void ClearSelection()
    {
        _selectionPaths = [];
        StatusSelection = string.Empty;
    }

    /// <summary>
    /// The closest existing directory at or above <paramref name="path"/>'s parent, falling back to the
    /// drive root. Used to keep Files somewhere real after the current folder is moved or deleted.
    /// </summary>
    private static string? NearestExistingAncestor(string path)
    {
        for (var dir = Directory.GetParent(path); dir is not null; dir = dir.Parent)
        {
            if (dir.Exists)
            {
                return dir.FullName;
            }
        }

        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? root : null;
    }

    private static bool ListingsMatch(
        IReadOnlyList<DirectoryEntry> existing,
        IReadOnlyList<DirectoryEntry> refreshed)
    {
        if (existing.Count != refreshed.Count)
        {
            return false;
        }

        var byPath = existing.ToDictionary(
            static entry => entry.FullPath,
            StringComparer.OrdinalIgnoreCase);
        return refreshed.All(entry =>
            byPath.TryGetValue(entry.FullPath, out var prior) && prior == entry);
    }

    private void RebuildFiles()
    {
        var sorted = FileListingSort.Sort(_entries, _sortColumn, _sortDescending);
        Files = sorted.Select(static e => new FileRowViewModel(e)).ToList();
        ItemCount = _entries.Count == 1 ? "1 item" : $"{_entries.Count} items";
    }

    private void RebuildPathSegments(string fullPath)
    {
        PathSegments.Clear();

        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        var rootLabel = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isRootTheWholePath = string.Equals(
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        PathSegments.Add(new PathSegmentViewModel(rootLabel, root, isRoot: true, isLast: isRootTheWholePath));

        var remainder = fullPath[root.Length..];
        var parts = remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var accumulated = root;
        for (var i = 0; i < parts.Length; i++)
        {
            accumulated = Path.Combine(accumulated, parts[i]);
            PathSegments.Add(new PathSegmentViewModel(parts[i], accumulated, isRoot: false, isLast: i == parts.Length - 1));
        }
    }

    private void UpdateFreeSpace(string fullPath)
    {
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                StatusFree = string.Empty;
                return;
            }

            var drive = new DriveInfo(root);
            var label = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StatusFree = $"{FormatGigabytes(drive.AvailableFreeSpace)} free ({label})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusFree = string.Empty;
        }
    }

    private string CaretFor(FileSortColumn column)
    {
        if (_sortColumn != column)
        {
            return string.Empty;
        }

        return _sortDescending ? "▾" : "▴"; // ▾ / ▴
    }

    private static string FormatGigabytes(long bytes)
    {
        var gib = bytes / (1024d * 1024d * 1024d);
        return gib >= 100
            ? $"{gib.ToString("0", CultureInfo.CurrentCulture)} GB"
            : $"{gib.ToString("0.0", CultureInfo.CurrentCulture)} GB";
    }
}

public sealed record NavItem(
    string Symbol,
    string Name,
    bool IsActive,
    bool SymbolAccent,
    string? TargetPath = null)
{
    public override string ToString() => $"{Symbol}{Name}";
}

public readonly record struct WorkspaceRefreshResult(bool FilesChanged, bool VisibleRichViewChanged);
