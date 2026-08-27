using Filekin.Core.Archives;
using Filekin.Core.FileSystem;

namespace Filekin.Infrastructure.Windows.Archives;

/// <summary>
/// Reverses a completed <c>/zip</c>: deletes the archive that was written, then restores the archive
/// it replaced, if any. Same ordering rule as <see cref="ZipExtractionUndo"/> — the new file goes
/// before the old one comes back, because they share a path.
/// </summary>
public sealed class ZipCompressionUndo : ICompressionUndo
{
    private readonly IRecycleBin _recycleBin;

    public ZipCompressionUndo(IRecycleBin recycleBin)
    {
        ArgumentNullException.ThrowIfNull(recycleBin);
        _recycleBin = recycleBin;
    }

    public Task<string> UndoAsync(CompressionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Task.Run(() => Undo(outcome), cancellationToken);
    }

    private string Undo(CompressionOutcome outcome)
    {
        var removed = false;
        try
        {
            if (File.Exists(outcome.OutputPath))
            {
                File.Delete(outcome.OutputPath);
                removed = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not remove {outcome.OutputName}: {ex.Message}";
        }

        if (outcome.ReplacedOriginal is not { Length: > 0 } original)
        {
            return removed
                ? $"Undid {outcome.OutputName}: the archive was removed."
                : $"{outcome.OutputName} was already gone.";
        }

        return TryRestore(original)
            ? $"Undid {outcome.OutputName}: the archive was removed and the previous one put back."
            : $"Undid {outcome.OutputName}, but the previous archive could not be restored.";
    }

    private bool TryRestore(string original)
    {
        try
        {
            var match = _recycleBin.List()
                .Where(item => item.OriginalPath.Equals(original, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.DeletedWhen ?? DateTime.MinValue)
                .FirstOrDefault();

            return match is not null && _recycleBin.Restore(match);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
