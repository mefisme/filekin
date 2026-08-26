using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// Shared logic for the transfer commands <c>/copy</c> and <c>/move</c>. The last argument is the
/// destination and every earlier argument is a source, so the single form is <c>/copy a b</c> and a
/// multi-item <c>@selection</c> expands to <c>/copy a b c dest</c>. With more than one source the
/// destination must be an existing folder; all sources are validated to exist first, so a bad argument
/// does not leave a half-finished batch. The commands differ only in the concrete filesystem action and
/// the past-tense verb they report.
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
        var sources = new List<string>(arguments.Count - 1);
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            var source = ResolvePath(context, arguments[i]);
            RequireExists(source, "Source");
            sources.Add(source);
        }

        if (sources.Count > 1 && Operations.GetKind(destination) != FileSystemEntryKind.Directory)
        {
            throw new CommandArgumentException(
                $"Destination must be an existing folder when moving or copying multiple items: {destination}");
        }

        var targets = new List<string>(sources.Count);
        foreach (var source in sources)
        {
            var target = ComputeTransferTarget(source, destination);
            EnsureAbsent(target);
            PerformTransfer(source, target);
            targets.Add(target);
        }

        var message = targets.Count == 1
            ? $"{PastVerb} {GetLeafName(sources[0])} → {targets[0]}"
            : $"{PastVerb} {targets.Count} items → {destination}";
        return AppCommandResult.Ok(message, [.. targets]);
    }

    protected abstract void PerformTransfer(string source, string target);
}
