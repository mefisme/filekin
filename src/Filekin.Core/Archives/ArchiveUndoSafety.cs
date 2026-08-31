using Filekin.Core.FileSystem;

namespace Filekin.Core.Archives;

/// <summary>The present state of one path an archive operation attempted to create.</summary>
public enum ArchiveOutputState
{
    Unchanged,
    Edited,
    Missing,
    Unverifiable,
}

/// <summary>What blocks safe archive Undo without changing the filesystem.</summary>
public enum ArchiveUndoIssueKind
{
    OutputEvidenceUnavailable,
    OutputEvidenceAmbiguous,
    OutputInspectionFailed,
    OriginalPathOccupied,
    RestoreIdentityUnavailable,
    RecycledOriginalMissing,
    RecycledOriginalAmbiguous,
    RecycleBinInspectionFailed,
}

/// <summary>The aggregate present-state safety of one archive invocation.</summary>
public enum ArchiveUndoSafety
{
    Ready,
    NeedsEditedOutputDecision,
    Unavailable,
}

/// <summary>The explicit choices for an output edited after Filekin wrote it.</summary>
public enum ArchiveEditedOutputDecision
{
    KeepEdited,
    RecycleEdited,
    Cancel,
}

/// <summary>
/// Reads a current file fingerprint. Implementations must return unavailable evidence rather than a
/// partial fingerprint when the file cannot be read consistently.
/// </summary>
public interface IArchiveOutputEvidenceReader
{
    ArchiveOutputEvidence Read(string path);
}

public sealed record ArchiveUndoIssue(
    int ArchiveIndex,
    string ArchivePath,
    string Path,
    ArchiveUndoIssueKind Kind,
    string Message);

public sealed record ArchiveOutputAssessment(
    ArchiveOutputEvidence CompletionEvidence,
    ArchiveOutputState State,
    string Message,
    ArchiveOutputEvidence? CurrentEvidence = null);

/// <summary>
/// A decision the future conflict UI must obtain before Undo can touch an edited output. Keeping the
/// edited file is deliberately first and remains the safe default.
/// </summary>
public sealed record ArchiveEditedOutputConflict(
    int ArchiveIndex,
    string ArchivePath,
    ArchiveOutputAssessment Output)
{
    private static readonly IReadOnlyList<ArchiveEditedOutputDecision> SupportedChoices =
        Array.AsReadOnly([
            ArchiveEditedOutputDecision.KeepEdited,
            ArchiveEditedOutputDecision.RecycleEdited,
            ArchiveEditedOutputDecision.Cancel,
        ]);

    public static ArchiveEditedOutputDecision DefaultChoice => ArchiveEditedOutputDecision.KeepEdited;

    public static IReadOnlyList<ArchiveEditedOutputDecision> Choices => SupportedChoices;
}

public enum ArchiveReplacementState
{
    Ready,
    RestoreIdentityUnavailable,
    RecycledOriginalMissing,
    RecycledOriginalAmbiguous,
    OriginalPathOccupied,
    InspectionFailed,
}

public sealed record ArchiveReplacementAssessment(
    ArchiveReplacementEvidence CompletionEvidence,
    ArchiveReplacementState State,
    RecycledItem? CurrentRecycledItem,
    string Message);

/// <summary>Present-state results for one archive, retained in original execution order.</summary>
public sealed record ArchiveUndoUnitAssessment
{
    public ArchiveUndoUnitAssessment(
        int archiveIndex,
        string archivePath,
        IReadOnlyList<string> createdDirectories,
        IReadOnlyList<ArchiveOutputAssessment> outputs,
        IReadOnlyList<ArchiveReplacementAssessment> replacements)
    {
        ArchiveIndex = archiveIndex;
        ArchivePath = archivePath;
        CreatedDirectories = [.. createdDirectories];
        Outputs = [.. outputs];
        Replacements = [.. replacements];
    }

    public int ArchiveIndex { get; }

    public string ArchivePath { get; }

    public IReadOnlyList<string> CreatedDirectories { get; }

    public IReadOnlyList<ArchiveOutputAssessment> Outputs { get; }

    public IReadOnlyList<ArchiveReplacementAssessment> Replacements { get; }
}

/// <summary>A side-effect-free snapshot of whether one archive invocation can be undone now.</summary>
public sealed record ArchiveUndoAssessment
{
    public ArchiveUndoAssessment(
        IReadOnlyList<ArchiveUndoUnitAssessment> archives,
        IReadOnlyList<ArchiveEditedOutputConflict> editedOutputs,
        IReadOnlyList<ArchiveUndoIssue> issues)
    {
        Archives = [.. archives];
        EditedOutputs = [.. editedOutputs];
        Issues = [.. issues];
        Safety = Issues.Count > 0
            ? ArchiveUndoSafety.Unavailable
            : EditedOutputs.Count > 0
                ? ArchiveUndoSafety.NeedsEditedOutputDecision
                : ArchiveUndoSafety.Ready;
    }

    public ArchiveUndoSafety Safety { get; }

    public IReadOnlyList<ArchiveUndoUnitAssessment> Archives { get; }

    public IReadOnlyList<ArchiveEditedOutputConflict> EditedOutputs { get; }

    public IReadOnlyList<ArchiveUndoIssue> Issues { get; }

    public bool IsReady => Safety == ArchiveUndoSafety.Ready;
}

/// <summary>
/// Compares completion evidence with the current filesystem and exact Recycle Bin identities. It
/// never changes state or silently chooses what to do with an edited output.
/// </summary>
public sealed class ArchiveUndoEvaluator
{
    private readonly IFileSystemOperations _fileSystem;
    private readonly IRecycleBin _recycleBin;
    private readonly IArchiveOutputEvidenceReader _evidenceReader;

    public ArchiveUndoEvaluator(
        IFileSystemOperations fileSystem,
        IRecycleBin recycleBin,
        IArchiveOutputEvidenceReader evidenceReader)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(recycleBin);
        ArgumentNullException.ThrowIfNull(evidenceReader);
        _fileSystem = fileSystem;
        _recycleBin = recycleBin;
        _evidenceReader = evidenceReader;
    }

    public ArchiveUndoAssessment Evaluate(ExtractionBatchOutcome batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return Evaluate(batch.Outcomes.Select((outcome, index) => new ArchiveUndoUnit(
            index,
            outcome.ArchivePath,
            outcome.CreatedFiles,
            outcome.CreatedDirectories,
            outcome.ReplacedOriginals,
            outcome.CreatedFileEvidence,
            outcome.ReplacementEvidence)).ToArray());
    }

    public ArchiveUndoAssessment Evaluate(CompressionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var outputPaths = outcome.OutputEvidence is not null || outcome.ArchiveBytes > 0
            ? new[] { outcome.OutputPath }
            : [];
        var replacementPaths = outcome.ReplacedOriginal is null
            ? []
            : new[] { outcome.ReplacedOriginal };
        return Evaluate([
            new ArchiveUndoUnit(
                0,
                outcome.OutputPath,
                outputPaths,
                [],
                replacementPaths,
                outcome.OutputEvidence is null ? [] : [outcome.OutputEvidence],
                outcome.ReplacementEvidence is null ? [] : [outcome.ReplacementEvidence]),
        ]);
    }

    public ArchiveUndoAssessment Evaluate(ArchiveUndoPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Evaluate(payload.Archives.Select(archive => new ArchiveUndoUnit(
            archive.ArchiveIndex,
            archive.ArchivePath,
            archive.PendingOutputs.Select(static output => output.Path).ToArray(),
            archive.PendingDirectories,
            archive.PendingReplacements.Select(static replacement => replacement.OriginalPath).ToArray(),
            archive.PendingOutputs,
            archive.PendingReplacements)).ToArray());
    }

    private ArchiveUndoAssessment Evaluate(IReadOnlyList<ArchiveUndoUnit> units)
    {
        var issues = new List<ArchiveUndoIssue>();
        var edited = new List<ArchiveEditedOutputConflict>();
        var archiveAssessments = new List<ArchiveUndoUnitAssessment>(units.Count);
        IReadOnlyList<RecycledItem>? recycledItems = null;
        Exception? recycleInspectionFailure = null;

        if (units.Any(unit => unit.ReplacementPaths.Count > 0))
        {
            try
            {
                recycledItems = _recycleBin.List();
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                recycleInspectionFailure = ex;
            }
        }

        foreach (var unit in units)
        {
            var outputs = EvaluateOutputs(unit, issues, edited);
            var replacements = EvaluateReplacements(
                unit,
                outputs,
                recycledItems,
                recycleInspectionFailure,
                issues);
            archiveAssessments.Add(new ArchiveUndoUnitAssessment(
                unit.Index,
                unit.ArchivePath,
                unit.CreatedDirectories,
                outputs,
                replacements));
        }

        return new ArchiveUndoAssessment(archiveAssessments, edited, issues);
    }

    private List<ArchiveOutputAssessment> EvaluateOutputs(
        ArchiveUndoUnit unit,
        List<ArchiveUndoIssue> issues,
        List<ArchiveEditedOutputConflict> edited)
    {
        var results = new List<ArchiveOutputAssessment>();
        foreach (var path in unit.OutputPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = unit.OutputEvidence
                .Where(evidence => string.Equals(evidence.Path, path, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                var kind = matches.Length == 0
                    ? ArchiveUndoIssueKind.OutputEvidenceUnavailable
                    : ArchiveUndoIssueKind.OutputEvidenceAmbiguous;
                var message = matches.Length == 0
                    ? $"Completion evidence is unavailable for {path}."
                    : $"Completion evidence is ambiguous for {path}.";
                issues.Add(Issue(unit, path, kind, message));
                results.Add(new ArchiveOutputAssessment(
                    ArchiveOutputEvidence.Unavailable(path, message),
                    ArchiveOutputState.Unverifiable,
                    message));
                continue;
            }

            var result = EvaluateOutput(unit, matches[0], issues);
            results.Add(result);
            if (result.State == ArchiveOutputState.Edited)
            {
                edited.Add(new ArchiveEditedOutputConflict(unit.Index, unit.ArchivePath, result));
            }
        }

        return results;
    }

    private ArchiveOutputAssessment EvaluateOutput(
        ArchiveUndoUnit unit,
        ArchiveOutputEvidence completion,
        List<ArchiveUndoIssue> issues)
    {
        if (!completion.CanVerify)
        {
            var message = completion.UnavailableReason ?? $"Completion evidence is unavailable for {completion.Path}.";
            issues.Add(Issue(
                unit,
                completion.Path,
                ArchiveUndoIssueKind.OutputEvidenceUnavailable,
                message));
            return new ArchiveOutputAssessment(completion, ArchiveOutputState.Unverifiable, message);
        }

        FileSystemEntryKind currentKind;
        try
        {
            currentKind = _fileSystem.GetKind(completion.Path);
        }
        catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
        {
            return UnverifiableOutput(unit, completion, ex.Message, issues);
        }

        if (completion.ExistedAtCompletion == false)
        {
            if (currentKind == FileSystemEntryKind.None)
            {
                return new ArchiveOutputAssessment(
                    completion,
                    ArchiveOutputState.Missing,
                    $"No archive output remains at {completion.Path}.",
                    ArchiveOutputEvidence.Absent(completion.Path));
            }

            var message = $"A path Filekin did not finish writing is now occupied: {completion.Path}";
            issues.Add(Issue(unit, completion.Path, ArchiveUndoIssueKind.OriginalPathOccupied, message));
            return new ArchiveOutputAssessment(completion, ArchiveOutputState.Unverifiable, message);
        }

        if (currentKind == FileSystemEntryKind.None)
        {
            return new ArchiveOutputAssessment(
                completion,
                ArchiveOutputState.Missing,
                $"The archive output is no longer present: {completion.Path}",
                ArchiveOutputEvidence.Absent(completion.Path));
        }

        if (currentKind != FileSystemEntryKind.File)
        {
            return UnverifiableOutput(
                unit,
                completion,
                "The output path is no longer a file.",
                issues);
        }

        ArchiveOutputEvidence current;
        try
        {
            current = _evidenceReader.Read(completion.Path);
        }
        catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
        {
            return UnverifiableOutput(unit, completion, ex.Message, issues);
        }

        if (!current.CanVerify)
        {
            return UnverifiableOutput(
                unit,
                completion,
                current.UnavailableReason ?? "The current output fingerprint is unavailable.",
                issues);
        }

        if (current.ExistedAtCompletion == false)
        {
            return new ArchiveOutputAssessment(
                completion,
                ArchiveOutputState.Missing,
                $"The archive output is no longer present: {completion.Path}",
                current);
        }

        var unchanged = current.Length == completion.Length &&
            current.LastWriteTimeUtc == completion.LastWriteTimeUtc &&
            string.Equals(current.Sha256, completion.Sha256, StringComparison.OrdinalIgnoreCase);
        return new ArchiveOutputAssessment(
            completion,
            unchanged ? ArchiveOutputState.Unchanged : ArchiveOutputState.Edited,
            unchanged
                ? $"The archive output is unchanged: {completion.Path}"
                : $"The archive output was edited after Filekin wrote it: {completion.Path}",
            current);
    }

    private List<ArchiveReplacementAssessment> EvaluateReplacements(
        ArchiveUndoUnit unit,
        IReadOnlyList<ArchiveOutputAssessment> outputs,
        IReadOnlyList<RecycledItem>? recycledItems,
        Exception? recycleInspectionFailure,
        List<ArchiveUndoIssue> issues)
    {
        var results = new List<ArchiveReplacementAssessment>();
        foreach (var path in unit.ReplacementPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = unit.ReplacementEvidence
                .Where(evidence => string.Equals(evidence.OriginalPath, path, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (matches.Length != 1 || !matches[0].CanRestore)
            {
                var evidence = matches.Length == 1
                    ? matches[0]
                    : new ArchiveReplacementEvidence(path, null, matches.Length == 0
                        ? "Exact replacement evidence is unavailable."
                        : "Replacement evidence is ambiguous.");
                var message = evidence.RestoreUnavailableReason ?? "The exact Recycle Bin identity is unavailable.";
                issues.Add(Issue(unit, path, ArchiveUndoIssueKind.RestoreIdentityUnavailable, message));
                results.Add(new ArchiveReplacementAssessment(
                    evidence,
                    ArchiveReplacementState.RestoreIdentityUnavailable,
                    null,
                    message));
                continue;
            }

            var completion = matches[0];
            if (recycleInspectionFailure is not null)
            {
                var message = $"Could not inspect the Recycle Bin before Undo: {recycleInspectionFailure.Message}";
                issues.Add(Issue(unit, path, ArchiveUndoIssueKind.RecycleBinInspectionFailed, message));
                results.Add(new ArchiveReplacementAssessment(
                    completion,
                    ArchiveReplacementState.InspectionFailed,
                    null,
                    message));
                continue;
            }

            var identity = completion.RecycledItem!.RecycleBinIdentity!;
            var currentMatches = recycledItems!
                .Where(item => !string.IsNullOrWhiteSpace(item.RecycleBinIdentity) &&
                    string.Equals(item.RecycleBinIdentity, identity, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (currentMatches.Length != 1)
            {
                var missing = currentMatches.Length == 0;
                var kind = missing
                    ? ArchiveUndoIssueKind.RecycledOriginalMissing
                    : ArchiveUndoIssueKind.RecycledOriginalAmbiguous;
                var state = missing
                    ? ArchiveReplacementState.RecycledOriginalMissing
                    : ArchiveReplacementState.RecycledOriginalAmbiguous;
                var message = missing
                    ? $"The exact recycled original is no longer available for {path}."
                    : $"The exact recycled original is ambiguous for {path}.";
                issues.Add(Issue(unit, path, kind, message));
                results.Add(new ArchiveReplacementAssessment(completion, state, null, message));
                continue;
            }

            var matchingOutput = outputs.SingleOrDefault(output => string.Equals(
                output.CompletionEvidence.Path,
                path,
                StringComparison.OrdinalIgnoreCase));
            if (matchingOutput is null)
            {
                try
                {
                    if (_fileSystem.GetKind(path) != FileSystemEntryKind.None)
                    {
                        var message = $"The original path is occupied: {path}";
                        issues.Add(Issue(unit, path, ArchiveUndoIssueKind.OriginalPathOccupied, message));
                        results.Add(new ArchiveReplacementAssessment(
                            completion,
                            ArchiveReplacementState.OriginalPathOccupied,
                            currentMatches[0],
                            message));
                        continue;
                    }
                }
                catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
                {
                    var message = $"Could not inspect the original path before Undo: {ex.Message}";
                    issues.Add(Issue(unit, path, ArchiveUndoIssueKind.OutputInspectionFailed, message));
                    results.Add(new ArchiveReplacementAssessment(
                        completion,
                        ArchiveReplacementState.InspectionFailed,
                        currentMatches[0],
                        message));
                    continue;
                }
            }
            else if (matchingOutput.State == ArchiveOutputState.Unverifiable)
            {
                var message = $"The original path cannot be cleared safely: {path}";
                results.Add(new ArchiveReplacementAssessment(
                    completion,
                    ArchiveReplacementState.OriginalPathOccupied,
                    currentMatches[0],
                    message));
                continue;
            }

            results.Add(new ArchiveReplacementAssessment(
                completion,
                ArchiveReplacementState.Ready,
                currentMatches[0],
                $"The exact recycled original is available for {path}."));
        }

        return results;
    }

    private static ArchiveOutputAssessment UnverifiableOutput(
        ArchiveUndoUnit unit,
        ArchiveOutputEvidence completion,
        string reason,
        List<ArchiveUndoIssue> issues)
    {
        var message = $"Could not verify the archive output at {completion.Path}: {reason}";
        issues.Add(Issue(unit, completion.Path, ArchiveUndoIssueKind.OutputInspectionFailed, message));
        return new ArchiveOutputAssessment(completion, ArchiveOutputState.Unverifiable, message);
    }

    private static ArchiveUndoIssue Issue(
        ArchiveUndoUnit unit,
        string path,
        ArchiveUndoIssueKind kind,
        string message) =>
        new(unit.Index, unit.ArchivePath, path, kind, message);

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;

    private sealed record ArchiveUndoUnit(
        int Index,
        string ArchivePath,
        IReadOnlyList<string> OutputPaths,
        IReadOnlyList<string> CreatedDirectories,
        IReadOnlyList<string> ReplacementPaths,
        IReadOnlyList<ArchiveOutputEvidence> OutputEvidence,
        IReadOnlyList<ArchiveReplacementEvidence> ReplacementEvidence);
}
