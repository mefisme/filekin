using Filekin.Core.Commands.App;

namespace Filekin.Core.Operations;

/// <summary>
/// Durable detail for one <c>/move</c> or <c>/rename</c> invocation. Successful relocations remain
/// in execution order and command failures stay attached to the same user-level operation.
/// </summary>
public sealed record RelocationOperationPayload
{
    public RelocationOperationPayload(
        IReadOnlyList<PathRelocation> relocations,
        IReadOnlyList<AppCommandFailure> failures,
        IReadOnlyList<PathRelocation>? pendingRelocations = null)
    {
        ArgumentNullException.ThrowIfNull(relocations);
        ArgumentNullException.ThrowIfNull(failures);
        if (relocations.Count == 0)
        {
            throw new ArgumentException(
                "A relocation history payload requires a completed relocation.",
                nameof(relocations));
        }

        foreach (var relocation in relocations)
        {
            ArgumentNullException.ThrowIfNull(relocation);
            ArgumentException.ThrowIfNullOrWhiteSpace(relocation.SourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(relocation.DestinationPath);
        }

        var pending = pendingRelocations ?? relocations;
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Any(item => !relocations.Contains(item)))
        {
            throw new ArgumentException(
                "Pending relocations must belong to the original operation.",
                nameof(pendingRelocations));
        }

        Relocations = [.. relocations];
        Failures = [.. failures];
        PendingRelocations = [.. pending];
    }

    public IReadOnlyList<PathRelocation> Relocations { get; }

    public IReadOnlyList<AppCommandFailure> Failures { get; }

    /// <summary>
    /// Relocations still eligible for a retry after a partial Undo. Initially this is the complete
    /// relocation list; the original list remains unchanged for durable history detail.
    /// </summary>
    public IReadOnlyList<PathRelocation> PendingRelocations { get; }

    public RelocationOperationPayload WithPendingRelocations(
        IReadOnlyList<PathRelocation> pendingRelocations) =>
        new(Relocations, Failures, pendingRelocations);
}
