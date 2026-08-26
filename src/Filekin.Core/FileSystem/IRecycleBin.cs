namespace Filekin.Core.FileSystem;

/// <summary>
/// Reads and restores the Windows Recycle Bin, so <c>/toss</c>'d items can be browsed and put back
/// (UX-DESIGN.md — "Recycle Bin and Virtual Locations"; it appears as a readable Files surface, never
/// the raw <c>$Recycle.Bin</c> hierarchy). The calls do real shell work and must be offloaded from the
/// UI thread by the caller.
/// </summary>
public interface IRecycleBin
{
    /// <summary>Lists the current contents of the Recycle Bin. Order is unspecified.</summary>
    IReadOnlyList<RecycledItem> List();

    /// <summary>
    /// Restores <paramref name="item"/> to its original location. Returns <c>true</c> if a matching
    /// entry was found and restored; <c>false</c> if it was no longer present.
    /// </summary>
    bool Restore(RecycledItem item);

    /// <summary>
    /// Permanently deletes a single <paramref name="item"/> from the Recycle Bin. Returns <c>true</c> if a
    /// matching entry was found and deleted; <c>false</c> if it was no longer present. This cannot be
    /// undone; the caller confirms with the user first.
    /// </summary>
    bool DeleteForever(RecycledItem item);

    /// <summary>
    /// Permanently deletes every item in the Recycle Bin. This cannot be undone; the caller is
    /// responsible for confirming with the user first.
    /// </summary>
    void Empty();
}
