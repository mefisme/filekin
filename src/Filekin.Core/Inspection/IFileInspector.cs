namespace Filekin.Core.Inspection;

/// <summary>
/// Reads the immediately available metadata for one or more <c>/info</c> targets. Implementations
/// must not walk directory trees or read whole files; that work belongs to
/// <see cref="IAggregateScanner"/> and to the explicit on-demand actions.
/// </summary>
public interface IFileInspector
{
    /// <summary>Describes one file or folder.</summary>
    InspectionResult Inspect(string path);

    /// <summary>
    /// Describes a whole selection as one aggregate rather than a stack of property sheets
    /// (ARCHITECTURE.md — Topic 5R). A single-item selection is described as that item.
    /// </summary>
    InspectionResult InspectSelection(IReadOnlyList<string> paths);
}
