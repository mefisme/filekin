using Filekin.Core.Operations;

namespace Filekin.Core.Commands.References;

/// <summary>
/// Durably retargets saved Locations whose paths equal or sit beneath app-owned filesystem moves.
/// All matching Locations are written as one settings mutation.
/// </summary>
public interface IUserLocationPathRebaser
{
    Task<UserLocationPathRebaseResult> RebaseAsync(
        IReadOnlyList<PathRelocation> relocations,
        CancellationToken cancellationToken = default);
}

public sealed record UserLocationPathRebaseResult(bool Succeeded, int UpdatedCount, string Message)
{
    public static UserLocationPathRebaseResult Ok(int updatedCount) => new(true, updatedCount, string.Empty);

    public static UserLocationPathRebaseResult Fail(string message) => new(false, 0, message);
}
