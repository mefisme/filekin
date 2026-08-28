namespace Filekin.Core.Discovery;

/// <summary>The purpose a discovered filesystem path serves in a program's footprint.</summary>
public enum WhereLocationKind
{
    Executable,
    Installation,
    UserData,
    Configuration,
    Shortcut,
}

/// <summary>The configured Windows PATH lists containing a discovered executable's folder.</summary>
[Flags]
public enum WherePathScope
{
    None = 0,
    Process = 1,
    User = 2,
    Machine = 4,
}

/// <summary>One existing, navigable filesystem location associated with the query.</summary>
public sealed record WhereLocation(
    string Path,
    WhereLocationKind Kind,
    string Sources,
    WherePathScope PathScope = WherePathScope.None);

/// <summary>A progressive snapshot produced while the bounded discovery pass is still running.</summary>
public sealed record WhereDiscoveryProgress(
    string Stage,
    IReadOnlyList<WhereLocation> Locations,
    int UnreadableLocations);

/// <summary>The final stable snapshot after every configured discovery source has completed.</summary>
public sealed record WhereDiscoveryOutcome(
    IReadOnlyList<WhereLocation> Locations,
    int UnreadableLocations);
