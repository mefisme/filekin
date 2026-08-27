namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>The filesystem/command resolution and launch kind for one <c>/run</c> target.</summary>
/// <param name="OriginalTarget">The target exactly as the user typed it.</param>
/// <param name="LaunchTarget">What is handed to Windows — a full path when one was found.</param>
/// <param name="DisplayName">The short name used in result messages.</param>
/// <param name="Kind">How Filekin should activate the target.</param>
/// <param name="FoundOnDisk">
/// Whether the target resolved to a real file or folder. A name that resolved nowhere is still
/// attempted, because Windows shell execution knows registrations Filekin does not, but a failure
/// can then be reported as "not found" instead of repeating the Windows process-start wording.
/// </param>
public sealed record RunTargetResolution(
    string OriginalTarget,
    string LaunchTarget,
    string DisplayName,
    RunTargetKind Kind,
    bool FoundOnDisk);
