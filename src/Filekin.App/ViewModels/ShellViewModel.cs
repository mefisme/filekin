using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Filekin.Core.Commands.References;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Files shell view model. It owns the current filesystem location, the listing shown in the Files
/// hierarchy, the active sort, the current selection, and the command bar. Filesystem enumeration and
/// command execution run off the UI thread (DECISIONS.md, 2026-08-24 — "UI Thread Must Remain
/// Responsive"); the listing is rebuilt on navigation, re-sort, and after a command that changed it.
///
/// The sidebar <see cref="Locations"/> and <see cref="Surfaces"/> remain static design samples: their
/// navigation is a separate wiring task and is not represented as finished behavior.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IDirectoryLister _lister;
    private readonly CommandExecutor _executor = new();
    private readonly WindowsRecycleBin _recycleBin = new();
    private readonly List<string> _history = [];
    private readonly Dispatcher _dispatcher;
    private int _historyIndex;
    private string _historyDraft = string.Empty;

    private bool _isRecycleBinOpen;
    private IReadOnlyList<RecycledItemViewModel> _recycledItems = [];
    private List<RecycledItemViewModel> _selectedRecycledItems = [];
    private string _recycleBinStatus = string.Empty;

    private bool _isConfirming;
    private string _confirmPrompt = string.Empty;
    private Func<Task>? _pendingConfirmAction;

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
    private bool _isFilesWorkspaceSelected = true;
    private TerminalTabViewModel? _selectedTerminal;

    public ShellViewModel()
        : this(new FileSystemDirectoryLister())
    {
    }

    public ShellViewModel(IDirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(lister);
        _lister = lister;

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
    public ObservableCollection<TerminalTabViewModel> TerminalTabs { get; } = [];

    public bool IsFilesWorkspaceSelected
    {
        get => _isFilesWorkspaceSelected;
        private set
        {
            if (SetProperty(ref _isFilesWorkspaceSelected, value))
            {
                OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
            }
        }
    }

    public bool IsTerminalWorkspaceSelected => !IsFilesWorkspaceSelected;

    public TerminalTabViewModel? SelectedTerminal
    {
        get => _selectedTerminal;
        private set => SetProperty(ref _selectedTerminal, value);
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
    public string WorkspaceSelectionStatus => _isRecycleBinOpen
        ? RecycleBinSelectionStatus()
        : _statusSelection;

    public string StatusFree
    {
        get => _statusFree;
        private set => SetProperty(ref _statusFree, value);
    }

    /// <summary>The current folder, shown quietly as the command-bar prompt (UX-DESIGN.md).</summary>
    public string PromptPath => _currentPath ?? string.Empty;

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

    /// <summary>Whether the Files hierarchy (headers + list) is shown; hidden while a rich view is open.</summary>
    public bool IsFilesContentVisible => !_isRecycleBinOpen;

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
    public void RequestConfirmation(string prompt, Func<Task> onYes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(onYes);

        _pendingConfirmAction = onYes;
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
        CancelConfirmation();
        if (action is not null)
        {
            await action().ConfigureAwait(true);
        }
    }

    /// <summary>Dismisses the pending confirm without doing anything (N or Esc).</summary>
    public void CancelConfirmation()
    {
        _pendingConfirmAction = null;
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

    /// <summary>The sidebar's user-defined <c>@</c> Locations (static design sample for now).</summary>
    public IReadOnlyList<NavItem> Locations { get; } =
    [
        new("@", "Projects", IsActive: false, SymbolAccent: false),
        new("@", "Downloads", IsActive: false, SymbolAccent: false),
        new("@", "Music", IsActive: false, SymbolAccent: false),
        new("@", "GitHub", IsActive: true, SymbolAccent: false),
        new("@", "SnapMap", IsActive: false, SymbolAccent: false),
    ];

    /// <summary>
    /// The built-in <c>/places</c>, <c>/drives</c>, and <c>/recycle</c> Filekin surfaces. <c>/places</c> and
    /// <c>/drives</c> remain static design samples; <c>/recycle</c> opens the Recycle Bin view.
    /// </summary>
    public IReadOnlyList<NavItem> Surfaces { get; } =
    [
        new("/", "places", IsActive: false, SymbolAccent: true),
        new("/", "drives", IsActive: false, SymbolAccent: true),
        new("/", "recycle", IsActive: false, SymbolAccent: true),
    ];

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

        if (_currentPath is null)
        {
            ShowNotice("The command bar needs a filesystem folder.");
            return;
        }

        AddToHistory(input);
        CommandInput = string.Empty;
        IsBusy = true;
        ShowRunning();

        try
        {
            var outcome = await _executor
                .ExecuteAsync(input, BuildReferenceContext(), _currentPath)
                .ConfigureAwait(true);
            ApplyResult(outcome);

            if (outcome.TerminalSession is { } terminalSession && outcome.TerminalTitle is { } terminalTitle)
            {
                AddTerminal(terminalTitle, terminalSession);
                return;
            }

            if (outcome.OpensRecycleBin)
            {
                await OpenRecycleBinAsync().ConfigureAwait(true);
                return;
            }

            // Decide where Files should sit after the command: a cd moves us; otherwise re-list the
            // current folder if it may have changed.
            string? destination = null;
            if (outcome.NewFolderPath is { } newFolder &&
                !string.Equals(newFolder, _currentPath, StringComparison.OrdinalIgnoreCase))
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
#pragma warning disable CA1031 // The command bar must never crash the shell on an unexpected failure.
        catch (Exception ex)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, $"Command failed: {ex.Message}"));
        }
#pragma warning restore CA1031
        finally
        {
            IsBusy = false;
        }
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
            AddTerminal(outcome.TerminalTitle!, outcome.TerminalSession!);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, ex.Message));
            SelectFilesWorkspace();
        }
    }

    public void SelectFilesWorkspace()
    {
        IsFilesWorkspaceSelected = true;
        SelectedTerminal = null;
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

        IsFilesWorkspaceSelected = false;
        SelectedTerminal = terminal;
        foreach (var candidate in TerminalTabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, terminal);
        }
    }

    /// <summary>
    /// Moves one workspace forward or back for Ctrl+Tab. The order matches the tab strip: the
    /// permanent Files workspace first, then the live terminals, cycling at both ends.
    /// </summary>
    public void SelectAdjacentWorkspace(bool forward)
    {
        if (TerminalTabs.Count == 0)
        {
            return;
        }

        var count = TerminalTabs.Count + 1;
        var current = IsFilesWorkspaceSelected || SelectedTerminal is null
            ? 0
            : TerminalTabs.IndexOf(SelectedTerminal) + 1;
        var next = (((current + (forward ? 1 : -1)) % count) + count) % count;
        if (next == 0)
        {
            SelectFilesWorkspace();
        }
        else
        {
            SelectTerminal(TerminalTabs[next - 1]);
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
        TerminalTabs.RemoveAt(index);
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

        await terminal.DisposeAsync().ConfigureAwait(true);
    }

    private void AddTerminal(string title, Filekin.Core.Terminal.ITerminalSession session)
    {
        var uniqueTitle = DisambiguateTerminalTitle(title);
        var terminal = new TerminalTabViewModel(uniqueTitle, session, _dispatcher);
        terminal.RootShellExited += OnTerminalRootShellExited;
        TerminalTabs.Add(terminal);
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

    /// <summary>Opens the Recycle Bin view and loads its contents.</summary>
    public async Task OpenRecycleBinAsync()
    {
        IsRecycleBinOpen = true;
        await RefreshRecycleBinAsync().ConfigureAwait(true);
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
        var richViewChanged = _isRecycleBinOpen &&
            await RefreshRecycleBinAsync().ConfigureAwait(true);

        return new WorkspaceRefreshResult(filesChanged, richViewChanged);
    }

    /// <summary>Closes the Recycle Bin view and returns to the Files hierarchy.</summary>
    public void CloseRecycleBin() => IsRecycleBinOpen = false;

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
        ResultVisible = true;
        ResultSeverity = CommandResultSeverity.Info;
        ResultGlyph = "…";
        ResultText = "Running…";
        HasExpandableOutput = false;
        OutputText = string.Empty;
    }

    private void ShowNotice(string text) =>
        ApplyResult(CommandExecutionOutcome.Notice(text));

    private void ApplyResult(CommandExecutionOutcome outcome)
    {
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
            CommandResultSeverity.Error => "✕",
            _ => "›",
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var terminal in TerminalTabs.ToArray())
        {
            terminal.RootShellExited -= OnTerminalRootShellExited;
            await terminal.DisposeAsync().ConfigureAwait(false);
        }

        TerminalTabs.Clear();
        await _executor.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Loads the initial location (the user's home folder) when the window opens.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);

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
        OnPropertyChanged(nameof(PromptPath));
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

        FileLauncher.Open(row.FullPath);
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

public sealed record NavItem(string Symbol, string Name, bool IsActive, bool SymbolAccent);

public readonly record struct WorkspaceRefreshResult(bool FilesChanged, bool VisibleRichViewChanged);
