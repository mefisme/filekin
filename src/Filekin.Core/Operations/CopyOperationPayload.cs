using Filekin.Core.Commands.App;

namespace Filekin.Core.Operations;

/// <summary>
/// Durable, platform-neutral detail for one <c>/copy</c> invocation. Created paths are the exact
/// successful destinations reported by the command; failures remain attached to the same top-level
/// operation so a partial batch is not flattened into several history rows.
/// </summary>
public sealed record CopyOperationPayload
{
    public CopyOperationPayload(
        IReadOnlyList<string> createdPaths,
        IReadOnlyList<AppCommandFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(createdPaths);
        ArgumentNullException.ThrowIfNull(failures);
        if (createdPaths.Count == 0)
        {
            throw new ArgumentException("A copy history payload requires a created path.", nameof(createdPaths));
        }

        CreatedPaths = [.. createdPaths];
        Failures = [.. failures];
    }

    public IReadOnlyList<string> CreatedPaths { get; }

    public IReadOnlyList<AppCommandFailure> Failures { get; }
}
