using Filekin.Core.FileSystem;

namespace Filekin.Core.Archives;

public enum ArchiveUndoOperationKind
{
    Extraction,
    Compression,
}

/// <summary>
/// One archive's original Undo evidence plus the exact work still pending after any prior attempt.
/// Archives remain in invocation execution order; the executor deliberately walks them in reverse.
/// </summary>
public sealed record ArchiveUndoArchiveWork
{
    public ArchiveUndoArchiveWork(
        int archiveIndex,
        string archivePath,
        IReadOnlyList<ArchiveOutputEvidence> outputs,
        IReadOnlyList<string> createdDirectories,
        IReadOnlyList<ArchiveReplacementEvidence> replacements,
        IReadOnlyList<ArchiveOutputEvidence>? pendingOutputs = null,
        IReadOnlyList<string>? pendingDirectories = null,
        IReadOnlyList<ArchiveReplacementEvidence>? pendingReplacements = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archiveIndex);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(createdDirectories);
        ArgumentNullException.ThrowIfNull(replacements);
        if (outputs.Any(static output => output is null) ||
            outputs.GroupBy(static output => output.Path, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Archive outputs must have unique paths.", nameof(outputs));
        }

        if (createdDirectories.Any(string.IsNullOrWhiteSpace) ||
            createdDirectories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != createdDirectories.Count)
        {
            throw new ArgumentException(
                "Created archive directories must have unique nonempty paths.",
                nameof(createdDirectories));
        }

        if (replacements.Any(static replacement => replacement is null) ||
            replacements.GroupBy(static replacement => replacement.OriginalPath, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Archive replacements must have unique paths.", nameof(replacements));
        }

        var remainingOutputs = outputs.ToList();
        var pendingOutputList = pendingOutputs ?? outputs;
        if (pendingOutputList.Any(output => !remainingOutputs.Remove(output)))
        {
            throw new ArgumentException("Pending outputs must belong to the original archive work.", nameof(pendingOutputs));
        }

        var remainingDirectories = createdDirectories.ToList();
        var pendingDirectoryList = pendingDirectories ?? createdDirectories;
        if (pendingDirectoryList.Any(directory => !RemovePath(remainingDirectories, directory)))
        {
            throw new ArgumentException(
                "Pending directories must belong to the original archive work.",
                nameof(pendingDirectories));
        }

        var remainingReplacements = replacements.ToList();
        var pendingReplacementList = pendingReplacements ?? replacements;
        if (pendingReplacementList.Any(replacement => !remainingReplacements.Remove(replacement)))
        {
            throw new ArgumentException(
                "Pending replacements must belong to the original archive work.",
                nameof(pendingReplacements));
        }

        ArchiveIndex = archiveIndex;
        ArchivePath = archivePath;
        Outputs = [.. outputs];
        CreatedDirectories = [.. createdDirectories];
        Replacements = [.. replacements];
        PendingOutputs = [.. pendingOutputList];
        PendingDirectories = [.. pendingDirectoryList];
        PendingReplacements = [.. pendingReplacementList];
    }

    public ArchiveUndoArchiveWork()
        : this(0, string.Empty, [], [], [])
    {
    }

    public int ArchiveIndex { get; init; }

    public string ArchivePath { get; init; }

    public IReadOnlyList<ArchiveOutputEvidence> Outputs { get; init; }

    public IReadOnlyList<string> CreatedDirectories { get; init; }

    public IReadOnlyList<ArchiveReplacementEvidence> Replacements { get; init; }

    public IReadOnlyList<ArchiveOutputEvidence> PendingOutputs { get; init; }

    public IReadOnlyList<string> PendingDirectories { get; init; }

    public IReadOnlyList<ArchiveReplacementEvidence> PendingReplacements { get; init; }

    public bool HasPendingWork =>
        PendingOutputs.Count > 0 || PendingDirectories.Count > 0 || PendingReplacements.Count > 0;

    public ArchiveUndoArchiveWork WithPending(
        IReadOnlyList<ArchiveOutputEvidence> outputs,
        IReadOnlyList<string> directories,
        IReadOnlyList<ArchiveReplacementEvidence> replacements) =>
        new(
            ArchiveIndex,
            ArchivePath,
            Outputs,
            CreatedDirectories,
            Replacements,
            outputs,
            directories,
            replacements);

    private static bool RemovePath(List<string> paths, string wanted)
    {
        var match = paths.FirstOrDefault(path => string.Equals(path, wanted, StringComparison.OrdinalIgnoreCase));
        return match is not null && paths.Remove(match);
    }
}

/// <summary>A JSON-round-trippable one-invocation archive Undo payload.</summary>
public sealed record ArchiveUndoPayload
{
    public ArchiveUndoPayload(
        ArchiveUndoOperationKind operationKind,
        IReadOnlyList<ArchiveUndoArchiveWork> archives)
    {
        ArgumentNullException.ThrowIfNull(archives);
        if (archives.Any(static archive => archive is null) ||
            archives.Select(static archive => archive.ArchiveIndex).Distinct().Count() != archives.Count)
        {
            throw new ArgumentException("Archive work requires unique archive indexes.", nameof(archives));
        }

        OperationKind = operationKind;
        Archives = [.. archives];
    }

    public ArchiveUndoPayload()
        : this(ArchiveUndoOperationKind.Extraction, [])
    {
    }

    public ArchiveUndoOperationKind OperationKind { get; init; }

    public IReadOnlyList<ArchiveUndoArchiveWork> Archives { get; init; }

    public bool HasPendingWork => Archives.Any(static archive => archive.HasPendingWork);

    public ArchiveUndoPayload WithArchives(IReadOnlyList<ArchiveUndoArchiveWork> archives) =>
        new(OperationKind, archives);

    public static ArchiveUndoPayload FromExtraction(ExtractionBatchOutcome batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new ArchiveUndoPayload(
            ArchiveUndoOperationKind.Extraction,
            batch.Outcomes.Select((outcome, index) => new ArchiveUndoArchiveWork(
                index,
                outcome.ArchivePath,
                OutputEvidence(outcome.CreatedFiles, outcome.CreatedFileEvidence),
                outcome.CreatedDirectories,
                ReplacementEvidence(outcome.ReplacedOriginals, outcome.ReplacementEvidence)))
            .ToArray());
    }

    public static ArchiveUndoPayload FromCompression(CompressionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        IReadOnlyList<ArchiveOutputEvidence> outputs = outcome.OutputEvidence is not null
            ? [outcome.OutputEvidence]
            : outcome.ArchiveBytes > 0
                ? [ArchiveOutputEvidence.Unavailable(
                    outcome.OutputPath,
                    "Completion evidence is unavailable for this archive output.")]
                : [];
        IReadOnlyList<ArchiveReplacementEvidence> replacements = outcome.ReplacedOriginal is null
            ? []
            : outcome.ReplacementEvidence is not null
                ? [outcome.ReplacementEvidence]
                : [new ArchiveReplacementEvidence(
                    outcome.ReplacedOriginal,
                    null,
                    "Exact replacement evidence is unavailable.")];
        return new ArchiveUndoPayload(
            ArchiveUndoOperationKind.Compression,
            [new ArchiveUndoArchiveWork(0, outcome.OutputPath, outputs, [], replacements)]);
    }

    private static ArchiveOutputEvidence[] OutputEvidence(
        IReadOnlyList<string> paths,
        IReadOnlyList<ArchiveOutputEvidence> evidence) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => SingleOutputEvidence(path, evidence))
            .ToArray();

    private static ArchiveOutputEvidence SingleOutputEvidence(
        string path,
        IReadOnlyList<ArchiveOutputEvidence> evidence)
    {
        var matches = evidence
            .Where(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : ArchiveOutputEvidence.Unavailable(
                path,
                matches.Length == 0
                    ? "Completion evidence is unavailable for this archive output."
                    : "Completion evidence is ambiguous for this archive output.");
    }

    private static ArchiveReplacementEvidence[] ReplacementEvidence(
        IReadOnlyList<string> paths,
        IReadOnlyList<ArchiveReplacementEvidence> evidence) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => SingleReplacementEvidence(path, evidence))
            .ToArray();

    private static ArchiveReplacementEvidence SingleReplacementEvidence(
        string path,
        IReadOnlyList<ArchiveReplacementEvidence> evidence)
    {
        var matches = evidence
            .Where(candidate => string.Equals(candidate.OriginalPath, path, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : new ArchiveReplacementEvidence(
                path,
                null,
                matches.Length == 0
                    ? "Exact replacement evidence is unavailable."
                    : "Replacement evidence is ambiguous.");
    }
}

/// <summary>A choice bound to the exact edited fingerprint the user reviewed.</summary>
public sealed record ArchiveEditedOutputResolution
{
    public ArchiveEditedOutputResolution(
        int archiveIndex,
        string path,
        ArchiveOutputEvidence observedEvidence,
        ArchiveEditedOutputDecision decision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archiveIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(observedEvidence);
        if (observedEvidence.ExistedAtCompletion != true ||
            !string.Equals(path, observedEvidence.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("An edited-output decision requires its observed file fingerprint.");
        }

        ArchiveIndex = archiveIndex;
        Path = path;
        ObservedEvidence = observedEvidence;
        Decision = decision;
    }

    public int ArchiveIndex { get; }

    public string Path { get; }

    public ArchiveOutputEvidence ObservedEvidence { get; }

    public ArchiveEditedOutputDecision Decision { get; }

    public static ArchiveEditedOutputResolution FromConflict(
        ArchiveEditedOutputConflict conflict,
        ArchiveEditedOutputDecision decision)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return new ArchiveEditedOutputResolution(
            conflict.ArchiveIndex,
            conflict.Output.CompletionEvidence.Path,
            conflict.Output.CurrentEvidence ?? throw new ArgumentException(
                "The edited-output conflict has no current fingerprint.",
                nameof(conflict)),
            decision);
    }
}

public enum ArchiveDirectoryRemoval
{
    Removed,
    Missing,
    NotEmpty,
}

/// <summary>Ordinary file/directory removals used by the platform-neutral executor.</summary>
public interface IArchiveUndoStorage
{
    void DeleteFile(string path);

    ArchiveDirectoryRemoval RemoveDirectoryIfEmpty(string path);
}

public enum ArchiveUndoStep
{
    DeleteUnchangedOutput,
    RecycleEditedOutput,
    RestoreOriginal,
    RemoveDirectory,
}

public enum ArchiveUndoAttemptOutcome
{
    Succeeded,
    PartiallyUndone,
    Failed,
    Blocked,
    Cancelled,
}

public sealed record ArchiveUndoFailure(
    int ArchiveIndex,
    string Path,
    ArchiveUndoStep Step,
    string Message,
    bool MayHaveChangedFileSystem);

public sealed record ArchiveUndoResult
{
    public ArchiveUndoResult(
        ArchiveUndoAttemptOutcome outcome,
        ArchiveUndoPayload updatedPayload,
        IReadOnlyList<ArchiveOutputEvidence> removedOutputs,
        IReadOnlyList<ArchiveOutputEvidence> missingOutputs,
        IReadOnlyList<ArchiveOutputEvidence> keptEditedOutputs,
        IReadOnlyList<RecycleOutcome> recycledEditedOutputs,
        IReadOnlyList<ArchiveReplacementEvidence> restoredOriginals,
        IReadOnlyList<ArchiveReplacementEvidence> keptRecycledOriginals,
        IReadOnlyList<string> removedDirectories,
        IReadOnlyList<string> keptDirectories,
        IReadOnlyList<ArchiveUndoFailure> failures,
        ArchiveUndoAssessment? blockedBy)
    {
        Outcome = outcome;
        UpdatedPayload = updatedPayload;
        RemovedOutputs = [.. removedOutputs];
        MissingOutputs = [.. missingOutputs];
        KeptEditedOutputs = [.. keptEditedOutputs];
        RecycledEditedOutputs = [.. recycledEditedOutputs];
        RestoredOriginals = [.. restoredOriginals];
        KeptRecycledOriginals = [.. keptRecycledOriginals];
        RemovedDirectories = [.. removedDirectories];
        KeptDirectories = [.. keptDirectories];
        Failures = [.. failures];
        BlockedBy = blockedBy;
    }

    public ArchiveUndoAttemptOutcome Outcome { get; }

    public ArchiveUndoPayload UpdatedPayload { get; }

    public IReadOnlyList<ArchiveOutputEvidence> RemovedOutputs { get; }

    public IReadOnlyList<ArchiveOutputEvidence> MissingOutputs { get; }

    public IReadOnlyList<ArchiveOutputEvidence> KeptEditedOutputs { get; }

    public IReadOnlyList<RecycleOutcome> RecycledEditedOutputs { get; }

    public IReadOnlyList<ArchiveReplacementEvidence> RestoredOriginals { get; }

    public IReadOnlyList<ArchiveReplacementEvidence> KeptRecycledOriginals { get; }

    public IReadOnlyList<string> RemovedDirectories { get; }

    public IReadOnlyList<string> KeptDirectories { get; }

    public IReadOnlyList<ArchiveUndoFailure> Failures { get; }

    public ArchiveUndoAssessment? BlockedBy { get; }

    public bool MayHaveChangedFileSystem =>
        RemovedOutputs.Count > 0 || RecycledEditedOutputs.Count > 0 ||
        RestoredOriginals.Count > 0 || RemovedDirectories.Count > 0 ||
        Failures.Any(static failure => failure.MayHaveChangedFileSystem);
}

/// <summary>
/// Safely reverses archive work one archive at a time in reverse execution order. It never applies
/// the Keep Edited default implicitly and stops before earlier dependent archives when work remains.
/// </summary>
public sealed class ArchiveUndoExecutor
{
    private readonly IFileSystemOperations _fileSystem;
    private readonly IRecycleBin _recycleBin;
    private readonly IArchiveUndoStorage _storage;
    private readonly ArchiveUndoEvaluator _evaluator;

    public ArchiveUndoExecutor(
        IFileSystemOperations fileSystem,
        IRecycleBin recycleBin,
        IArchiveOutputEvidenceReader evidenceReader,
        IArchiveUndoStorage storage)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(recycleBin);
        ArgumentNullException.ThrowIfNull(evidenceReader);
        ArgumentNullException.ThrowIfNull(storage);
        _fileSystem = fileSystem;
        _recycleBin = recycleBin;
        _storage = storage;
        _evaluator = new ArchiveUndoEvaluator(fileSystem, recycleBin, evidenceReader);
    }

    public ArchiveUndoResult Execute(
        ArchiveUndoPayload payload,
        IReadOnlyList<ArchiveEditedOutputResolution>? resolutions = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var decisions = BuildDecisionLookup(resolutions ?? []);
        if (payload.Archives.Any(archive => archive.PendingOutputs.Any(output =>
            decisions.TryGetValue(KeyFor(archive.ArchiveIndex, output.Path), out var resolution) &&
            resolution.Decision == ArchiveEditedOutputDecision.Cancel)))
        {
            return EmptyResult(ArchiveUndoAttemptOutcome.Cancelled, payload);
        }

        var state = new ExecutionState(payload);
        for (var archivePosition = state.Archives.Count - 1; archivePosition >= 0; archivePosition--)
        {
            var archive = state.Archives[archivePosition];
            if (!archive.HasPendingWork)
            {
                continue;
            }

            var preflight = _evaluator.Evaluate(state.PayloadFor(archivePosition));
            if (preflight.Safety == ArchiveUndoSafety.Unavailable ||
                preflight.EditedOutputs.Any(conflict => !decisions.ContainsKey(KeyFor(
                    conflict.ArchiveIndex,
                    conflict.Output.CompletionEvidence.Path))))
            {
                state.BlockedBy = preflight;
                break;
            }

            ExecuteOutputs(state, archivePosition, decisions);
            if (state.BlockedBy is not null)
            {
                break;
            }

            ExecuteReplacements(state, archivePosition);
            if (state.BlockedBy is not null || archive.PendingOutputs.Count > 0 || archive.PendingReplacements.Count > 0)
            {
                break;
            }

            ExecuteDirectories(state, archivePosition);
            if (archive.HasPendingWork)
            {
                break;
            }
        }

        return state.Result();
    }

    private void ExecuteOutputs(
        ExecutionState state,
        int archivePosition,
        Dictionary<string, ArchiveEditedOutputResolution> decisions)
    {
        var archive = state.Archives[archivePosition];
        foreach (var output in archive.PendingOutputs.ToArray())
        {
            var assessment = _evaluator.Evaluate(state.PayloadForOutput(archivePosition, output));
            if (assessment.Safety == ArchiveUndoSafety.Unavailable)
            {
                state.BlockedBy = assessment;
                return;
            }

            var current = assessment.Archives.Single().Outputs.Single();
            if (current.State == ArchiveOutputState.Missing)
            {
                archive.PendingOutputs.Remove(output);
                state.MissingOutputs.Add(output);
                state.CompletedWork++;
                continue;
            }

            if (current.State == ArchiveOutputState.Edited)
            {
                if (!decisions.TryGetValue(KeyFor(archive.ArchiveIndex, output.Path), out var resolution))
                {
                    state.BlockedBy = assessment;
                    return;
                }

                if (resolution.Decision == ArchiveEditedOutputDecision.KeepEdited)
                {
                    archive.PendingOutputs.Remove(output);
                    state.KeptEditedOutputs.Add(output);
                    state.KeptOutputKeys.Add(KeyFor(archive.ArchiveIndex, output.Path));
                    state.CompletedWork++;
                    continue;
                }

                if (resolution.Decision == ArchiveEditedOutputDecision.Cancel ||
                    !SameFingerprint(current.CurrentEvidence, resolution.ObservedEvidence))
                {
                    state.BlockedBy = assessment;
                    return;
                }

                try
                {
                    var recycleOutcome = _fileSystem.Recycle(output.Path);
                    archive.PendingOutputs.Remove(output);
                    state.RecycledEditedOutputs.Add(recycleOutcome);
                    state.CompletedWork++;
                }
                catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
                {
                    state.Failures.Add(new ArchiveUndoFailure(
                        archive.ArchiveIndex,
                        output.Path,
                        ArchiveUndoStep.RecycleEditedOutput,
                        ex.Message,
                        MayHaveWritten(ex)));
                }

                continue;
            }

            try
            {
                _storage.DeleteFile(output.Path);
                archive.PendingOutputs.Remove(output);
                state.RemovedOutputs.Add(output);
                state.CompletedWork++;
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                state.Failures.Add(new ArchiveUndoFailure(
                    archive.ArchiveIndex,
                    output.Path,
                    ArchiveUndoStep.DeleteUnchangedOutput,
                    ex.Message,
                    MayHaveWritten(ex)));
            }
        }
    }

    private void ExecuteReplacements(ExecutionState state, int archivePosition)
    {
        var archive = state.Archives[archivePosition];
        foreach (var replacement in archive.PendingReplacements.ToArray())
        {
            if (state.KeptOutputKeys.Contains(KeyFor(archive.ArchiveIndex, replacement.OriginalPath)))
            {
                archive.PendingReplacements.Remove(replacement);
                state.KeptRecycledOriginals.Add(replacement);
                state.CompletedWork++;
                continue;
            }

            if (archive.PendingOutputs.Any(output =>
                string.Equals(output.Path, replacement.OriginalPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var assessment = _evaluator.Evaluate(state.PayloadForReplacement(archivePosition, replacement));
            if (!assessment.IsReady)
            {
                state.BlockedBy = assessment;
                return;
            }

            var current = assessment.Archives.Single().Replacements.Single().CurrentRecycledItem!;
            try
            {
                if (_recycleBin.Restore(current))
                {
                    archive.PendingReplacements.Remove(replacement);
                    state.RestoredOriginals.Add(replacement);
                    state.CompletedWork++;
                }
                else
                {
                    state.Failures.Add(new ArchiveUndoFailure(
                        archive.ArchiveIndex,
                        replacement.OriginalPath,
                        ArchiveUndoStep.RestoreOriginal,
                        "The exact recycled original could not be restored.",
                        MayHaveChangedFileSystem: false));
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                state.Failures.Add(new ArchiveUndoFailure(
                    archive.ArchiveIndex,
                    replacement.OriginalPath,
                    ArchiveUndoStep.RestoreOriginal,
                    ex.Message,
                    MayHaveWritten(ex)));
            }
        }
    }

    private void ExecuteDirectories(ExecutionState state, int archivePosition)
    {
        var archive = state.Archives[archivePosition];
        for (var index = archive.PendingDirectories.Count - 1; index >= 0; index--)
        {
            var directory = archive.PendingDirectories[index];
            try
            {
                var removal = _storage.RemoveDirectoryIfEmpty(directory);
                archive.PendingDirectories.RemoveAt(index);
                if (removal == ArchiveDirectoryRemoval.Removed)
                {
                    state.RemovedDirectories.Add(directory);
                }
                else if (removal == ArchiveDirectoryRemoval.NotEmpty)
                {
                    state.KeptDirectories.Add(directory);
                }

                state.CompletedWork++;
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                state.Failures.Add(new ArchiveUndoFailure(
                    archive.ArchiveIndex,
                    directory,
                    ArchiveUndoStep.RemoveDirectory,
                    ex.Message,
                    MayHaveWritten(ex)));
                break;
            }
        }
    }

    private static Dictionary<string, ArchiveEditedOutputResolution> BuildDecisionLookup(
        IReadOnlyList<ArchiveEditedOutputResolution> resolutions)
    {
        var decisions = new Dictionary<string, ArchiveEditedOutputResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolution in resolutions)
        {
            if (!decisions.TryAdd(KeyFor(resolution.ArchiveIndex, resolution.Path), resolution))
            {
                throw new ArgumentException(
                    $"More than one edited-output decision was supplied for {resolution.Path}.",
                    nameof(resolutions));
            }
        }

        return decisions;
    }

    private static string KeyFor(int archiveIndex, string path) => $"{archiveIndex}\0{path}";

    private static bool SameFingerprint(ArchiveOutputEvidence? current, ArchiveOutputEvidence observed) =>
        current?.ExistedAtCompletion == true &&
        current.Length == observed.Length &&
        current.LastWriteTimeUtc == observed.LastWriteTimeUtc &&
        string.Equals(current.Sha256, observed.Sha256, StringComparison.OrdinalIgnoreCase);

    private static ArchiveUndoResult EmptyResult(ArchiveUndoAttemptOutcome outcome, ArchiveUndoPayload payload) =>
        new(outcome, payload, [], [], [], [], [], [], [], [], [], null);

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException or InvalidOperationException;

    private static bool MayHaveWritten(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private sealed class ExecutionState
    {
        private readonly ArchiveUndoPayload _original;

        public ExecutionState(ArchiveUndoPayload payload)
        {
            _original = payload;
            Archives = payload.Archives.Select(archive => new MutableArchiveWork(archive)).ToList();
        }

        public List<MutableArchiveWork> Archives { get; }

        public List<ArchiveOutputEvidence> RemovedOutputs { get; } = [];

        public List<ArchiveOutputEvidence> MissingOutputs { get; } = [];

        public List<ArchiveOutputEvidence> KeptEditedOutputs { get; } = [];

        public HashSet<string> KeptOutputKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RecycleOutcome> RecycledEditedOutputs { get; } = [];

        public List<ArchiveReplacementEvidence> RestoredOriginals { get; } = [];

        public List<ArchiveReplacementEvidence> KeptRecycledOriginals { get; } = [];

        public List<string> RemovedDirectories { get; } = [];

        public List<string> KeptDirectories { get; } = [];

        public List<ArchiveUndoFailure> Failures { get; } = [];

        public ArchiveUndoAssessment? BlockedBy { get; set; }

        public int CompletedWork { get; set; }

        public ArchiveUndoPayload PayloadFor(int archivePosition) =>
            new(_original.OperationKind, [Archives[archivePosition].Snapshot()]);

        public ArchiveUndoPayload PayloadForOutput(int archivePosition, ArchiveOutputEvidence output) =>
            new(_original.OperationKind, [Archives[archivePosition].Snapshot([output], [], [])]);

        public ArchiveUndoPayload PayloadForReplacement(
            int archivePosition,
            ArchiveReplacementEvidence replacement) =>
            new(_original.OperationKind, [Archives[archivePosition].Snapshot([], [], [replacement])]);

        public ArchiveUndoResult Result()
        {
            var updated = _original.WithArchives(Archives.Select(static archive => archive.Snapshot()).ToArray());
            var intentionallyPartial = KeptEditedOutputs.Count > 0 ||
                KeptRecycledOriginals.Count > 0 || KeptDirectories.Count > 0 ||
                RecycledEditedOutputs.Any(static outcome => !outcome.CanRestore);
            var outcome = !updated.HasPendingWork
                ? intentionallyPartial
                    ? ArchiveUndoAttemptOutcome.PartiallyUndone
                    : ArchiveUndoAttemptOutcome.Succeeded
                : CompletedWork > 0
                    ? ArchiveUndoAttemptOutcome.PartiallyUndone
                    : BlockedBy is not null
                        ? ArchiveUndoAttemptOutcome.Blocked
                        : ArchiveUndoAttemptOutcome.Failed;
            return new ArchiveUndoResult(
                outcome,
                updated,
                RemovedOutputs,
                MissingOutputs,
                KeptEditedOutputs,
                RecycledEditedOutputs,
                RestoredOriginals,
                KeptRecycledOriginals,
                RemovedDirectories,
                KeptDirectories,
                Failures,
                BlockedBy);
        }
    }

    private sealed class MutableArchiveWork
    {
        private readonly ArchiveUndoArchiveWork _original;

        public MutableArchiveWork(ArchiveUndoArchiveWork archive)
        {
            _original = archive;
            PendingOutputs = [.. archive.PendingOutputs];
            PendingDirectories = [.. archive.PendingDirectories];
            PendingReplacements = [.. archive.PendingReplacements];
        }

        public int ArchiveIndex => _original.ArchiveIndex;

        public List<ArchiveOutputEvidence> PendingOutputs { get; }

        public List<string> PendingDirectories { get; }

        public List<ArchiveReplacementEvidence> PendingReplacements { get; }

        public bool HasPendingWork =>
            PendingOutputs.Count > 0 || PendingDirectories.Count > 0 || PendingReplacements.Count > 0;

        public ArchiveUndoArchiveWork Snapshot() =>
            _original.WithPending(PendingOutputs, PendingDirectories, PendingReplacements);

        public ArchiveUndoArchiveWork Snapshot(
            IReadOnlyList<ArchiveOutputEvidence> outputs,
            IReadOnlyList<string> directories,
            IReadOnlyList<ArchiveReplacementEvidence> replacements) =>
            _original.WithPending(outputs, directories, replacements);
    }
}
