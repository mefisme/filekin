using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App;

public enum AppCommandOutcome
{
    Success,
    PartialSuccess,
    Error,
}

/// <summary>
/// The outcome of running one application command. It feeds the compact command-bar result
/// indicator (FEATURES.md — "Compact Command Result Indicator") and, for successful app-owned
/// mutations, provides authoritative detail to the separate operation-history/undo journal.
/// </summary>
public sealed record AppCommandResult
{
    private AppCommandResult(
        AppCommandOutcome outcome,
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations,
        IReadOnlyList<AppCommandFailure> failures,
        bool touchedFileSystem)
    {
        Outcome = outcome;
        Message = message;
        AffectedPaths = affectedPaths;
        Relocations = relocations;
        Failures = failures;
        TouchedFileSystem = touchedFileSystem;
    }

    public AppCommandOutcome Outcome { get; }

    public string Message { get; }

    public IReadOnlyList<string> AffectedPaths { get; }

    /// <summary>Successful source/destination moves that saved Locations and history can follow.</summary>
    public IReadOnlyList<PathRelocation> Relocations { get; }

    /// <summary>Independent batch targets that failed while other targets were allowed to continue.</summary>
    public IReadOnlyList<AppCommandFailure> Failures { get; }

    /// <summary>
    /// Whether the command may have changed the filesystem, including a batch that failed part way
    /// through. <see cref="AffectedPaths"/> alone is not enough: a batch that moved two of five items
    /// and then threw reports no paths, yet the folder on screen is already stale.
    /// </summary>
    public bool TouchedFileSystem { get; }

    public bool Succeeded => Outcome == AppCommandOutcome.Success;

    public static AppCommandResult Ok(string message, params string[] affectedPaths)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        return new AppCommandResult(
            AppCommandOutcome.Success,
            message,
            affectedPaths,
            [],
            [],
            affectedPaths.Length > 0);
    }

    public static AppCommandResult Ok(
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        ArgumentNullException.ThrowIfNull(relocations);
        return new AppCommandResult(
            AppCommandOutcome.Success,
            message,
            affectedPaths,
            relocations,
            [],
            affectedPaths.Count > 0);
    }

    /// <summary>
    /// Some independent targets completed and some failed. Completed paths and relocations remain
    /// authoritative so refresh, history, and saved-Location rebasing can act on the work that did
    /// happen.
    /// </summary>
    public static AppCommandResult Partial(
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations,
        IReadOnlyList<AppCommandFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        ArgumentNullException.ThrowIfNull(relocations);
        ArgumentNullException.ThrowIfNull(failures);
        if (affectedPaths.Count == 0)
        {
            throw new ArgumentException("A partial result requires at least one completed target.", nameof(affectedPaths));
        }

        if (failures.Count == 0)
        {
            throw new ArgumentException("A partial result requires at least one failed target.", nameof(failures));
        }

        return new AppCommandResult(
            AppCommandOutcome.PartialSuccess,
            message,
            affectedPaths,
            relocations,
            failures,
            touchedFileSystem: true);
    }

    /// <summary>An ordinary refusal: bad arguments, a missing target. Nothing was written.</summary>
    public static AppCommandResult Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AppCommandResult(AppCommandOutcome.Error, message, [], [], [], touchedFileSystem: false);
    }

    /// <summary>Every target in a batch failed, but the failures remain individually inspectable.</summary>
    public static AppCommandResult FailedBatch(
        string message,
        IReadOnlyList<AppCommandFailure> failures,
        bool touchedFileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException("A failed batch requires at least one failed target.", nameof(failures));
        }

        return new AppCommandResult(
            AppCommandOutcome.Error,
            message,
            [],
            [],
            failures,
            touchedFileSystem);
    }

    /// <summary>
    /// A failure that happened after the command had begun writing, so the visible folder must be
    /// re-listed even though the command reports no completed paths.
    /// </summary>
    public static AppCommandResult FailedWhileWriting(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AppCommandResult(AppCommandOutcome.Error, message, [], [], [], touchedFileSystem: true);
    }
}
