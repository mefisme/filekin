using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Commands.App.Tidy;
using Filekin.Core.FileSystem;
using Filekin.Core.Tidy;
using Filekin.Infrastructure.Windows.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>
/// The <c>/tidy</c> surface.
///
/// ARCHITECTURE.md Topic 5X originally had Tidy run the moment Enter was pressed. The owner
/// superseded that on 2026-08-27: Tidy shows its plan first, exactly as <c>/unzip</c> already does,
/// because the same user already knows that shape. <c>-y</c> skips the plan for one run, the Tidy
/// settings toggle skips it always, and a "Don't show this again" tick on the plan itself writes that
/// same setting so the preference can be turned off from where it is noticed.
///
/// The plan's ticks are per category and reset every run. Which categories were chosen is never
/// persisted, so Tidy cannot quietly do less than the user expects for a reason set days ago.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>File names sampled onto each category row, enough to recognise what is in it.</summary>
    private const int TidySampleCount = 3;

    private TidyPlan? _tidyPlan;
    private CancellationTokenSource? _tidyRun;

    private bool _isTidyOpen;
    private string _tidyFolderPath = string.Empty;
    private string _tidySummary = string.Empty;
    private string _tidySkippedText = string.Empty;
    private bool _isTidyBusy;
    private string _tidyProgressText = string.Empty;
    private double _tidyProgressFraction;
    private bool _tidyHidePreviewNextTime;

    /// <summary>The category rows of the current plan, in presentation order.</summary>
    public ObservableCollection<TidyGroupViewModel> TidyGroups { get; } = [];

    /// <summary>Whether the Tidy plan is showing over the Files hierarchy.</summary>
    public bool IsTidyOpen
    {
        get => _isTidyOpen;
        private set
        {
            if (SetProperty(ref _isTidyOpen, value))
            {
                OnPropertyChanged(nameof(IsFilesContentVisible));
                OnPropertyChanged(nameof(WorkspaceSelectionStatus));
                OnPropertyChanged(nameof(CanViewTidyProgress));
            }
        }
    }

    public string TidyTitle =>
        $"Tidy — {Path.GetFileName(_tidyFolderPath.TrimEnd(Path.DirectorySeparatorChar))}";

    public string TidyFolderPath
    {
        get => _tidyFolderPath;
        private set
        {
            if (SetProperty(ref _tidyFolderPath, value))
            {
                OnPropertyChanged(nameof(TidyTitle));
            }
        }
    }

    /// <summary>The count above the rows, reflecting only the ticked categories.</summary>
    public string TidySummary
    {
        get => _tidySummary;
        private set => SetProperty(ref _tidySummary, value);
    }

    /// <summary>Why files will not move, when any will not. Empty otherwise.</summary>
    public string TidySkippedText
    {
        get => _tidySkippedText;
        private set
        {
            if (SetProperty(ref _tidySkippedText, value))
            {
                OnPropertyChanged(nameof(HasTidySkipped));
            }
        }
    }

    public bool HasTidySkipped => _tidySkippedText.Length > 0;

    public bool IsTidyBusy
    {
        get => _isTidyBusy;
        private set
        {
            if (SetProperty(ref _isTidyBusy, value))
            {
                OnPropertyChanged(nameof(CanStartTidy));
                OnPropertyChanged(nameof(IsTidyPlanEditable));
                OnPropertyChanged(nameof(CanViewTidyProgress));
                OnPropertyChanged(nameof(TidySecondaryActionLabel));
            }
        }
    }

    /// <summary>False while a run is under way, when the plan is a progress readout rather than a form.</summary>
    public bool IsTidyPlanEditable => !_isTidyBusy;

    /// <summary>The task strip offers View only when the live surface is not already showing.</summary>
    public bool CanViewTidyProgress => _isTidyBusy && !_isTidyOpen;

    /// <summary>The plan says Cancel; a live run requires the explicit word Stop.</summary>
    public string TidySecondaryActionLabel => _isTidyBusy ? "Stop" : "Cancel";

    public string TidyProgressText
    {
        get => _tidyProgressText;
        private set => SetProperty(ref _tidyProgressText, value);
    }

    public double TidyProgressFraction
    {
        get => _tidyProgressFraction;
        private set => SetProperty(ref _tidyProgressFraction, value);
    }

    /// <summary>
    /// The plan's own "Don't show this again". It writes the durable Tidy setting only when the run
    /// is confirmed: ticking it and then cancelling changes nothing, because the user abandoned the
    /// whole action.
    /// </summary>
    public bool TidyHidePreviewNextTime
    {
        get => _tidyHidePreviewNextTime;
        set => SetProperty(ref _tidyHidePreviewNextTime, value);
    }

    public bool CanStartTidy => !_isTidyBusy && TidyGroups.Any(group => group.IsSelected);

    private void OnTidyGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TidyGroupViewModel.IsSelected))
        {
            RefreshTidySelection();
        }
    }

    /// <summary>Recomputes the summary after a category tick changes.</summary>
    public void RefreshTidySelection()
    {
        var files = TidyGroups.Where(group => group.IsSelected).Sum(group => group.Count);
        var folders = TidyGroups.Count(group => group.IsSelected);
        TidySummary = files == 0
            ? "Nothing selected."
            : $"{files:N0} {(files == 1 ? "file" : "files")} into {folders} {(folders == 1 ? "folder" : "folders")}";
        OnPropertyChanged(nameof(CanStartTidy));
    }

    /// <summary>Closes the Tidy plan without running it.</summary>
    public void CloseTidy()
    {
        IsTidyOpen = false;
        if (!_isTidyBusy)
        {
            _tidyPlan = null;
            ClearTidyRows();
        }
    }

    private void ClearTidyRows()
    {
        foreach (var row in TidyGroups)
        {
            row.PropertyChanged -= OnTidyGroupChanged;
        }

        TidyGroups.Clear();
    }

    /// <summary>Stops a running tidy. Files already moved stay moved; the result says how many.</summary>
    public void StopTidy() => _tidyRun?.Cancel();

    /// <summary>Reopens the live surface for a run whose plan was dismissed.</summary>
    public void ViewTidyProgress()
    {
        CloseWhere();
        IsTidyOpen = true;
    }

    /// <summary>
    /// The plan's second button. Idle it abandons the plan; mid-run it stops the move, because a
    /// button labelled Cancel beside a running operation must actually cancel that operation.
    /// </summary>
    public void TidySecondaryAction()
    {
        if (_isTidyBusy)
        {
            StopTidy();
            return;
        }

        CloseTidy();
    }

    private async Task OpenTidyAsync(TidyInvocation request)
    {
        var operations = new WindowsFileSystemOperations();
        if (operations.GetKind(request.FolderPath) != FileSystemEntryKind.Directory)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error,
                $"{request.FolderPath} is not a folder."));
            return;
        }

        // Listing the folder and probing every destination is real I/O.
        TidyPlan plan;
        try
        {
            plan = await Task.Run(() => new TidyPlanner(_lister, operations).Plan(request.FolderPath))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Error,
                $"/tidy failed: {ex.Message}"));
            return;
        }

        if (!plan.HasWork)
        {
            ApplyResult(CommandExecutionOutcome.Inline(
                CommandResultSeverity.Success,
                plan.Skipped.Count == 0
                    ? "Nothing to tidy — no loose files here."
                    : $"Nothing to tidy — {DescribeSkips(plan.Skipped)}."));
            return;
        }

        _tidyPlan = plan;
        TidyFolderPath = plan.FolderPath;
        TidyHidePreviewNextTime = false;
        BuildTidyRows(plan, operations);

        var skipPreview = request.SkipPreview ?? !_settings.Current.Tidy.PreviewBeforeTidying;
        if (skipPreview)
        {
            await RunTidyAsync().ConfigureAwait(true);
            return;
        }

        IsRecycleBinOpen = false;
        IsPlacesOpen = false;
        IsDrivesOpen = false;
        IsSettingsOpen = false;
        CloseInfo();
        CloseWhere();
        CloseArchive();
        IsAgentsOpen = false;
        IsAgentProjectsOpen = false;
        IsTidyOpen = true;
    }

    private void BuildTidyRows(TidyPlan plan, WindowsFileSystemOperations operations)
    {
        ClearTidyRows();
        foreach (var group in plan.Groups)
        {
            var samples = group.Items.Take(TidySampleCount).Select(item => item.Name).ToList();
            if (group.Count > samples.Count)
            {
                samples.Add($"and {group.Count - samples.Count:N0} more");
            }

            var row = new TidyGroupViewModel(
                group,
                samples,
                operations.GetKind(group.DestinationPath) == FileSystemEntryKind.Directory);

            // The header count and the Tidy button both follow the ticks, so the surface has to hear
            // about a row the user changed.
            row.PropertyChanged += OnTidyGroupChanged;
            TidyGroups.Add(row);
        }

        TidySkippedText = plan.Skipped.Count == 0 ? string.Empty : DescribeSkips(plan.Skipped);
        RefreshTidySelection();
    }

    /// <summary>Runs the plan against the ticked categories and reports on the command bar.</summary>
    public async Task RunTidyAsync()
    {
        if (_tidyPlan is not { } plan || _isTidyBusy)
        {
            return;
        }

        var categories = TidyGroups
            .Where(group => group.IsSelected)
            .Select(group => group.Category)
            .ToList();
        if (categories.Count == 0)
        {
            return;
        }

        // The tick is honoured only now, on confirmation.
        if (_tidyHidePreviewNextTime && _settings.Current.Tidy.PreviewBeforeTidying)
        {
            await SetTidyPreviewAsync(false).ConfigureAwait(true);
        }

        _tidyRun = new CancellationTokenSource();
        IsTidyBusy = true;
        TidyProgressFraction = 0;
        TidyProgressText = "Starting…";

        var progress = new Progress<TidyProgress>(report =>
        {
            TidyProgressFraction = report.FilesTotal == 0
                ? 0
                : (double)report.FilesDone / report.FilesTotal;
            TidyProgressText = report.CurrentName.Length == 0
                ? "Finishing…"
                : $"{report.FilesDone:N0} of {report.FilesTotal:N0} · {report.CurrentName}";
        });

        TidyOutcome? outcome = null;
        var cancelled = false;
        try
        {
            var runner = new TidyRunner(new WindowsFileSystemOperations());
            outcome = await Task.Run(
                () => runner.Run(plan, categories, progress, _tidyRun.Token),
                _tidyRun.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            IsTidyBusy = false;
            _tidyRun?.Dispose();
            _tidyRun = null;
            _tidyPlan = null;
            ClearTidyRows();

            // Closing here rather than at cancellation keeps a detached run visible in the task
            // strip for its whole life, the same way archive work is owned by Files.
            IsTidyOpen = false;

            // Files that moved changed the visible folder, whether or not the run finished. Tidy only
            // ever creates subfolders inside it, so the folder itself always survives and a plain
            // re-list is enough.
            if (_currentPath is not null)
            {
                _ = await RefreshFilesAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        if (cancelled)
        {
            ApplyResult(CommandExecutionOutcome.Notice(
                "Tidy stopped. Files already moved were left in place."));
            return;
        }

        if (outcome is null)
        {
            return;
        }

        // Topic 5W: /tidy is recorded in history but is not undoable in v1. A no-op does not enter
        // filesystem history because Filekin did not mutate anything.
        string? historyWarning = null;
        if (outcome.MovedCount > 0)
        {
            historyWarning = await TryRecordOperationAsync(
                    "tidy",
                    $"Tidied {Path.GetFileName(outcome.FolderPath)}",
                    outcome,
                    canUndo: false)
                .ConfigureAwait(true);
        }

        ApplyResult(CommandExecutionOutcome.Inline(
            outcome.Failures.Count > 0 ? CommandResultSeverity.Error : CommandResultSeverity.Success,
            historyWarning is null
                ? DescribeTidyOutcome(outcome)
                : $"{DescribeTidyOutcome(outcome)} {historyWarning}"));
    }

    private static string DescribeTidyOutcome(TidyOutcome outcome)
    {
        var parts = new List<string>
        {
            outcome.MovedCount == 1 ? "Tidied 1 file" : $"Tidied {outcome.MovedCount:N0} files",
        };

        if (outcome.Skipped.Count > 0)
        {
            parts.Add($"{outcome.Skipped.Count:N0} skipped");
        }

        if (outcome.Failures.Count > 0)
        {
            parts.Add($"{outcome.Failures.Count:N0} failed");
        }

        return string.Join(" · ", parts) + ".";
    }

    /// <summary>Groups refusals by reason so the line stays short however many there are.</summary>
    private static string DescribeSkips(IReadOnlyList<TidySkip> skipped) =>
        string.Join(
            ", ",
            skipped
                .GroupBy(skip => skip.Reason, StringComparer.Ordinal)
                .Select(group => $"{group.Count():N0} {group.Key}"));
}
