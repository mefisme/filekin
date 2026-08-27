using Filekin.Core.Commands.App.External;
using Filekin.Core.Commands.App.FileOperations;
using Filekin.Core.Commands.App.Locations;
using Filekin.Core.Commands.References;
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
            new TossCommand(operations),
        ];
    }

    /// <summary>
    /// The external escape-hatch command (<c>/ext</c>) that leaves the app for a real external terminal
    /// or a named external program (UX-DESIGN.md — External Terminal Escape Hatch).
    /// </summary>
    public static IReadOnlyList<IAppCommand> CreateExternalCommands(IExternalLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);

        return [new ExternalTerminalCommand(launcher)];
    }

    /// <summary>Builds a dispatcher over the built-in file-operation commands.</summary>
    public static AppCommandDispatcher CreateDispatcher(IFileSystemOperations operations)
    {
        return new AppCommandDispatcher(CreateFileOperations(operations));
    }

    /// <summary>Builds a dispatcher over the file-operation and external escape-hatch commands.</summary>
    public static AppCommandDispatcher CreateDispatcher(IFileSystemOperations operations, IExternalLauncher launcher)
    {
        return new AppCommandDispatcher([.. CreateFileOperations(operations), .. CreateExternalCommands(launcher)]);
    }

    /// <summary>Builds the complete dispatcher, including durable user Location management.</summary>
    public static AppCommandDispatcher CreateDispatcher(
        IFileSystemOperations operations,
        IExternalLauncher launcher,
        IUserLocationEditor locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return new AppCommandDispatcher(
            [.. CreateFileOperations(operations), .. CreateExternalCommands(launcher), new LocationCommand(locations)]);
    }
}
