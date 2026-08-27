using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/rename &lt;target&gt; &lt;new-name&gt;</c> — renames a single file or directory in place
/// (DECISIONS.md, 2026-08-24 — "`/rename` Remains Available but Simple"). The new name is a bare leaf
/// name, not a path: renaming into a different directory is a move, so a new name containing a path
/// separator is rejected. Rename is an expected version-one undo candidate.
/// </summary>
public sealed class RenameCommand : FileOperationCommand
{
    public RenameCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "rename";

    protected override AppCommandResult Execute(AppCommandContext context)
    {
        RequireArgumentCount(context, 2, "/rename <target> <new-name>");

        var target = ResolvePath(context, context.Command.Arguments[0]);
        var newName = context.Command.Arguments[1];

        if (newName.Contains(Path.DirectorySeparatorChar) ||
            newName.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(newName))
        {
            throw new CommandArgumentException("New name must be a file name, not a path. Use /move to relocate.");
        }

        RequireExists(target, "Target");

        var parent = Path.GetDirectoryName(target)
            ?? throw new CommandArgumentException("Cannot rename a filesystem root.");
        var renamed = Path.Combine(parent, newName);

        EnsureAbsent(renamed);
        Operations.Move(target, renamed);

        return AppCommandResult.Ok(
            $"Renamed {GetLeafName(target)} → {newName}",
            [renamed],
            [new PathRelocation(target, renamed)]);
    }
}
