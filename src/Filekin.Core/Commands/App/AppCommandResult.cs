using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App;

public enum AppCommandOutcome
{
    Success,
    Error,
}

/// <summary>
/// The outcome of running one application command. It feeds the compact command-bar result
/// indicator (FEATURES.md — "Compact Command Result Indicator") and, for successful app-owned
/// mutations, provides the affected paths that the operation-history/undo journal will later record
/// (that SQLite-backed journal is a separate subsystem and is not built here).
/// </summary>
public sealed record AppCommandResult
{
    private AppCommandResult(
        AppCommandOutcome outcome,
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations,
        bool touchedFileSystem)
    {
        Outcome = outcome;
        Message = message;
        AffectedPaths = affectedPaths;
        Relocations = relocations;
        TouchedFileSystem = touchedFileSystem;
    }

    public AppCommandOutcome Outcome { get; }

    public string Message { get; }

    public IReadOnlyList<string> AffectedPaths { get; }

    /// <summary>Successful source/destination moves that saved Locations and history can follow.</summary>
    public IReadOnlyList<PathRelocation> Relocations { get; }

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
        return new AppCommandResult(AppCommandOutcome.Success, message, affectedPaths, [], affectedPaths.Length > 0);
    }

    public static AppCommandResult Ok(
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        ArgumentNullException.ThrowIfNull(relocations);
        return new AppCommandResult(AppCommandOutcome.Success, message, affectedPaths, relocations, affectedPaths.Count > 0);
    }

    /// <summary>An ordinary refusal: bad arguments, a missing target. Nothing was written.</summary>
    public static AppCommandResult Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AppCommandResult(AppCommandOutcome.Error, message, [], [], touchedFileSystem: false);
    }

    /// <summary>
    /// A failure that happened after the command had begun writing, so the visible folder must be
    /// re-listed even though the command reports no completed paths.
    /// </summary>
    public static AppCommandResult FailedWhileWriting(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AppCommandResult(AppCommandOutcome.Error, message, [], [], touchedFileSystem: true);
    }
}
