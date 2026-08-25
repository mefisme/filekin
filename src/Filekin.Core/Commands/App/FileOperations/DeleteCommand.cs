using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/delete &lt;target&gt;</c> — deletes a single file or directory through the app-owned
/// Windows-native delete path, following the user's Recycle Bin behavior where supported. It is not a
/// permanent-delete shortcut (DECISIONS.md, 2026-08-24 — "`/delete` Uses App-Owned Windows-Native
/// Delete Behavior"). Multi-target <c>@selection</c> deletion arrives with reference-selection
/// expansion; this command handles the documented single-target grammar.
/// </summary>
public sealed class DeleteCommand : FileOperationCommand
{
    public DeleteCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "delete";

    protected override AppCommandResult Execute(AppCommandContext context)
    {
        RequireArgumentCount(context, 1, "/delete <target>");

        var target = ResolvePath(context, context.Command.Arguments[0]);
        RequireExists(target, "Target");

        Operations.Recycle(target);

        return AppCommandResult.Ok($"Deleted {GetLeafName(target)} (Recycle Bin)", target);
    }
}
