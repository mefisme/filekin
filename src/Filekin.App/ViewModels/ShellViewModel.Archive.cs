using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Archives;
using Filekin.Core.Commands.App.Unzip;
using Filekin.Core.Commands.App.Zip;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;
using Filekin.Infrastructure.Windows.Archives;
using Filekin.Infrastructure.Windows.FileSystem;
using Filekin.Infrastructure.Windows.Operations;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.App.ViewModels;

/// <summary>What the archive surface is currently doing.</summary>
public enum ArchiveMode
{
    None,
    Unzip,
    Zip,
}

/// <summary>
/// The archive surface, shared by <c>/unzip</c> and <c>/zip</c>.
///
/// One surface for both because they ask the same question — "here is what I am about to write, is
/// that right?" — and answering it twice in two shapes would teach the user two things where one
/// will do (ENGINEERING-GUARDRAILS.md — keep rich views semantically distinct, not merely separate).
///
/// The preview is the default rather than a switch the careful user has to find, because
/// <c>/unzip</c> writes many files at once and, until the durable history exists, the in-session
/// <c>[Undo]</c> is the only way back. <c>-y</c> and the Settings toggle turn it off for anyone who
/// would rather go straight through.
///
/// Every control on the sheet re-plans from the archive's own table of contents rather than editing
/// the plan in place, so what is listed is always what will actually be written.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Rows shown before the list is capped. Long enough to judge, short enough to stay fast.</summary>
    private const int MaxArchiveRows = 400;

    private readonly ZipArchiveReader _archiveReader = new();
    private readonly SqliteOperationJournal _journal = new();

    private ZipExtractor? _extractor;
    private ZipCompressor? _compressor;
    private ZipExtractionUndo? _extractionUndo;
    private ZipCompressionUndo? _compressionUndo;

    private ArchiveMode _archiveMode;
    private UnzipInvocation? _unzipRequest;
    private ZipInvocation? _zipRequest;
    private readonly Dictionary<string, IReadOnlyList<ArchiveEntry>> _archiveContents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArchivePlan> _unzipPlans = [];
    private ZipPlan? _zipPlan;
    private CancellationTokenSource? _archiveRun;
    private bool _archiveRunWroteAnything;
    private bool _archiveRunUndoRecorded;
    private bool _operationJournalAvailable = true;
    private string? _archiveRunHistoryWarning;
    private Guid? _archiveUndoEntryId;

    private bool _isArchiveOpen;
    private string _archiveTitle = string.Empty;
    private string _archiveSummary = string.Empty;
    private string _archiveDestination = string.Empty;
    private string _archiveWarning = string.Empty;
    private string _archiveFolderName = string.Empty;
    private bool _archiveIntoFolder = true;
    private bool _archiveOverwrite;
    private bool _archiveHidePreviewNextTime;
    private bool _isArchiveBusy;
    private string _archiveProgressText = string.Empty;
    private double _archiveProgressFraction;
    private bool _canUndoArchive;
    private bool _archiveUndoMatchesResult;
    private string _archiveUndoLabel = string.Empty;

    private ZipExtractor Extractor => _extractor ??= new ZipExtractor(new WindowsFileSystemOperations());

    private ZipCompressor Compressor => _compressor ??= new ZipCompressor(new WindowsFileSystemOperations());

    private ZipExtractionUndo ExtractionUndo => _extractionUndo ??= new ZipExtractionUndo(_recycleBin);

    private ZipCompressionUndo CompressionUndo => _compressionUndo ??= new ZipCompressionUndo(_recycleBin);

    /// <summary>Whether the archive preview is showing over the Files hierarchy.</summary>
    public bool IsArchiveOpen
    {
        get => _isArchiveOpen;
        private set
        {
            if (SetProperty(ref _isArchiveOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
                OnPropertyChanged(nameof(CanViewArchiveProgress));
            }
        }
    }

    /// <summary>For example <c>Extract photos.zip</c> or <c>Create photos.zip</c>.</summary>
    public string ArchiveTitle
    {
        get => _archiveTitle;
        private set => SetProperty(ref _archiveTitle, value);
    }

    /// <summary>For example <c>34 files · 12.4 MB</c>.</summary>
    public string ArchiveSummary
    {
        get => _archiveSummary;
        private set => SetProperty(ref _archiveSummary, value);
    }

    /// <summary>The folder the content will land in, written out in full.</summary>
    public string ArchiveDestination
    {
        get => _archiveDestination;
        private set => SetProperty(ref _archiveDestination, value);
    }

    /// <summary>Collisions and refused entries, summarized. Empty when there is nothing to say.</summary>
    public string ArchiveWarning
    {
        get => _archiveWarning;
        private set
        {
            if (SetProperty(ref _archiveWarning, value))
            {
                OnPropertyChanged(nameof(HasArchiveWarning));
            }
        }
    }

    public bool HasArchiveWarning => _archiveWarning.Length > 0;

    /// <summary>The name of the single folder extraction creates. Editable on the sheet.</summary>
    public string ArchiveFolderName
    {
        get => _archiveFolderName;
        set
        {
            if (SetProperty(ref _archiveFolderName, value))
            {
                Replan();
            }
        }
    }

    /// <summary>
    /// Whether the content lands in one folder of its own. Off is <c>-noroot</c>: for <c>/unzip</c>
    /// the archive's wrapper is stripped, for <c>/zip</c> the folder's own name is not stored.
    /// </summary>
    public bool ArchiveIntoFolder
    {
        get => _archiveIntoFolder;
        set
        {
            if (SetProperty(ref _archiveIntoFolder, value))
            {
                OnPropertyChanged(nameof(CanEditArchiveFolderName));
                Replan();
            }
        }
    }

    /// <summary>Renaming the folder only means anything when there is a folder.</summary>
    public bool CanEditArchiveFolderName => _archiveMode == ArchiveMode.Unzip && _archiveIntoFolder;

    /// <summary>Whether existing files are replaced. Originals go to the Recycle Bin either way.</summary>
    public bool ArchiveOverwrite
    {
        get => _archiveOverwrite;
        set
        {
            if (SetProperty(ref _archiveOverwrite, value))
            {
                Replan();
            }
        }
    }

    /// <summary>Whether the operation is running, which disables the controls and the action.</summary>
    public bool IsArchiveBusy
    {
        get => _isArchiveBusy;
        private set
        {
            if (SetProperty(ref _isArchiveBusy, value))
            {
                OnPropertyChanged(nameof(CanRunArchive));
                OnPropertyChanged(nameof(CanViewArchiveProgress));
                OnPropertyChanged(nameof(ArchiveSecondaryActionLabel));
            }
        }
    }

    public string ArchiveProgressText
    {
        get => _archiveProgressText;
        private set => SetProperty(ref _archiveProgressText, value);
    }

    /// <summary>Zero to one, for the progress bar.</summary>
    public double ArchiveProgressFraction
    {
        get => _archiveProgressFraction;
        private set => SetProperty(ref _archiveProgressFraction, value);
    }

    /// <summary><c>Extract</c> or <c>Create</c>, so the button says what it does.</summary>
    public string ArchiveActionLabel => _archiveMode == ArchiveMode.Zip ? "Create" : "Extract";

    public bool CanRunArchive => !_isArchiveBusy && _archiveMode != ArchiveMode.None;

    /// <summary>Whether the detached command-bar task can reopen its progress surface.</summary>
    public bool CanViewArchiveProgress => _isArchiveBusy && !_isArchiveOpen;

    /// <summary>The preview says Cancel; a live operation requires the explicit word Stop.</summary>
    public string ArchiveSecondaryActionLabel => _isArchiveBusy ? "Stop" : "Cancel";

    /// <summary>The rows of the preview, in plan order.</summary>
    public ObservableCollection<ArchiveRowViewModel> ArchiveRows { get; } = [];

    /// <summary>
    /// Whether the last archive operation can still be reversed. This is the in-session
    /// <c>[Undo]</c>; the durable <c>/undo</c> arrives with the SQLite journal.
    /// </summary>
    public bool CanUndoArchive
    {
        get => _canUndoArchive;
        private set
        {
            if (SetProperty(ref _canUndoArchive, value))
            {
                OnPropertyChanged(nameof(CanShowArchiveUndo));
            }
        }
    }

    /// <summary>Undo is shown only beside the archive result it actually describes.</summary>
    public bool CanShowArchiveUndo => _canUndoArchive && _archiveUndoMatchesResult;

    /// <summary>What the undo button would reverse, for its tooltip and assistive text.</summary>
    public string ArchiveUndoLabel
    {
        get => _archiveUndoLabel;
        private set => SetProperty(ref _archiveUndoLabel, value);
    }

    /// <summary>Handles a parsed <c>/unzip</c>: plans it, then previews or runs it.</summary>
    public async Task OpenUnzipAsync(UnzipInvocation request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var archives = request.ArchivePaths;
        var settings = _settings.Current.Archives;
        var collisions = request.CollisionPolicy ?? PreferredCollisionPolicy(settings);
        var skipPreview = request.SkipPreview ?? !settings.PreviewBeforeExtracting;

        _archiveMode = ArchiveMode.Unzip;
        _unzipRequest = request;
        _zipRequest = null;
        _zipPlan = null;
        _archiveIntoFolder = request.Layout == UnzipLayout.NewFolder;
        _archiveOverwrite = collisions == CollisionPolicy.Overwrite;
        ArchiveHidePreviewNextTime = false;
        _archiveFolderName = string.Empty;

        // Reading each archive's index and probing the destination is I/O, so it never runs on the
        // UI thread even though only the index is touched.
        string? failure = null;
        try
        {
            await Task.Run(
                () =>
                {
                    _archiveContents.Clear();
                    foreach (var archive in archives)
                    {
                        _archiveContents[archive] = _archiveReader.ReadEntries(archive, cancellationToken);
                    }
                },
                cancellationToken).ConfigureAwait(true);
        }
        catch (ArchiveReadException ex)
        {
            failure = ex.Message;
        }

        if (failure is not null)
        {
            ShowArchiveFailure(failure);
            return;
        }

        await Task.Run(() => BuildUnzipPlans(request), cancellationToken).ConfigureAwait(true);

        // The first plan supplies the proposed folder name, so the box shows what will happen.
        _archiveFolderName = _unzipPlans.Count == 1 ? _unzipPlans[0].FolderName ?? string.Empty : string.Empty;
        OnPropertyChanged(nameof(ArchiveFolderName));

        if (skipPreview)
        {
            PresentArchivePlan();
            _ = RunArchiveAsync();
            return;
        }

        ShowArchiveSheet();
    }

    /// <summary>Handles a parsed <c>/zip</c>: plans it, then previews or runs it.</summary>
    public async Task OpenZipAsync(ZipInvocation request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = _settings.Current.Archives;
        // Same precedence as /unzip: a switch wins for this command, otherwise the stored default.
        var collisions = request.CollisionPolicy ?? PreferredCollisionPolicy(settings);
        var skipPreview = request.SkipPreview ?? !settings.PreviewBeforeExtracting;

        _archiveMode = ArchiveMode.Zip;
        _zipRequest = request;
        _unzipRequest = null;
        _unzipPlans.Clear();
        _archiveContents.Clear();
        _archiveIntoFolder = true;
        _archiveOverwrite = collisions == CollisionPolicy.Overwrite;
        ArchiveHidePreviewNextTime = false;
        _archiveFolderName = string.Empty;

        // Enumerating the sources walks the tree, which is exactly the work that must stay off the
        // UI thread for a large folder.
        await Task.Run(() => BuildZipPlan(request), cancellationToken).ConfigureAwait(true);

        if (skipPreview)
        {
            PresentArchivePlan();
            _ = RunArchiveAsync();
            return;
        }

        ShowArchiveSheet();
    }

    /// <summary>
    /// Dismisses the archive surface. A live operation keeps running and remains reachable from the
    /// command-bar task strip; an idle preview is abandoned and its plan can be released.
    /// </summary>
    public void CloseArchive()
    {
        IsArchiveOpen = false;
        if (!_isArchiveBusy)
        {
            ClearArchivePlan();
        }
    }

    /// <summary>Reopens the progress surface for the archive task currently running.</summary>
    public void OpenArchiveProgress()
    {
        if (!_isArchiveBusy)
        {
            return;
        }

        CloseInfo();
        CloseWhere();
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        IsArchiveOpen = true;
    }

    /// <summary>Runs the planned operation and records it so it can be undone.</summary>
    /// <summary>
    /// The preview sheet's own "Don't show this again". It writes the durable Archives setting only
    /// when the operation is confirmed, so ticking it and then going Back changes nothing. The same
    /// preference also lives in Settings, Archives, or the tick would be a one-way door: once used,
    /// the sheet carrying it never opens again (owner decision, 2026-08-27).
    /// </summary>
    public bool ArchiveHidePreviewNextTime
    {
        get => _archiveHidePreviewNextTime;
        set => SetProperty(ref _archiveHidePreviewNextTime, value);
    }

    public async Task RunArchiveAsync()
    {
        if (_isArchiveBusy || _archiveMode == ArchiveMode.None)
        {
            return;
        }

        if (_archiveHidePreviewNextTime && _settings.Current.Archives.PreviewBeforeExtracting)
        {
            await SetArchivePreviewAsync(false).ConfigureAwait(true);
        }

        CancelArchiveRun();
        var cancellation = new CancellationTokenSource();
        _archiveRun = cancellation;
        _archiveRunWroteAnything = false;
        _archiveRunUndoRecorded = false;
        _archiveRunHistoryWarning = null;
        var mode = _archiveMode;

        IsArchiveBusy = true;
        ArchiveProgressFraction = 0;
        ArchiveProgressText = mode == ArchiveMode.Zip ? "Creating…" : "Extracting…";

        try
        {
            var summary = mode == ArchiveMode.Zip
                ? await RunZipAsync(cancellation.Token).ConfigureAwait(true)
                : await RunUnzipAsync(cancellation.Token).ConfigureAwait(true);

            IsArchiveOpen = false;
            _archiveMode = ArchiveMode.None;
            ApplyResult(CommandExecutionOutcome.Inline(
                summary.Severity, summary.Text, refreshListing: true));
            SetArchiveUndoResultAssociation(_archiveRunUndoRecorded);
        }
        catch (OperationCanceledException)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Info,
                DescribeStoppedArchiveRun(),
                refreshListing: true));
            SetArchiveUndoResultAssociation(_archiveRunUndoRecorded);
            IsArchiveOpen = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArchiveReadException)
        {
            ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, ex.Message));
            IsArchiveOpen = false;
        }
#pragma warning disable CA1031 // A detached archive task must report unexpected failure, never crash the shell.
        catch (Exception ex)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error, $"Archive operation failed: {ex.Message}", refreshListing: true));
            IsArchiveOpen = false;
        }
#pragma warning restore CA1031
        finally
        {
            IsArchiveBusy = false;
            ArchiveProgressText = string.Empty;
            _archiveRun = null;
            cancellation.Dispose();
            ClearArchivePlan();
            await RefreshFilesAfterArchiveChangeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Reverses the most recent archive operation of this session.</summary>
    public async Task UndoArchiveAsync()
    {
        if (_archiveUndoEntryId is not { } entryId)
        {
            CanUndoArchive = false;
            return;
        }

        JournalEntry? entry;
        try
        {
            entry = await Task.Run(() => _journal.FindAsync(entryId)).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Persistence failure must disable Undo without crashing the shell.
        catch (Exception ex)
        {
            MarkOperationJournalUnavailable();
            CanUndoArchive = false;
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error, OperationJournalUnavailableMessage(ex)));
            return;
        }
#pragma warning restore CA1031

        if (entry is null || !entry.CanAttemptUndo || entry.Kind is not ("unzip" or "zip"))
        {
            CanUndoArchive = false;
            _archiveUndoEntryId = null;
            ArchiveUndoLabel = string.Empty;
            SetArchiveUndoResultAssociation(matches: false);
            ApplyResult(CommandExecutionOutcome.Notice(
                "That archive operation is no longer available to undo."));
            return;
        }

        CanUndoArchive = false;

        try
        {
            var message = entry.Kind switch
            {
                "unzip" => await ExtractionUndo.UndoAsync(
                    DeserializeExtractionBatch(entry.PayloadJson))
                    .ConfigureAwait(true),
                "zip" => await CompressionUndo.UndoAsync(
                    JsonSerializer.Deserialize<CompressionOutcome>(entry.PayloadJson) ?? new CompressionOutcome())
                    .ConfigureAwait(true),
                _ => "Nothing to undo.",
            };

            var historyUpdated = await TryTransitionUndoAsync(
                    entry.Id,
                    OperationUndoState.Undone,
                    message)
                .ConfigureAwait(true);
            _archiveUndoEntryId = null;
            ArchiveUndoLabel = string.Empty;
            ApplyResult(CommandExecutionOutcome.Inline(
                historyUpdated ? CommandResultSeverity.Success : CommandResultSeverity.Warning,
                historyUpdated ? message : $"{message} History could not be updated; further Undo is disabled.",
                refreshListing: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await TryTransitionUndoAsync(entry.Id, OperationUndoState.UndoFailed, ex.Message)
                .ConfigureAwait(true);
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error, $"Could not undo: {ex.Message}"));
        }
        finally
        {
            // Archive execution and Undo run outside ExecuteCommandAsync, so they must refresh the
            // Files hierarchy themselves instead of relying on CommandExecutionOutcome.RefreshListing.
            await RefreshFilesAfterArchiveChangeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Explicitly stops a running extraction or compression.</summary>
    public void StopArchiveRun()
    {
        if (!_isArchiveBusy)
        {
            return;
        }

        ArchiveProgressText = "Stopping…";
        CancelArchiveRun();
    }

    private async Task<(CommandResultSeverity Severity, string Text)> RunUnzipAsync(CancellationToken cancellationToken)
    {
        var extracted = 0;
        var skipped = 0;
        var failures = new List<string>();
        var outcomes = new List<ExtractionOutcome>();

        try
        {
            foreach (var plan in _unzipPlans)
            {
                var progress = new Progress<ExtractionProgress>(report =>
                {
                    ArchiveProgressFraction = report.FilesTotal == 0
                        ? 0
                        : (double)report.FilesDone / report.FilesTotal;
                    ArchiveProgressText = report.CurrentEntry.Length == 0
                        ? "Finishing…"
                        : $"{report.FilesDone:N0} of {report.FilesTotal:N0} · {report.CurrentEntry}";
                });

                var outcome = await Extractor.ExtractAsync(plan, progress, cancellationToken).ConfigureAwait(true);
                outcomes.Add(outcome);
                extracted += outcome.CreatedFiles.Count;
                skipped += outcome.SkippedCount;
                failures.AddRange(outcome.Failures);

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            var batch = new ExtractionBatchOutcome(outcomes);
            _archiveRunWroteAnything = batch.WroteAnything;
            if (batch.WroteAnything)
            {
                var subject = outcomes.Count == 1
                    ? outcomes[0].ArchiveName
                    : Count(outcomes.Count, "archive");
                _archiveRunHistoryWarning = await TryRecordOperationAsync(
                    "unzip",
                    $"Extracted {subject}",
                    batch,
                    canUndo: true,
                    attachArchiveUndo: true).ConfigureAwait(true);
            }
        }

        var text = $"Extracted {Count(extracted, "file")}";
        if (skipped > 0)
        {
            text += $", left {Count(skipped, "existing file")} alone";
        }

        if (failures.Count > 0)
        {
            text += $". {Count(failures.Count, "entry")} failed: {failures[0]}";
            return (CommandResultSeverity.Error, AppendHistoryWarning(text + "."));
        }

        return (CommandResultSeverity.Success, AppendHistoryWarning(text + "."));
    }

    private async Task<(CommandResultSeverity Severity, string Text)> RunZipAsync(CancellationToken cancellationToken)
    {
        if (_zipPlan is not { } plan)
        {
            return (CommandResultSeverity.Error, "There is nothing to compress.");
        }

        var progress = new Progress<CompressionProgress>(report =>
        {
            ArchiveProgressFraction = report.FilesTotal == 0 ? 0 : (double)report.FilesDone / report.FilesTotal;
            ArchiveProgressText = report.CurrentEntry.Length == 0
                ? "Finishing…"
                : $"{report.FilesDone:N0} of {report.FilesTotal:N0} · {report.CurrentEntry}";
        });

        var outcome = await Compressor.CompressAsync(plan, progress, cancellationToken).ConfigureAwait(true);
        _archiveRunWroteAnything = outcome.FilesStored > 0;
        if (_archiveRunWroteAnything)
        {
            _archiveRunHistoryWarning = await TryRecordOperationAsync(
                    "zip",
                    $"Created {outcome.OutputName}",
                    outcome,
                    canUndo: true,
                    attachArchiveUndo: true)
                .ConfigureAwait(true);
        }

        if (outcome.FilesStored == 0 && outcome.Failures.Count > 0)
        {
            return (CommandResultSeverity.Error, AppendHistoryWarning(outcome.Failures[0]));
        }

        var text = $"Created {outcome.OutputName} — {Count(outcome.FilesStored, "file")}, " +
                   $"{ByteSize.Format(outcome.ArchiveBytes)}";
        return outcome.Failures.Count > 0
            ? (CommandResultSeverity.Error, AppendHistoryWarning(
                $"{text}. {Count(outcome.Failures.Count, "file")} failed."))
            : (CommandResultSeverity.Success, AppendHistoryWarning(text + "."));
    }

    private async Task<string?> TryRecordOperationAsync(
        string kind,
        string summary,
        object payload,
        bool canUndo,
        bool attachArchiveUndo = false,
        string? undoStatusDetail = null)
    {
        if (!_operationJournalAvailable)
        {
            return "History and Undo are unavailable for this session.";
        }

        var entry = new JournalEntry(
            Guid.NewGuid(),
            DateTimeOffset.Now,
            kind,
            summary,
            JsonSerializer.Serialize(payload),
            canUndo ? OperationUndoState.Undoable : OperationUndoState.NotUndoable,
            undoStatusDetail);
        try
        {
            await Task.Run(() => _journal.RecordAsync(entry)).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A history failure must never disguise a completed filesystem change.
        catch (Exception ex)
        {
            MarkOperationJournalUnavailable();
            return OperationJournalUnavailableMessage(ex);
        }
#pragma warning restore CA1031

        if (canUndo && attachArchiveUndo)
        {
            _archiveRunUndoRecorded = true;
            _archiveUndoEntryId = entry.Id;
            CanUndoArchive = true;
            ArchiveUndoLabel = summary;
        }

        return null;
    }

    private static ExtractionBatchOutcome DeserializeExtractionBatch(string payloadJson)
    {
        var batch = JsonSerializer.Deserialize<ExtractionBatchOutcome>(payloadJson);
        if (batch is { Outcomes.Count: > 0 })
        {
            return batch;
        }

        // Pre-persistence builds used a single-outcome payload for result-line Undo.
        var legacy = JsonSerializer.Deserialize<ExtractionOutcome>(payloadJson);
        return legacy is null ? new ExtractionBatchOutcome() : new ExtractionBatchOutcome([legacy]);
    }

    private string DescribeStoppedArchiveRun()
    {
        if (!_archiveRunWroteAnything)
        {
            return "Stopped. No archive changes were made.";
        }

        if (_archiveRunUndoRecorded)
        {
            return "Stopped. Anything already written can be undone.";
        }

        return AppendHistoryWarning("Stopped. Some archive changes were made.");
    }

    private string AppendHistoryWarning(string text) => _archiveRunHistoryWarning is null
        ? text
        : $"{text} {_archiveRunHistoryWarning}";

    private static string OperationJournalUnavailableMessage(Exception ex) =>
        $"History and Undo could not be recorded: {ex.Message}";

    private void MarkOperationJournalUnavailable()
    {
        _operationJournalAvailable = false;
        CanUndoArchive = false;
        _archiveUndoEntryId = null;
        ArchiveUndoLabel = string.Empty;
    }

    private async Task<bool> TryTransitionUndoAsync(Guid id, OperationUndoState state, string detail)
    {
        try
        {
            await Task.Run(() => _journal.TransitionUndoAsync(id, state, detail)).ConfigureAwait(true);
            return true;
        }
#pragma warning disable CA1031 // The filesystem result remains authoritative when history is unavailable.
        catch (Exception)
        {
            MarkOperationJournalUnavailable();
            return false;
        }
#pragma warning restore CA1031
    }

    private void BuildUnzipPlans(UnzipInvocation request)
    {
        _unzipPlans.Clear();
        var layout = _archiveIntoFolder ? UnzipLayout.NewFolder : UnzipLayout.NoRoot;
        var collisions = _archiveOverwrite ? CollisionPolicy.Overwrite : CollisionPolicy.Skip;

        foreach (var archive in request.ArchivePaths)
        {
            if (!_archiveContents.TryGetValue(archive, out var entries))
            {
                continue;
            }

            // One archive may have its folder renamed on the sheet; several keep their own names,
            // because a single name cannot describe them all.
            var folderName = request.ArchivePaths.Count == 1 && _archiveFolderName.Length > 0
                ? _archiveFolderName
                : null;

            _unzipPlans.Add(ArchivePlanner.Create(
                archive, request.DestinationPath, entries, layout, collisions, folderName));
        }
    }

    private void BuildZipPlan(ZipInvocation request) =>
        _zipPlan = ZipPlanner.Create(
            request.SourcePaths,
            request.OutputPath,
            _archiveIntoFolder,
            _archiveOverwrite ? CollisionPolicy.Overwrite : CollisionPolicy.Skip);

    /// <summary>
    /// Rebuilds the plan after a control changes. Planning probes the filesystem for collisions, so
    /// it is offloaded even though it is quick — a slow network destination must not freeze a
    /// checkbox.
    /// </summary>
    private async void Replan()
    {
        if (!_isArchiveOpen || _isArchiveBusy)
        {
            return;
        }

        try
        {
            if (_archiveMode == ArchiveMode.Unzip && _unzipRequest is { } unzip)
            {
                await Task.Run(() => BuildUnzipPlans(unzip)).ConfigureAwait(true);
            }
            else if (_archiveMode == ArchiveMode.Zip && _zipRequest is { } zip)
            {
                await Task.Run(() => BuildZipPlan(zip)).ConfigureAwait(true);
            }

            ShowArchiveSheet();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ArchiveWarning = ex.Message;
        }
    }

    private void ShowArchiveSheet()
    {
        CloseInfo();
        CloseWhere();
        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;

        PresentArchivePlan();
        IsArchiveOpen = true;
    }

    private void PresentArchivePlan()
    {
        if (_archiveMode == ArchiveMode.Zip)
        {
            PresentZipPlan();
        }
        else
        {
            PresentUnzipPlans();
        }

        OnPropertyChanged(nameof(ArchiveActionLabel));
        OnPropertyChanged(nameof(CanEditArchiveFolderName));
        OnPropertyChanged(nameof(CanRunArchive));
    }

    private void PresentUnzipPlans()
    {
        var files = _unzipPlans.Sum(plan => plan.FileCount);
        var bytes = _unzipPlans.Sum(plan => plan.TotalBytes);
        var collisions = _unzipPlans.Sum(plan => plan.Collisions.Count);
        var refused = _unzipPlans.Sum(plan => plan.Rejected.Count);

        ArchiveTitle = _unzipPlans.Count == 1
            ? $"Extract {_unzipPlans[0].ArchiveName}"
            : $"Extract {_unzipPlans.Count} archives";
        ArchiveSummary = $"{Count(files, "file")} · {ByteSize.Format(bytes)}";
        ArchiveDestination = _unzipPlans.Count == 1
            ? _unzipPlans[0].TargetRoot
            : _unzipRequest?.DestinationPath ?? string.Empty;

        ArchiveRows.Clear();
        var shown = 0;
        var total = 0;

        foreach (var plan in _unzipPlans)
        {
            foreach (var entry in plan.Entries.Where(entry => !entry.IsDirectory))
            {
                total++;
                if (shown < MaxArchiveRows)
                {
                    ArchiveRows.Add(ArchiveRowViewModel.File(
                        entry.RelativeTarget, ByteSize.Format(entry.Length)));
                    shown++;
                }
            }

            foreach (var rejected in plan.Rejected)
            {
                ArchiveRows.Add(ArchiveRowViewModel.Refused(rejected.EntryPath, rejected.Reason));
            }
        }

        if (total > shown)
        {
            ArchiveRows.Add(ArchiveRowViewModel.More(total - shown));
        }

        ArchiveWarning = BuildWarning(collisions, refused);
    }

    private void PresentZipPlan()
    {
        if (_zipPlan is not { } plan)
        {
            return;
        }

        ArchiveTitle = $"Create {plan.OutputName}";
        ArchiveSummary = $"{Count(plan.FileCount, "file")} · {ByteSize.Format(plan.TotalBytes)}";
        ArchiveDestination = plan.OutputPath;

        ArchiveRows.Clear();
        foreach (var entry in plan.Entries.Take(MaxArchiveRows))
        {
            ArchiveRows.Add(ArchiveRowViewModel.File(entry.EntryPath, ByteSize.Format(entry.Length)));
        }

        if (plan.FileCount > MaxArchiveRows)
        {
            ArchiveRows.Add(ArchiveRowViewModel.More(plan.FileCount - MaxArchiveRows));
        }

        foreach (var skipped in plan.Skipped)
        {
            ArchiveRows.Add(ArchiveRowViewModel.Refused(Path.GetFileName(skipped.SourcePath), skipped.Reason));
        }

        ArchiveWarning = plan.OutputExists
            ? _archiveOverwrite
                ? $"{plan.OutputName} already exists and will be replaced. The original goes to the Recycle Bin."
                : $"{plan.OutputName} already exists. Turn on Replace to overwrite it."
            : string.Empty;
    }

    private string BuildWarning(int collisions, int refused)
    {
        var parts = new List<string>();

        if (collisions > 0)
        {
            parts.Add(_archiveOverwrite
                ? $"{Count(collisions, "file")} will be replaced. Originals go to the Recycle Bin."
                : $"{Count(collisions, "file")} already exist and will be left alone.");
        }

        if (refused > 0)
        {
            parts.Add($"{Count(refused, "entry")} refused for safety.");
        }

        return string.Join(" ", parts);
    }

    private void ShowArchiveFailure(string message)
    {
        _archiveMode = ArchiveMode.None;
        IsArchiveOpen = false;
        ApplyResult(CommandExecutionOutcome.Inline(CommandResultSeverity.Error, message));
    }

    private void CancelArchiveRun()
    {
        if (_archiveRun is { } running)
        {
            running.Cancel();
        }
    }

    private void ClearArchivePlan()
    {
        _archiveMode = ArchiveMode.None;
        _unzipRequest = null;
        _zipRequest = null;
        ArchiveRows.Clear();
        _unzipPlans.Clear();
        _archiveContents.Clear();
        _zipPlan = null;
    }

    private async Task RefreshFilesAfterArchiveChangeAsync()
    {
        if (_currentPath is not { } path)
        {
            return;
        }

        if (Directory.Exists(path))
        {
            _ = await RefreshFilesAsync(CancellationToken.None).ConfigureAwait(true);
            return;
        }

        // Undo can remove the folder the user entered after extraction. Keep Files on the nearest
        // surviving parent instead of leaving a dead location in the hierarchy.
        if (NearestExistingAncestor(path) is { } ancestor)
        {
            await NavigateToAsync(ancestor).ConfigureAwait(true);
        }
    }

    private void SetArchiveUndoResultAssociation(bool matches)
    {
        if (_archiveUndoMatchesResult == matches)
        {
            return;
        }

        _archiveUndoMatchesResult = matches;
        OnPropertyChanged(nameof(CanShowArchiveUndo));
    }

    private static CollisionPolicy PreferredCollisionPolicy(ArchiveSettings settings) =>
        settings.WhenAFileExists == CollisionPreference.Overwrite
            ? CollisionPolicy.Overwrite
            : CollisionPolicy.Skip;

    private static string Count(int value, string noun) =>
        value == 1 ? $"1 {noun}" : $"{value:N0} {noun}s";
}
