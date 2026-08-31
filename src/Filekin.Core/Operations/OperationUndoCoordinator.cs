using System.Text.Json;
using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Operations;

public enum CoordinatedUndoAvailability
{
    Ready,
    NeedsDecision,
    Unavailable,
    NotFound,
}

/// <summary>
/// Present-state result for one exact journal row. The operation-specific assessment is retained so
/// a future conflict surface can render the evidence that was actually reviewed.
/// </summary>
public sealed record CoordinatedUndoEvaluation(
    CoordinatedUndoAvailability Availability,
    JournalEntry? Entry,
    string Detail,
    RelocationUndoAssessment? Relocation = null,
    TossRestoreAssessment? Toss = null,
    ArchiveUndoAssessment? Archive = null);

/// <summary>User decisions whose evidence must be revalidated by the operation executor.</summary>
public sealed record OperationUndoDecisions
{
    public OperationUndoDecisions(IReadOnlyList<ArchiveEditedOutputResolution> archiveEditedOutputs)
    {
        ArgumentNullException.ThrowIfNull(archiveEditedOutputs);
        if (archiveEditedOutputs.Any(static resolution => resolution is null))
        {
            throw new ArgumentException("Archive Undo decisions cannot contain null values.", nameof(archiveEditedOutputs));
        }

        ArchiveEditedOutputs = [.. archiveEditedOutputs];
    }

    public OperationUndoDecisions()
        : this([])
    {
    }

    public IReadOnlyList<ArchiveEditedOutputResolution> ArchiveEditedOutputs { get; }
}

public enum CoordinatedUndoOutcome
{
    Undone,
    PartiallyUndone,
    Failed,
    NeedsDecision,
    Cancelled,
    Unavailable,
    NotFound,
}

/// <summary>The authoritative outcome of attempting one exact journal entry.</summary>
public sealed record CoordinatedUndoResult(
    CoordinatedUndoOutcome Outcome,
    JournalEntry? Entry,
    string Detail,
    bool MayHaveChangedFileSystem,
    CoordinatedUndoEvaluation Evaluation);

/// <summary>
/// Owns the exact-entry boundary shared by result-line Undo, <c>/undo</c>, and future history rows.
/// It dispatches only from the journal's declared operation kind, reevaluates current state, executes
/// at most once, and atomically stores both the lifecycle and exact work left for a retry.
/// </summary>
public sealed class OperationUndoCoordinator : IDisposable
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IOperationJournal _journal;
    private readonly RelocationUndoEvaluator _relocationEvaluator;
    private readonly RelocationUndo _relocationUndo;
    private readonly TossRestoreEvaluator _tossEvaluator;
    private readonly TossRestore _tossRestore;
    private readonly ArchiveUndoEvaluator _archiveEvaluator;
    private readonly ArchiveUndoExecutor _archiveUndo;
    private readonly SemaphoreSlim _attemptGate = new(1, 1);
    private bool _disposed;

    public OperationUndoCoordinator(
        IOperationJournal journal,
        IFileSystemOperations fileSystem,
        IRecycleBin recycleBin,
        IArchiveOutputEvidenceReader archiveEvidenceReader,
        IArchiveUndoStorage archiveStorage)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(recycleBin);
        ArgumentNullException.ThrowIfNull(archiveEvidenceReader);
        ArgumentNullException.ThrowIfNull(archiveStorage);
        _journal = journal;
        _relocationEvaluator = new RelocationUndoEvaluator(fileSystem);
        _relocationUndo = new RelocationUndo(fileSystem);
        _tossEvaluator = new TossRestoreEvaluator(fileSystem, recycleBin);
        _tossRestore = new TossRestore(fileSystem, recycleBin);
        _archiveEvaluator = new ArchiveUndoEvaluator(fileSystem, recycleBin, archiveEvidenceReader);
        _archiveUndo = new ArchiveUndoExecutor(fileSystem, recycleBin, archiveEvidenceReader, archiveStorage);
    }

    public async Task<CoordinatedUndoEvaluation> EvaluateAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _attemptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAndEvaluateAsync(entryId, cancellationToken).ConfigureAwait(false);
            return loaded.Evaluation;
        }
        finally
        {
            _attemptGate.Release();
        }
    }

    public async Task<CoordinatedUndoResult> UndoAsync(
        Guid entryId,
        OperationUndoDecisions? decisions = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _attemptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAndEvaluateAsync(entryId, cancellationToken).ConfigureAwait(false);
            var evaluation = loaded.Evaluation;
            if (evaluation.Entry is null)
            {
                return Result(CoordinatedUndoOutcome.NotFound, evaluation, changed: false);
            }

            if (!evaluation.Entry.CanAttemptUndo)
            {
                return Result(CoordinatedUndoOutcome.Unavailable, evaluation, changed: false);
            }

            if (loaded.Payload is null)
            {
                return await MakeUnavailableAsync(evaluation.Entry, evaluation, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (evaluation.Availability == CoordinatedUndoAvailability.Unavailable)
            {
                return await MakeUnavailableAsync(evaluation.Entry, evaluation, cancellationToken)
                    .ConfigureAwait(false);
            }

            decisions ??= new OperationUndoDecisions();
            if (evaluation.Availability == CoordinatedUndoAvailability.NeedsDecision &&
                (loaded.Payload is not ArchivePayload || decisions.ArchiveEditedOutputs.Count == 0))
            {
                return Result(CoordinatedUndoOutcome.NeedsDecision, evaluation, changed: false);
            }

            return loaded.Payload switch
            {
                RelocationPayload relocation => await ApplyRelocationAsync(
                    evaluation.Entry,
                    evaluation,
                    relocation.Value,
                    cancellationToken).ConfigureAwait(false),
                TossPayload toss => await ApplyTossAsync(
                    evaluation.Entry,
                    evaluation,
                    toss.Value,
                    cancellationToken).ConfigureAwait(false),
                ArchivePayload archive => await ApplyArchiveAsync(
                    evaluation.Entry,
                    evaluation,
                    archive.Value,
                    decisions.ArchiveEditedOutputs,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("The evaluated Undo payload has no handler."),
            };
        }
        finally
        {
            _attemptGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _attemptGate.Dispose();
    }

    private async Task<LoadedUndo> LoadAndEvaluateAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await _journal.FindAsync(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new LoadedUndo(
                new CoordinatedUndoEvaluation(
                    CoordinatedUndoAvailability.NotFound,
                    null,
                    "That operation is no longer in history."),
                null);
        }

        if (!entry.CanAttemptUndo)
        {
            return new LoadedUndo(
                new CoordinatedUndoEvaluation(
                    CoordinatedUndoAvailability.Unavailable,
                    entry,
                    entry.UndoStatusDetail ?? "That operation is not available to undo."),
                null);
        }

        ParsedPayload payload;
        try
        {
            payload = ParsePayload(entry);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new LoadedUndo(
                new CoordinatedUndoEvaluation(
                    CoordinatedUndoAvailability.Unavailable,
                    entry,
                    $"Undo data for this {entry.Kind} operation is invalid: {ex.Message}"),
                null);
        }

        var evaluation = await Task.Run(
                () => Evaluate(entry, payload),
                cancellationToken)
            .ConfigureAwait(false);
        return new LoadedUndo(evaluation, payload);
    }

    private CoordinatedUndoEvaluation Evaluate(JournalEntry entry, ParsedPayload payload) => payload switch
    {
        RelocationPayload relocation => EvaluateRelocation(entry, relocation.Value),
        TossPayload toss => EvaluateToss(entry, toss.Value),
        ArchivePayload archive => EvaluateArchive(entry, archive.Value),
        _ => throw new InvalidOperationException("The parsed Undo payload has no evaluator."),
    };

    private CoordinatedUndoEvaluation EvaluateRelocation(
        JournalEntry entry,
        RelocationOperationPayload payload)
    {
        if (payload.PendingRelocations.Count == 0)
        {
            return Unavailable(entry, "No moved items remain to undo.");
        }

        var assessment = _relocationEvaluator.Evaluate(payload);
        return assessment.Safety switch
        {
            RelocationUndoSafety.Ready => Ready(entry, "The move can be undone now.", relocation: assessment),
            RelocationUndoSafety.NeedsConflictResolution => NeedsDecision(
                entry,
                assessment.Issues[0].Message,
                relocation: assessment),
            _ => Unavailable(entry, assessment.Issues[0].Message, relocation: assessment),
        };
    }

    private CoordinatedUndoEvaluation EvaluateToss(JournalEntry entry, TossOperationPayload payload)
    {
        if (payload.PendingItems.Count == 0)
        {
            return Unavailable(entry, "No recycled items remain to restore.");
        }

        var assessment = _tossEvaluator.Evaluate(payload);
        return assessment.Safety switch
        {
            TossRestoreSafety.Ready => Ready(entry, "The recycled items can be restored now.", toss: assessment),
            TossRestoreSafety.NeedsConflictResolution => NeedsDecision(
                entry,
                assessment.Issues[0].Message,
                toss: assessment),
            _ => Unavailable(entry, assessment.Issues[0].Message, toss: assessment),
        };
    }

    private CoordinatedUndoEvaluation EvaluateArchive(JournalEntry entry, ArchiveUndoPayload payload)
    {
        if (!payload.HasPendingWork)
        {
            return Unavailable(entry, "No archive changes remain to undo.");
        }

        var assessment = _archiveEvaluator.Evaluate(payload);
        return assessment.Safety switch
        {
            ArchiveUndoSafety.Ready => Ready(entry, "The archive operation can be undone now.", archive: assessment),
            ArchiveUndoSafety.NeedsEditedOutputDecision => NeedsDecision(
                entry,
                assessment.EditedOutputs[0].Output.Message,
                archive: assessment),
            _ => Unavailable(entry, assessment.Issues[0].Message, archive: assessment),
        };
    }

    private async Task<CoordinatedUndoResult> ApplyRelocationAsync(
        JournalEntry entry,
        CoordinatedUndoEvaluation evaluation,
        RelocationOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(() => _relocationUndo.Undo(payload), cancellationToken)
            .ConfigureAwait(false);
        var detail = Describe(result);
        var state = result.Outcome switch
        {
            RelocationUndoOutcome.Succeeded => OperationUndoState.Undone,
            RelocationUndoOutcome.PartiallyUndone => OperationUndoState.PartiallyUndone,
            _ => OperationUndoState.UndoFailed,
        };
        return await CommitAsync(
            entry,
            evaluation,
            result.UpdatedPayload,
            state,
            detail,
            result.MayHaveChangedFileSystem,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoordinatedUndoResult> ApplyTossAsync(
        JournalEntry entry,
        CoordinatedUndoEvaluation evaluation,
        TossOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(() => _tossRestore.Restore(payload), cancellationToken)
            .ConfigureAwait(false);
        var detail = Describe(result);
        var state = result.Outcome switch
        {
            TossRestoreOutcome.Succeeded => OperationUndoState.Undone,
            TossRestoreOutcome.PartiallyRestored => OperationUndoState.PartiallyUndone,
            _ => OperationUndoState.UndoFailed,
        };
        return await CommitAsync(
            entry,
            evaluation,
            result.UpdatedPayload,
            state,
            detail,
            result.MayHaveChangedFileSystem,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoordinatedUndoResult> ApplyArchiveAsync(
        JournalEntry entry,
        CoordinatedUndoEvaluation evaluation,
        ArchiveUndoPayload payload,
        IReadOnlyList<ArchiveEditedOutputResolution> decisions,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(() => _archiveUndo.Execute(payload, decisions), cancellationToken)
            .ConfigureAwait(false);
        if (result.Outcome == ArchiveUndoAttemptOutcome.Cancelled)
        {
            return new CoordinatedUndoResult(
                CoordinatedUndoOutcome.Cancelled,
                entry,
                "Undo was cancelled. No changes were made.",
                false,
                evaluation);
        }

        var detail = Describe(result);
        var state = result.Outcome switch
        {
            ArchiveUndoAttemptOutcome.Succeeded => OperationUndoState.Undone,
            ArchiveUndoAttemptOutcome.PartiallyUndone => OperationUndoState.PartiallyUndone,
            _ => OperationUndoState.UndoFailed,
        };
        return await CommitAsync(
            entry,
            evaluation,
            result.UpdatedPayload,
            state,
            detail,
            result.MayHaveChangedFileSystem,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoordinatedUndoResult> CommitAsync<TPayload>(
        JournalEntry entry,
        CoordinatedUndoEvaluation evaluation,
        TPayload updatedPayload,
        OperationUndoState state,
        string detail,
        bool changed,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(updatedPayload, PayloadJsonOptions);
        await _journal.ApplyUndoResultAsync(
                entry,
                payloadJson,
                state,
                detail,
                cancellationToken)
            .ConfigureAwait(false);
        var updated = entry.TransitionUndo(state, detail) with { PayloadJson = payloadJson };
        var outcome = state switch
        {
            OperationUndoState.Undone => CoordinatedUndoOutcome.Undone,
            OperationUndoState.PartiallyUndone => CoordinatedUndoOutcome.PartiallyUndone,
            _ => CoordinatedUndoOutcome.Failed,
        };
        return new CoordinatedUndoResult(outcome, updated, detail, changed, evaluation);
    }

    private async Task<CoordinatedUndoResult> MakeUnavailableAsync(
        JournalEntry entry,
        CoordinatedUndoEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        await _journal.ApplyUndoResultAsync(
                entry,
                entry.PayloadJson,
                OperationUndoState.Unavailable,
                evaluation.Detail,
                cancellationToken)
            .ConfigureAwait(false);
        var updated = entry.TransitionUndo(OperationUndoState.Unavailable, evaluation.Detail);
        return new CoordinatedUndoResult(
            CoordinatedUndoOutcome.Unavailable,
            updated,
            evaluation.Detail,
            false,
            evaluation);
    }

    private static ParsedPayload ParsePayload(JournalEntry entry) => entry.Kind switch
    {
        "move" or "rename" => new RelocationPayload(DeserializeRequired<RelocationOperationPayload>(
            entry.PayloadJson,
            entry.Kind)),
        "toss" => new TossPayload(DeserializeRequired<TossOperationPayload>(entry.PayloadJson, entry.Kind)),
        "unzip" => new ArchivePayload(ParseUnzipPayload(entry.PayloadJson)),
        "zip" => new ArchivePayload(ParseZipPayload(entry.PayloadJson)),
        _ => throw new InvalidOperationException($"Undo does not support operation kind '{entry.Kind}'."),
    };

    private static ArchiveUndoPayload ParseUnzipPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (HasProperty(document.RootElement, nameof(ArchiveUndoPayload.Archives)))
        {
            return DeserializeRequired<ArchiveUndoPayload>(json, "unzip");
        }

        if (HasProperty(document.RootElement, nameof(ExtractionBatchOutcome.Outcomes)))
        {
            return ArchiveUndoPayload.FromExtraction(
                DeserializeRequired<ExtractionBatchOutcome>(json, "unzip"));
        }

        return ArchiveUndoPayload.FromExtraction(new ExtractionBatchOutcome([
            DeserializeRequired<ExtractionOutcome>(json, "unzip"),
        ]));
    }

    private static ArchiveUndoPayload ParseZipPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return HasProperty(document.RootElement, nameof(ArchiveUndoPayload.Archives))
            ? DeserializeRequired<ArchiveUndoPayload>(json, "zip")
            : ArchiveUndoPayload.FromCompression(DeserializeRequired<CompressionOutcome>(json, "zip"));
    }

    private static T DeserializeRequired<T>(string json, string kind) where T : class =>
        JsonSerializer.Deserialize<T>(json, PayloadJsonOptions) ??
        throw new JsonException($"The {kind} payload was empty.");

    private static bool HasProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static CoordinatedUndoEvaluation Ready(
        JournalEntry entry,
        string detail,
        RelocationUndoAssessment? relocation = null,
        TossRestoreAssessment? toss = null,
        ArchiveUndoAssessment? archive = null) =>
        new(CoordinatedUndoAvailability.Ready, entry, detail, relocation, toss, archive);

    private static CoordinatedUndoEvaluation NeedsDecision(
        JournalEntry entry,
        string detail,
        RelocationUndoAssessment? relocation = null,
        TossRestoreAssessment? toss = null,
        ArchiveUndoAssessment? archive = null) =>
        new(CoordinatedUndoAvailability.NeedsDecision, entry, detail, relocation, toss, archive);

    private static CoordinatedUndoEvaluation Unavailable(
        JournalEntry entry,
        string detail,
        RelocationUndoAssessment? relocation = null,
        TossRestoreAssessment? toss = null,
        ArchiveUndoAssessment? archive = null) =>
        new(CoordinatedUndoAvailability.Unavailable, entry, detail, relocation, toss, archive);

    private static CoordinatedUndoResult Result(
        CoordinatedUndoOutcome outcome,
        CoordinatedUndoEvaluation evaluation,
        bool changed) =>
        new(outcome, evaluation.Entry, evaluation.Detail, changed, evaluation);

    private static string Describe(RelocationUndoResult result) => result.Outcome switch
    {
        RelocationUndoOutcome.Succeeded => Count(result.ReversedRelocations.Count, "moved item") +
            " restored to its original location.",
        RelocationUndoOutcome.PartiallyUndone =>
            $"Restored {Count(result.ReversedRelocations.Count, "moved item")}; " +
            $"{Count(result.RemainingRelocations.Count, "item")} still could not be restored.",
        _ => FirstIssue(result.BlockedBy?.Issues) ??
            FirstFailure(result.Failures) ?? "The move could not be undone.",
    };

    private static string Describe(TossRestoreResult result) => result.Outcome switch
    {
        TossRestoreOutcome.Succeeded => Count(result.RestoredItems.Count, "recycled item") + " restored.",
        TossRestoreOutcome.PartiallyRestored =>
            $"Restored {Count(result.RestoredItems.Count, "recycled item")}; " +
            $"{Count(result.RemainingItems.Count, "item")} still could not be restored.",
        _ => FirstIssue(result.BlockedBy?.Issues) ??
            FirstFailure(result.Failures) ?? "The recycled items could not be restored.",
    };

    private static string Describe(ArchiveUndoResult result) => result.Outcome switch
    {
        ArchiveUndoAttemptOutcome.Succeeded => "The archive changes were undone.",
        ArchiveUndoAttemptOutcome.PartiallyUndone when result.UpdatedPayload.HasPendingWork =>
            "Some archive changes were undone; remaining work can be retried.",
        ArchiveUndoAttemptOutcome.PartiallyUndone =>
            "The archive operation was only partially undone because some current content was kept.",
        _ => FirstArchiveIssue(result.BlockedBy) ??
            FirstArchiveFailure(result.Failures) ?? "The archive changes could not be undone.",
    };

    private static string? FirstIssue(IReadOnlyList<RelocationUndoIssue>? issues) =>
        issues is { Count: > 0 } ? issues[0].Message : null;

    private static string? FirstIssue(IReadOnlyList<TossRestoreIssue>? issues) =>
        issues is { Count: > 0 } ? issues[0].Message : null;

    private static string? FirstFailure(IReadOnlyList<RelocationUndoFailure> failures) =>
        failures.Count > 0 ? failures[0].Message : null;

    private static string? FirstFailure(IReadOnlyList<TossRestoreFailure> failures) =>
        failures.Count > 0 ? failures[0].Message : null;

    private static string? FirstArchiveIssue(ArchiveUndoAssessment? assessment) =>
        assessment is null
            ? null
            : assessment.Issues.Count > 0
                ? assessment.Issues[0].Message
                : assessment.EditedOutputs.Count > 0
                    ? assessment.EditedOutputs[0].Output.Message
                    : null;

    private static string? FirstArchiveFailure(IReadOnlyList<ArchiveUndoFailure> failures) =>
        failures.Count > 0 ? failures[0].Message : null;

    private static string Count(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private sealed record LoadedUndo(CoordinatedUndoEvaluation Evaluation, ParsedPayload? Payload);

    private abstract record ParsedPayload;

    private sealed record RelocationPayload(RelocationOperationPayload Value) : ParsedPayload;

    private sealed record TossPayload(TossOperationPayload Value) : ParsedPayload;

    private sealed record ArchivePayload(ArchiveUndoPayload Value) : ParsedPayload;
}
