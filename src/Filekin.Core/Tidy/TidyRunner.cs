using Filekin.Core.FileSystem;

namespace Filekin.Core.Tidy;

/// <summary>
/// Carries out a <see cref="TidyPlan"/>, limited to the categories the user left ticked.
///
/// Conflict and permission handling is deliberately thin. The ENGINEERING-GUARDRAILS forbid
/// duplicating the file-operation conflict logic inside the Tidy engine, and the planner has already
/// decided what is safe to move, so this type only has to honour two things the plan cannot promise:
/// the destination may have gained a file since the plan was built, and any individual move may fail
/// on permissions. Both are per-file outcomes — ARCHITECTURE.md Topic 5Y requires a batch to keep
/// going where it safely can rather than becoming all-or-nothing.
/// </summary>
public sealed class TidyRunner
{
    private readonly IFileSystemOperations _operations;

    public TidyRunner(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public TidyOutcome Run(
        TidyPlan plan,
        IReadOnlyCollection<TidyCategory> categories,
        IProgress<TidyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(categories);

        var groups = plan.Groups.Where(group => categories.Contains(group.Category)).ToList();
        var total = groups.Sum(group => group.Count);
        var moved = 0;
        var used = new List<TidyCategory>();
        var skipped = plan.Skipped.ToList();
        var failures = new List<TidyFailure>();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create the folder only when something is actually going into it, so a cancelled or
            // fully-failed group never leaves an empty folder behind.
            var folderReady = false;
            foreach (var item in group.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new TidyProgress(moved, total, item.Name));

                var target = Path.Combine(group.DestinationPath, item.Name);
                if (_operations.GetKind(target) != FileSystemEntryKind.None)
                {
                    // Something arrived between planning and moving. Never overwrite it.
                    skipped.Add(new TidySkip(item.Name, $"already in {group.Category.FolderName()}"));
                    continue;
                }

                try
                {
                    if (!folderReady)
                    {
                        _operations.CreateDirectory(group.DestinationPath);
                        folderReady = true;
                        used.Add(group.Category);
                    }

                    _operations.Move(item.SourcePath, target);
                    moved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    failures.Add(new TidyFailure(item.Name, ex.Message));
                }
            }
        }

        progress?.Report(new TidyProgress(moved, total, string.Empty));
        return new TidyOutcome(plan.FolderPath, moved, used, skipped, failures);
    }
}
