using Filekin.Core.Commands.App.FileOperations;
using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App;

/// <summary>
/// Composition helpers for the built-in version-one application commands. Kept separate from the
/// dispatcher so command registration stays explicit and testable without a dependency-injection
/// container.
/// </summary>
public static class BuiltInAppCommands
{
    /// <summary>
    /// The confirmed core file-operation commands (DECISIONS.md, 2026-08-24 — "Core App-Owned File
    /// Operation Commands"), bound to a filesystem-operations implementation.
    /// </summary>
    public static IReadOnlyList<IAppCommand> CreateFileOperations(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        return
        [
            new CopyCommand(operations),
            new MoveCommand(operations),
            new RenameCommand(operations),
            new DeleteCommand(operations),
        ];
    }

    /// <summary>Builds a dispatcher over the built-in file-operation commands.</summary>
    public static AppCommandDispatcher CreateDispatcher(IFileSystemOperations operations)
    {
        return new AppCommandDispatcher(CreateFileOperations(operations));
    }
}
