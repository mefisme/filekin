using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Filekin.Core.FileSystem;
using Filekin.Core.Inspection;
using Filekin.Infrastructure.Windows.Inspection;

namespace Filekin.App.ViewModels;

/// <summary>
/// The Info surface (<c>/info</c>). A rich view over the preserved Files workspace like
/// <c>/places</c>, <c>/drives</c>, and Settings — but a field sheet rather than a list, because it
/// describes one thing instead of offering many (ENGINEERING-GUARDRAILS.md — keep rich views
/// semantically distinct).
///
/// The sheet opens immediately with everything metadata can answer. Recursive size, checksums, and
/// line counts are the expensive answers, and each arrives on its own terms: the scan streams into
/// its rows while it runs, and the other two wait to be asked.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly WindowsFileInspector _inspector = new();
    private readonly DirectoryAggregateScanner _scanner = new();

    private bool _isInfoOpen;
    private string _infoHeading = string.Empty;
    private string _infoStatus = string.Empty;
    private string? _infoSinglePath;
    private CancellationTokenSource? _infoScan;
    private InfoRowViewModel? _sizeRow;
    private InfoRowViewModel? _filesRow;
    private InfoRowViewModel? _foldersRow;

    /// <summary>Whether the Info sheet (<c>/info</c>) is showing over the Files hierarchy.</summary>
    public bool IsInfoOpen
    {
        get => _isInfoOpen;
        private set
        {
            if (SetProperty(ref _isInfoOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>The item name, or a count such as <c>37 selected items</c>.</summary>
    public string InfoHeading
    {
        get => _infoHeading;
        private set => SetProperty(ref _infoHeading, value);
    }

    public string InfoStatus
    {
        get => _infoStatus;
        private set
        {
            if (SetProperty(ref _infoStatus, value))
            {
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
            }
        }
    }

    /// <summary>The rows of the sheet, in display order.</summary>
    public ObservableCollection<InfoRowViewModel> InfoRows { get; } = [];

    /// <summary>Whether the Windows Properties escape hatch applies — one target only.</summary>
    public bool CanOpenWindowsProperties => _infoSinglePath is not null;

    /// <summary>Opens the Info sheet for one or more targets and starts any scan they need.</summary>
    public async Task OpenInfoAsync(IReadOnlyList<string> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        CancelInfoScan();
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseArchive();

        // Reading metadata touches the filesystem and the shell, so it never runs on the UI thread.
        var snapshot = await Task.Run(
            () => targets.Count == 1 ? _inspector.Inspect(targets[0]) : _inspector.InspectSelection(targets),
            cancellationToken).ConfigureAwait(true);

        BuildInfoRows(snapshot);
        IsInfoOpen = true;

        if (snapshot.NeedsAggregate)
        {
            StartInfoScan(targets, countRootFoldersThemselves: snapshot.Kind == InspectionKind.Selection);
        }
    }

    /// <summary>Closes the Info sheet and stops any scan still running behind it.</summary>
    public void CloseInfo()
    {
        CancelInfoScan();
        IsInfoOpen = false;
    }

    /// <summary>Runs the action a row offers: copy, checksum, or line count.</summary>
    public async Task InvokeInfoRowActionAsync(InfoRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.IsBusy || !row.HasAction)
        {
            return;
        }

        switch (row.Action)
        {
            case InfoRowAction.CopyValue:
                CopyToClipboard(row.Value);
                break;
            case InfoRowAction.CalculateChecksum:
                await FillRowAsync(row, token => FileChecksum.ComputeSha256Async(_infoSinglePath!, token)).ConfigureAwait(true);
                break;
            case InfoRowAction.CountLines:
                await FillRowAsync(row, CountLinesAsync).ConfigureAwait(true);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Hands a single target to the native Windows Properties dialog. <paramref name="ownerWindow"/>
    /// is the shell window handle, so the dialog belongs to Filekin instead of floating free behind it.
    /// </summary>
    public void OpenWindowsProperties(IntPtr ownerWindow = default)
    {
        if (_infoSinglePath is not { } path)
        {
            return;
        }

        try
        {
            WindowsPropertiesDialog.Show(path, ownerWindow);
        }
        catch (InvalidOperationException ex)
        {
            InfoStatus = ex.Message;
        }
    }

    private async Task<string> CountLinesAsync(CancellationToken cancellationToken)
    {
        var path = _infoSinglePath!;
        var probe = await Task.Run(() => TextFileReader.Sniff(path), cancellationToken).ConfigureAwait(true);
        if (probe is null)
        {
            return "Not text";
        }

        var lines = await TextFileReader.CountLinesAsync(path, probe, cancellationToken).ConfigureAwait(true);
        return lines.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static async Task FillRowAsync(InfoRowViewModel row, Func<CancellationToken, Task<string>> work)
    {
        row.IsBusy = true;
        row.Value = "Working…";
        row.ActionLabel = null;

        try
        {
            var result = await work(CancellationToken.None).ConfigureAwait(true);
            row.Value = result;

            // A hash is worth copying; a line count is not.
            if (row.Action == InfoRowAction.CalculateChecksum)
            {
                row.ActionLabel = "Copy";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            row.Value = ex.Message;
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private void BuildInfoRows(InspectionResult snapshot)
    {
        InfoRows.Clear();
        _sizeRow = null;
        _filesRow = null;
        _foldersRow = null;
        _infoSinglePath = snapshot.SinglePath;
        InfoHeading = snapshot.Heading;
        InfoStatus = string.Empty;
        OnPropertyChanged(nameof(CanOpenWindowsProperties));

        if (snapshot.Error is { } error)
        {
            InfoStatus = error;
            return;
        }

        foreach (var detail in snapshot.Details)
        {
            InfoRows.Add(new InfoRowViewModel(
                detail.Label,
                detail.Value,
                detail.Label == "Path" ? InfoRowAction.CopyValue : InfoRowAction.None,
                detail.Label == "Path" ? "Copy" : null));

            // Recursive totals belong directly under Type, which is where the spec sheet puts them.
            if (snapshot.NeedsAggregate && detail.Label == "Type")
            {
                AddAggregateRows(snapshot.Kind);
            }
        }

        if (snapshot.NeedsAggregate && _sizeRow is null)
        {
            AddAggregateRows(snapshot.Kind, index: 0);
        }

        if (snapshot.SinglePath is not null && snapshot.Kind == InspectionKind.File)
        {
            InfoRows.Add(new InfoRowViewModel("SHA-256", "—", InfoRowAction.CalculateChecksum, "Calculate"));

            if (snapshot.CanCountLines)
            {
                InfoRows.Add(new InfoRowViewModel("Lines", "—", InfoRowAction.CountLines, "Count"));
            }
        }
    }

    private void AddAggregateRows(InspectionKind kind, int? index = null)
    {
        _sizeRow = new InfoRowViewModel(kind == InspectionKind.Selection ? "Total size" : "Size", "Calculating…");
        _filesRow = new InfoRowViewModel("Files", "…");
        _foldersRow = new InfoRowViewModel("Folders", "…");

        if (index is { } at)
        {
            InfoRows.Insert(at, _sizeRow);
            InfoRows.Insert(at + 1, _filesRow);
            InfoRows.Insert(at + 2, _foldersRow);
            return;
        }

        InfoRows.Add(_sizeRow);
        InfoRows.Add(_filesRow);
        InfoRows.Add(_foldersRow);
    }

    private void StartInfoScan(IReadOnlyList<string> targets, bool countRootFoldersThemselves)
    {
        var cancellation = new CancellationTokenSource();
        _infoScan = cancellation;
        var roots = targets.ToArray();
        InfoStatus = "Scanning…";

        _ = Task.Run(
            () =>
            {
                try
                {
                    var totals = _scanner.Scan(
                        roots,
                        countRootFoldersThemselves,
                        progress => Publish(progress, cancellation.Token),
                        cancellation.Token);
                    Publish(totals, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Leaving the view stops the walk; there is nothing to report.
                }
#pragma warning disable CA1031 // A scan failure must not crash the shell; the sheet says so instead.
                catch (Exception ex)
                {
                    _ = _dispatcher.InvokeAsync(() =>
                    {
                        if (!cancellation.IsCancellationRequested)
                        {
                            InfoStatus = $"Could not measure this: {ex.Message}";
                        }
                    });
                }
#pragma warning restore CA1031
            },
            cancellation.Token);
    }

    private void Publish(AggregateTotals totals, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(() =>
        {
            // A scan that was superseded or dismissed must not write into the new sheet's rows.
            if (cancellationToken.IsCancellationRequested || _sizeRow is null)
            {
                return;
            }

            // The trailing ellipsis is the honest signal that a number is still moving.
            var suffix = totals.IsComplete ? string.Empty : "…";
            _sizeRow.Value = ByteSize.Format(totals.Bytes) + suffix;
            _filesRow!.Value = totals.Files.ToString("N0", CultureInfo.CurrentCulture) + suffix;
            _foldersRow!.Value = totals.Folders.ToString("N0", CultureInfo.CurrentCulture) + suffix;

            InfoStatus = totals switch
            {
                { IsComplete: false } => "Scanning…",
                { HasUnreadableFolders: true } => "Some folders could not be read",
                _ => string.Empty,
            };
        });
    }

    private void CancelInfoScan()
    {
        var scan = _infoScan;
        _infoScan = null;
        if (scan is null)
        {
            return;
        }

        scan.Cancel();
        scan.Dispose();
    }

    private void CopyToClipboard(string value)
    {
        try
        {
            Clipboard.SetText(value);
            InfoStatus = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another process can hold the clipboard open; that is not a Filekin failure.
            InfoStatus = "The clipboard is busy";
        }
    }
}
