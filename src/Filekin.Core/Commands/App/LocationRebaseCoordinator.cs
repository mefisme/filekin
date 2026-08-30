using Filekin.Core.Commands.References;
using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App;

/// <summary>
/// Closes the transaction gap between a completed filesystem move and the separate durable settings
/// write that makes saved Locations follow it. A failed settings write is compensated by moving the
/// filesystem items back in reverse order.
/// </summary>
public sealed class LocationRebaseCoordinator
{
    private readonly IFileSystemOperations _operations;
    private readonly IUserLocationPathRebaser _locations;

    public LocationRebaseCoordinator(
        IFileSystemOperations operations,
        IUserLocationPathRebaser locations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(locations);
        _operations = operations;
        _locations = locations;
    }

    public async Task<LocationRebaseCoordinationResult> RebaseOrRollbackAsync(
        IReadOnlyList<PathRelocation> relocations)
    {
        ArgumentNullException.ThrowIfNull(relocations);
        if (relocations.Count == 0)
        {
            return LocationRebaseCoordinationResult.Success(0, []);
        }

        // Once the filesystem move has succeeded, consistency work must not be abandoned merely
        // because the user cancels the original command.
        var rebase = await _locations.RebaseAsync(relocations, CancellationToken.None).ConfigureAwait(false);
        if (rebase.Succeeded)
        {
            return LocationRebaseCoordinationResult.Success(rebase.UpdatedCount, relocations);
        }

        // Reverse order so a later move that landed inside an earlier destination is undone first.
        var returned = 0;
        var remainingRelocations = relocations.ToList();
        try
        {
            for (var index = relocations.Count - 1; index >= 0; index--)
            {
                var relocation = relocations[index];
                if (_operations.GetKind(relocation.DestinationPath) == FileSystemEntryKind.None)
                {
                    throw new IOException($"Moved item is no longer at {relocation.DestinationPath}.");
                }

                if (_operations.GetKind(relocation.SourcePath) != FileSystemEntryKind.None)
                {
                    throw new IOException($"Original path is no longer free: {relocation.SourcePath}.");
                }

                _operations.Move(relocation.DestinationPath, relocation.SourcePath);
                remainingRelocations.RemoveAt(index);
                returned++;
            }

            return LocationRebaseCoordinationResult.Failure(
                $"Could not update saved Locations, so the filesystem move was rolled back. {rebase.Message}",
                rolledBack: true,
                remainingRelocations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Reinspect destinations rather than assuming that every uncompleted call still has a
            // moved item. A destination can disappear between the command and compensation, and a
            // move API can also throw after changing filesystem state.
            remainingRelocations = relocations
                .Where(relocation =>
                    _operations.GetKind(relocation.DestinationPath) != FileSystemEntryKind.None)
                .ToList();
            var unresolved = relocations.Count - returned - remainingRelocations.Count;
            var scope = $"{returned} of {relocations.Count} items were returned; " +
                        (remainingRelocations.Count == 1
                            ? "1 item remains at its moved destination"
                            : $"{remainingRelocations.Count} items remain at their moved destinations");
            if (unresolved > 0)
            {
                scope += $"; {unresolved} could not be found at either expected destination";
            }

            return LocationRebaseCoordinationResult.Failure(
                $"The filesystem move succeeded, but saved Locations could not be updated and rollback failed ({scope}): {ex.Message}",
                rolledBack: false,
                remainingRelocations);
        }
    }
}

public sealed record LocationRebaseCoordinationResult
{
    private LocationRebaseCoordinationResult(
        bool succeeded,
        bool rolledBack,
        int updatedCount,
        string message,
        IReadOnlyList<PathRelocation> remainingRelocations)
    {
        ArgumentNullException.ThrowIfNull(remainingRelocations);
        Succeeded = succeeded;
        RolledBack = rolledBack;
        UpdatedCount = updatedCount;
        Message = message;
        RemainingRelocations = [.. remainingRelocations];
    }

    public bool Succeeded { get; }

    public bool RolledBack { get; }

    public int UpdatedCount { get; }

    public string Message { get; }

    /// <summary>
    /// The exact successful command relocations still present after saved-Location consistency work.
    /// A complete compensation leaves this empty; a failed or partial compensation retains only the
    /// filesystem mutations that remain.
    /// </summary>
    public IReadOnlyList<PathRelocation> RemainingRelocations { get; }

    public static LocationRebaseCoordinationResult Success(
        int updatedCount,
        IReadOnlyList<PathRelocation> remainingRelocations) =>
        new(true, false, updatedCount, string.Empty, remainingRelocations);

    public static LocationRebaseCoordinationResult Failure(
        string message,
        bool rolledBack,
        IReadOnlyList<PathRelocation> remainingRelocations) =>
        new(false, rolledBack, 0, message, remainingRelocations);
}
