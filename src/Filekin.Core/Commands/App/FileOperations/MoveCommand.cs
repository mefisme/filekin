using Filekin.Core.FileSystem;
using Filekin.Core.Operations;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/move &lt;source&gt; &lt;destination&gt;</c> — an immediate app-owned filesystem move
/// (DECISIONS.md, 2026-08-24 — "`/move` Supports Selection-to-Destination Workflows"). Move is an
/// expected version-one undo candidate; the operation journal that records it is a separate subsystem.
/// </summary>
public sealed class MoveCommand : TransferCommand
{
    public MoveCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "move";

    protected override string Usage => "/move <source> <destination>";

    protected override string PastVerb => "Moved";

    protected override void PerformTransfer(string source, string target)
    {
        Operations.Move(source, target);
    }

    protected override PathRelocation DescribeRelocation(string source, string target) => new(source, target);
}
