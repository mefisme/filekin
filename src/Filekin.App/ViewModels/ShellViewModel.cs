using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Commands.References;
using Filekin.Core.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Files shell view model. It owns the current filesystem location, the listing shown in the Files
/// hierarchy, the active sort, and the current selection, and exposes that selection as a
/// <see cref="ReferenceContext"/> for the command bar to consume once it is wired (HANDOFF.md step 2).
/// Enumeration runs off the UI thread (DECISIONS.md, 2026-08-24 — "UI Thread Must Remain Responsive");
/// the listing is rebuilt on navigation and re-sort.
///
/// The sidebar <see cref="Locations"/> and <see cref="Surfaces"/> remain static design samples: their
/// navigation is a separate wiring task and is not represented as finished behavior.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly IDirectoryLister _lister;

    private IReadOnlyList<DirectoryEntry> _entries = [];
    private List<string> _selectionPaths = [];
    private string? _currentPath;

    private IReadOnlyList<FileRowViewModel> _files = [];
    private string _itemCount = string.Empty;
    private string _statusSelection = string.Empty;
    private string _statusFree = string.Empty;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private bool _sortDescending;

    public ShellViewModel()
        : this(new FileSystemDirectoryLister())
    {
    }

    public ShellViewModel(IDirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(lister);
        _lister = lister;
    }

    public IReadOnlyList<FileRowViewModel> Files
    {
        get => _files;
        private set => SetProperty(ref _files, value);
    }

    public ObservableCollection<PathSegmentViewModel> PathSegments { get; } = [];

    public string ItemCount
    {
        get => _itemCount;
        private set => SetProperty(ref _itemCount, value);
    }

    public string StatusSelection
    {
        get => _statusSelection;
        private set => SetProperty(ref _statusSelection, value);
    }

    public string StatusFree
    {
        get => _statusFree;
        private set => SetProperty(ref _statusFree, value);
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

    /// <summary>The built-in <c>/places</c> and <c>/drives</c> Filekin surfaces (static design sample for now).</summary>
    public IReadOnlyList<NavItem> Surfaces { get; } =
    [
        new("/", "places", IsActive: false, SymbolAccent: true),
        new("/", "drives", IsActive: false, SymbolAccent: true),
    ];

    /// <summary>The workspace state intrinsic <c>@</c> references resolve against: current folder and selection.</summary>
    public ReferenceContext BuildReferenceContext() => new(_currentPath, _selectionPaths);

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

    private void ClearSelection()
    {
        _selectionPaths = [];
        StatusSelection = string.Empty;
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
