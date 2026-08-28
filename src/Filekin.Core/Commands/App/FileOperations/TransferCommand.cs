using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// Shared logic for the transfer commands <c>/copy</c> and <c>/move</c>. The last argument is the
/// destination and every earlier argument is a source, so the single form is <c>/copy a b</c> and a
/// multi-item <c>@selection</c> expands to <c>/copy a b c dest</c>. With more than one source the
/// destination must be an existing folder. Each source is then validated and processed independently:
/// one missing, colliding, locked, or inaccessible target is reported without blocking unrelated
/// sources. The commands differ only in the concrete filesystem action and the past-tense verb they
/// report.
/// </summary>
public abstract class TransferCommand : FileOperationCommand
{
    protected TransferCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    protected abstract string Usage { get; }

    protected abstract string PastVerb { get; }

    protected override AppCommandResult Execute(AppCommandContext context)
    {
        var arguments = context.Command.Arguments;
        if (arguments.Count < 2)
        {
            throw new CommandArgumentException($"Usage: {Usage}");
        }

        var destination = ResolvePath(context, arguments[^1]);
        var sourceCount = arguments.Count - 1;
        if (sourceCount > 1 && Operations.GetKind(destination) != FileSystemEntryKind.Directory)
        {
            throw new CommandArgumentException(
                $"Destination must be an existing folder when moving or copying multiple items: {destination}");
        }

        var sources = new List<string>(sourceCount);
        var targets = new List<string>(sourceCount);
        var relocations = new List<PathRelocation>(sourceCount);
        var failures = new List<AppCommandFailure>();
        var failedWhileWriting = false;
        for (var index = 0; index < sourceCount; index++)
        {
            var rawSource = arguments[index];
            var failureTarget = string.IsNullOrWhiteSpace(rawSource) ? "(empty target)" : rawSource;
            try
            {
                var source = ResolvePath(context, rawSource);
                failureTarget = source;
                RequireExists(source, "Source");
                var target = ComputeTransferTarget(source, destination);
                EnsureAbsent(target);
                PerformTransfer(source, target);
                sources.Add(source);
                targets.Add(target);
                if (DescribeRelocation(source, target) is { } relocation)
                {
                    relocations.Add(relocation);
                }
            }
            catch (Exception ex) when (IsTargetFailure(ex))
            {
                failures.Add(new AppCommandFailure(failureTarget, ex.Message));
                failedWhileWriting |= MayHaveWritten(ex);
            }
        }

        if (targets.Count == 0)
        {
            return AppCommandResult.FailedBatch(
                sourceCount == 1
                    ? failures[0].Message
                    : $"0 {PastVerb.ToLowerInvariant()} · {failures.Count} failed",
                failures,
                failedWhileWriting);
        }

        if (failures.Count > 0)
        {
            return AppCommandResult.Partial(
                $"{targets.Count} {PastVerb.ToLowerInvariant()} · {failures.Count} failed",
                targets,
                relocations,
                failures);
        }

        var message = targets.Count == 1
            ? $"{PastVerb} {GetLeafName(sources[0])} → {targets[0]}"
            : $"{PastVerb} {targets.Count} items → {destination}";
        return AppCommandResult.Ok(message, targets, relocations);
    }

    protected abstract void PerformTransfer(string source, string target);

    protected virtual PathRelocation? DescribeRelocation(string source, string target) => null;
}
