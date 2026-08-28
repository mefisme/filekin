using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// <c>/copy &lt;source&gt; &lt;destination&gt;</c> — an immediate app-owned filesystem copy, not a
/// clipboard copy (DECISIONS.md, 2026-08-24 — "`/copy` Requires a Destination"). Copy is intentionally
/// outside the version-one guaranteed undo set.
/// </summary>
public sealed class CopyCommand : TransferCommand
{
    public CopyCommand(IFileSystemOperations operations)
        : base(operations)
    {
    }

    public override string Name => "copy";

    protected override string Usage => "/copy <source> [<source> …] <destination>";

    protected override string PastVerb => "Copied";

    protected override void PerformTransfer(string source, string target)
    {
        Operations.Copy(source, target);
    }
}
