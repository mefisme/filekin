using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/toss &lt;target&gt; [&lt;target&gt; …]</c> — sends one or more files or directories to the Recycle
/// Bin through the app-owned Windows-native delete path. It is deliberately not named <c>/delete</c>:
/// the value over PowerShell's <c>del</c>/<c>rm</c> is that it is recoverable ("toss it in the trash";
/// it sits there until emptied). It is not a permanent-delete shortcut (DECISIONS.md, 2026-08-24 —
/// Windows-native delete behavior). A multi-item <c>@selection</c> expands to several targets, so all
/// targets are validated to exist before any are removed. (The Recycle Bin itself is opened with the
/// separate <c>/trash</c> command.)
/// </summary>
public sealed class TossCommand : FileOperationCommand
{
    public TossCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "toss";

    protected override AppCommandResult Execute(AppCommandContext context)
    {
        var arguments = context.Command.Arguments;
        if (arguments.Count == 0)
        {
            throw new CommandArgumentException("Usage: /toss <target> [<target> …]");
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
