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
        IReadOnlyList<PathRelocation> relocations)
    {
        Outcome = outcome;
        Message = message;
        AffectedPaths = affectedPaths;
        Relocations = relocations;
    }

    public AppCommandOutcome Outcome { get; }

    public string Message { get; }

    public IReadOnlyList<string> AffectedPaths { get; }

    /// <summary>Successful source/destination moves that saved Locations and history can follow.</summary>
    public IReadOnlyList<PathRelocation> Relocations { get; }

    public bool Succeeded => Outcome == AppCommandOutcome.Success;

    public static AppCommandResult Ok(string message, params string[] affectedPaths)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        return new AppCommandResult(AppCommandOutcome.Success, message, affectedPaths, []);
    }

    public static AppCommandResult Ok(
        string message,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<PathRelocation> relocations)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(affectedPaths);
        ArgumentNullException.ThrowIfNull(relocations);
        return new AppCommandResult(AppCommandOutcome.Success, message, affectedPaths, relocations);
    }

    public static AppCommandResult Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AppCommandResult(AppCommandOutcome.Error, message, [], []);
    }
}
