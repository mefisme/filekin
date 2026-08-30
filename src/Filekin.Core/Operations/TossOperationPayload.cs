using Filekin.Core.Commands.App;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Operations;

/// <summary>
/// Durable, platform-neutral detail for one successful <c>/toss</c> target. The Recycle Bin identity
/// is opaque app data: it exists only so infrastructure can restore the exact item and must never be
/// rendered as a user-facing path.
/// </summary>
public sealed record TossedItem
{
    public TossedItem(
        string originalPath,
        string name,
        bool isDirectory,
        string? recycleBinIdentity,
        string? restoreUnavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var hasIdentity = !string.IsNullOrWhiteSpace(recycleBinIdentity);
        if (hasIdentity == !string.IsNullOrWhiteSpace(restoreUnavailableReason))
        {
            throw new ArgumentException(
                "A tossed item must contain either an exact identity or an unavailable reason.");
        }

        OriginalPath = originalPath;
        Name = name;
        IsDirectory = isDirectory;
        RecycleBinIdentity = recycleBinIdentity;
        RestoreUnavailableReason = restoreUnavailableReason;
    }

    public string OriginalPath { get; }

    public string Name { get; }

    public bool IsDirectory { get; }

    public string? RecycleBinIdentity { get; }

    public string? RestoreUnavailableReason { get; }

    public bool CanRestore => RecycleBinIdentity is not null;

    public RecycledItem ToRecycledItem() => new(
        Name,
        OriginalPath,
        DeletedWhen: null,
        SizeBytes: null,
        IsDirectory,
        RecycleBinIdentity);
}

/// <summary>
/// One invocation remains one history row. Successful targets and independent failures stay together
/// so partial batches are not flattened or made to look wholly successful.
/// </summary>
public sealed record TossOperationPayload
{
    public TossOperationPayload(
        IReadOnlyList<TossedItem> items,
        IReadOnlyList<AppCommandFailure> failures,
        IReadOnlyList<TossedItem>? pendingItems = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(failures);
        if (items.Count == 0)
        {
            throw new ArgumentException("A toss history payload requires a successful target.", nameof(items));
        }

        var pending = pendingItems ?? items;
        ArgumentNullException.ThrowIfNull(pending);
        var unmatchedItems = items.ToList();
        if (pending.Any(item => !unmatchedItems.Remove(item)))
        {
            throw new ArgumentException(
                "Pending tossed items must belong to the original operation.",
                nameof(pendingItems));
        }

        Items = [.. items];
        Failures = [.. failures];
        PendingItems = [.. pending];
    }

    public IReadOnlyList<TossedItem> Items { get; }

    public IReadOnlyList<AppCommandFailure> Failures { get; }

    public bool CanRestore => Items.All(static item => item.CanRestore);

    /// <summary>
    /// Exact items still eligible for another Restore attempt. The original item list remains intact
    /// for durable history detail after a partial attempt.
    /// </summary>
    public IReadOnlyList<TossedItem> PendingItems { get; }

    public TossOperationPayload WithPendingItems(IReadOnlyList<TossedItem> pendingItems) =>
        new(Items, Failures, pendingItems);
}
