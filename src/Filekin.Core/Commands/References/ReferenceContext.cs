namespace Filekin.Core.Commands.References;

/// <summary>
/// The workspace state that intrinsic <c>@</c> references resolve against: the current Files folder
/// (for <c>@thisfolder</c>) and the current filesystem selection (for <c>@selection</c>, which always
/// means every selected item and never collapses to the first — DECISIONS.md, 2026-08-24). Both are
/// supplied by the command bar at resolve time. Other names (Windows known folders, user Locations)
/// are resolved through an <see cref="INamedLocationResolver"/> instead.
/// </summary>
public sealed record ReferenceContext
{
    public ReferenceContext(string? currentFolderPath, IReadOnlyList<string> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        CurrentFolderPath = currentFolderPath;
        Selection = selection;
    }

    /// <summary>The current Files filesystem folder, or <c>null</c> when the location is a non-filesystem provider.</summary>
    public string? CurrentFolderPath { get; }

    /// <summary>The absolute paths of every currently selected filesystem item.</summary>
    public IReadOnlyList<string> Selection { get; }

    public static ReferenceContext ForFolder(string currentFolderPath) => new(currentFolderPath, []);
}
