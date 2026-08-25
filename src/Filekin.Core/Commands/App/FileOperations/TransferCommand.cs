using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// Shared logic for the two-argument transfer commands <c>/copy</c> and <c>/move</c>. Both resolve a
/// single source and destination against the current Files location, refuse to overwrite an existing
/// target, and differ only in the concrete filesystem action and the past-tense verb they report.
/// Multi-target <c>@selection</c> batches (DECISIONS.md — batch-operation philosophy) arrive once the
/// reference resolver expands a selection into multiple invocations; this command handles the
/// documented single source-to-destination grammar.
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
        RequireArgumentCount(context, 2, Usage);

        var source = ResolvePath(context, context.Command.Arguments[0]);
        var destination = ResolvePath(context, context.Command.Arguments[1]);

        RequireExists(source, "Source");
        var target = ComputeTransferTarget(source, destination);
        EnsureAbsent(target);

        PerformTransfer(source, target);

        return AppCommandResult.Ok($"{PastVerb} {GetLeafName(source)} → {target}", target);
    }

    protected abstract void PerformTransfer(string source, string target);
}
