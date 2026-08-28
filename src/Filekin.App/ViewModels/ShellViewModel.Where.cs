using System.Collections.ObjectModel;
using System.IO;
using Filekin.Core.Commands.App.Where;
using Filekin.Core.Discovery;
using Filekin.Infrastructure.Windows.Commands;
using Filekin.Infrastructure.Windows.Discovery;
using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>
/// The progressive <c>/where</c> discovery surface. Its scan belongs to the view, not the command
/// bar: the bar is usable again as soon as the rich view opens, while Stop, Back, or Esc cancel the
/// bounded background work and retain any results already published.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly WindowsWhereDiscovery _whereDiscovery = new();
    private readonly WindowsUserPathEditor _userPathEditor = new();

    private bool _isWhereOpen;
    private bool _isWhereScanning;
    private string _whereTitle = "Where";
    private string _whereStatus = string.Empty;
    private string _whereNotice = string.Empty;
    private IReadOnlyList<WhereLocation> _whereLocations = [];
    private CancellationTokenSource? _whereScan;
    private WindowsUserPathChange? _lastUserPathChange;

    public bool IsWhereOpen
    {
        get => _isWhereOpen;
        private set
        {
            if (SetProperty(ref _isWhereOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    public bool IsWhereScanning
    {
        get => _isWhereScanning;
        private set => SetProperty(ref _isWhereScanning, value);
    }

    public string WhereTitle
    {
        get => _whereTitle;
        private set => SetProperty(ref _whereTitle, value);
    }

    public string WhereStatus
    {
        get => _whereStatus;
        private set
        {
            if (SetProperty(ref _whereStatus, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    public string WhereNotice
    {
        get => _whereNotice;
        private set
        {
            if (SetProperty(ref _whereNotice, value))
            {
                OnPropertyChanged(nameof(HasWhereNotice));
            }
        }
    }

    public bool HasWhereNotice => _whereNotice.Length > 0;

    public bool CanUndoUserPathChange => _lastUserPathChange is not null;

    public ObservableCollection<WhereItemViewModel> WhereItems { get; } = [];

    /// <summary>Opens immediately, then lets the scan publish progressive snapshots behind it.</summary>
    public Task OpenWhereAsync(WhereInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CancelWhereScan();
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseArchive();
        CloseTidy();

        WhereTitle = $"Where — {request.Query}";
        WhereStatus = "Starting…";
        WhereNotice = string.Empty;
        _whereLocations = [];
        WhereItems.Clear();
        IsWhereOpen = true;
        IsWhereScanning = true;

        var cancellation = new CancellationTokenSource();
        _whereScan = cancellation;
        _ = RunWhereScanAsync(request.Query, cancellation);
        return Task.CompletedTask;
    }

    public void StopWhereScan() => _whereScan?.Cancel();

    public void CloseWhere()
    {
        CancelWhereScan();
        IsWhereOpen = false;
    }

    public void OpenWhereItem(WhereItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanOpen)
        {
            return;
        }

        try
        {
            FileLauncher.Open(item.Path);
            WhereNotice = $"Opened {Path.GetFileName(item.Path)}.";
        }
        catch (InvalidOperationException ex)
        {
            WhereNotice = ex.Message;
        }
    }

    /// <summary>Navigates to the containing folder; the view selects the target after this returns.</summary>
    public async Task<bool> GoToWhereItemAsync(
        WhereItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(item.Path));
        var destination = parent ?? (Directory.Exists(item.Path) ? item.Path : null);
        if (destination is null)
        {
            WhereNotice = "That location is no longer available.";
            return false;
        }

        await NavigateToAsync(destination, cancellationToken).ConfigureAwait(true);
        if (!string.Equals(_currentPath, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            WhereNotice = "That location could not be opened in Files.";
            return false;
        }

        CloseWhere();
        return true;
    }

    public void RequestAddWhereItemToUserPath(WhereItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanAddToUserPath || Path.GetDirectoryName(item.Path) is not { } folder)
        {
            return;
        }

        RequestConfirmation(
            $"Add \"{folder}\" to your Windows user PATH? New terminals and apps can find commands in this folder.",
            () => AddDirectoryToUserPathAsync(folder));
    }

    public async Task UndoUserPathChangeAsync()
    {
        if (_lastUserPathChange is not { } change)
        {
            return;
        }

        ReportPendingUserPathEdit();
        var result = await Task.Run(() => _userPathEditor.Undo(change)).ConfigureAwait(true);
        if (result.Succeeded)
        {
            _lastUserPathChange = null;
        }

        ApplyUserPathEdit(result, rememberChange: false);
    }

    private async Task RunWhereScanAsync(string query, CancellationTokenSource cancellation)
    {
        var progress = new Progress<WhereDiscoveryProgress>(snapshot =>
        {
            if (!ReferenceEquals(_whereScan, cancellation) || cancellation.IsCancellationRequested)
            {
                return;
            }

            _whereLocations = snapshot.Locations;
            RebuildWhereItems();
            WhereStatus = Status(snapshot.Stage, snapshot.Locations.Count, snapshot.UnreadableLocations);
        });

        try
        {
            var outcome = await _whereDiscovery
                .DiscoverAsync(query, progress, cancellation.Token)
                .ConfigureAwait(true);
            if (!ReferenceEquals(_whereScan, cancellation))
            {
                return;
            }

            _whereLocations = outcome.Locations;
            RebuildWhereItems();
            WhereStatus = outcome.Locations.Count switch
            {
                0 when outcome.UnreadableLocations > 0 => "No matches · some locations could not be read",
                0 => "No matches",
                1 when outcome.UnreadableLocations > 0 => "1 location · some locations could not be read",
                1 => "1 location",
                var count when outcome.UnreadableLocations > 0 => $"{count} locations · some locations could not be read",
                var count => $"{count} locations",
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_whereScan, cancellation) && IsWhereOpen)
            {
                WhereStatus = WhereItems.Count == 0 ? "Stopped" : $"Stopped · {WhereItems.Count} found";
            }
        }
#pragma warning disable CA1031 // A discovery failure must remain a result in the rich view, never crash Filekin.
        catch (Exception ex)
        {
            if (ReferenceEquals(_whereScan, cancellation) && IsWhereOpen)
            {
                WhereStatus = $"Could not finish: {ex.Message}";
            }
        }
#pragma warning restore CA1031
        finally
        {
            if (ReferenceEquals(_whereScan, cancellation))
            {
                _whereScan = null;
                IsWhereScanning = false;
            }

            cancellation.Dispose();
        }
    }

    private Task AddDirectoryToUserPathAsync(string directory) =>
        ApplyUserPathEditAsync(() => _userPathEditor.AddDirectory(directory));

    private async Task ApplyUserPathEditAsync(Func<WindowsUserPathEditResult> edit)
    {
        ReportPendingUserPathEdit();
        var result = await Task.Run(edit).ConfigureAwait(true);
        ApplyUserPathEdit(result, rememberChange: true);
    }

    /// <summary>
    /// Windows announces an environment change to every open window before the write returns, which
    /// takes seconds on a busy desktop. Say so immediately rather than letting the page look dead.
    /// </summary>
    private void ReportPendingUserPathEdit()
    {
        const string Pending = "Telling Windows about the change…";
        WhereNotice = Pending;
        if (IsSettingsOpen && IsAdvancedCategory)
        {
            ReportSettings(Pending, isError: false);
        }
    }

    private void ApplyUserPathEdit(WindowsUserPathEditResult result, bool rememberChange)
    {
        if (result.Succeeded && rememberChange)
        {
            _lastUserPathChange = result.Change;
        }

        if (result.Succeeded)
        {
            RefreshWherePathScopes();
            RebuildWindowsPathSettings();
        }

        OnPropertyChanged(nameof(CanUndoUserPathChange));
        WhereNotice = result.Message;
        if (IsSettingsOpen && IsAdvancedCategory)
        {
            ReportSettings(result.Message, isError: !result.Succeeded);
        }
    }

    private void RefreshWherePathScopes()
    {
        _whereLocations =
        [
            .. _whereLocations.Select(location => location.Kind == WhereLocationKind.Executable
                ? location with
                {
                    PathScope = WindowsEnvironmentPath.ScopeOf(Path.GetDirectoryName(location.Path) ?? location.Path),
                }
                : location),
        ];
        RebuildWhereItems();
    }

    private void RebuildWhereItems()
    {
        WhereItems.Clear();
        WhereLocationKind? previous = null;
        foreach (var location in _whereLocations)
        {
            WhereItems.Add(new WhereItemViewModel(location, startsSection: location.Kind != previous));
            previous = location.Kind;
        }
    }

    private void CancelWhereScan()
    {
        var cancellation = _whereScan;
        _whereScan = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        IsWhereScanning = false;
    }

    private static string Status(string stage, int count, int unreadable) =>
        unreadable > 0 ? $"{stage} · {count} found · some skipped" : $"{stage} · {count} found";
}
