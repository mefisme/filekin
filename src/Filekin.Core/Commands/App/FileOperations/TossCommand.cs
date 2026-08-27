using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/toss &lt;target&gt; [&lt;target&gt; …]</c> — sends one or more files or directories to the Recycle
/// Bin through the app-owned Windows-native delete path. The value over PowerShell's <c>del</c>/<c>rm</c>
/// is that it is recoverable ("toss it in the trash"; it sits there until emptied). It is not a
/// permanent-delete shortcut (DECISIONS.md, 2026-08-24 — Windows-native delete behavior). A multi-item
/// <c>@selection</c> expands to several targets, so all targets are validated to exist before any are
/// removed.
///
/// <c>/trash</c> and <c>/delete</c> are confirmed aliases of this one command (DECISIONS.md,
/// 2026-08-27), because a user reaching for recoverable delete types whichever word they already
/// know. The Recycle Bin view itself is opened with the separate <c>/recycle</c> command, so no alias
/// here is ambiguous.
/// </summary>
public sealed class TossCommand : FileOperationCommand
{
    public TossCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "toss";

    public override IReadOnlyList<string> Aliases => ["trash", "delete"];

    protected override AppCommandResult Execute(AppCommandContext context)
    {
        var arguments = context.Command.Arguments;
        if (arguments.Count == 0)
        {
            // Echo the alias the user typed so the usage line matches what they wrote.
            throw new CommandArgumentException($"Usage: /{context.Command.Name} <target> [<target> …]");
        }

        var targets = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            var target = ResolvePath(context, argument);
            RequireExists(target, "Target");
            targets.Add(target);
        }

        foreach (var target in targets)
        {
            Operations.Recycle(target);
        }

        var message = targets.Count == 1
            ? $"Moved {GetLeafName(targets[0])} to the Recycle Bin"
            : $"Moved {targets.Count} items to the Recycle Bin";
        return AppCommandResult.Ok(message, [.. targets]);
    }
}
