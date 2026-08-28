using Filekin.Core.FileSystem;

namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// Shared base for the app-owned file-operation commands (<c>/copy</c>, <c>/move</c>,
/// <c>/rename</c>, <c>/delete</c>). It resolves relative arguments against the current Files
/// location, turns ordinary user mistakes and filesystem failures into <see cref="AppCommandResult"/>
/// errors instead of exceptions, and leaves the actual side-effects to <see cref="IFileSystemOperations"/>.
/// </summary>
public abstract class FileOperationCommand : IAppCommand
{
    protected FileOperationCommand(IFileSystemOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations;
    }

    public abstract string Name { get; }

    /// <summary>Most file-operation commands have exactly one name; <c>/toss</c> overrides this.</summary>
    public virtual IReadOnlyList<string> Aliases => [];

    protected IFileSystemOperations Operations { get; }

    public Task<AppCommandResult> ExecuteAsync(AppCommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return Task.FromResult(Execute(context));
        }
        catch (CommandArgumentException ex)
        {
            return Task.FromResult(AppCommandResult.Fail(ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A filesystem exception means the command had already started writing, so a batch may
            // have completed some of its targets before failing. Report the name the user actually
            // typed, which may be an alias of this command.
            return Task.FromResult(
                AppCommandResult.FailedWhileWriting($"/{context.Command.Name} failed: {ex.Message}"));
        }
    }

    protected abstract AppCommandResult Execute(AppCommandContext context);

    /// <summary>
    /// Requires the argument list to have exactly <paramref name="expected"/> tokens, otherwise
    /// reports the command usage.
    /// </summary>
    protected static void RequireArgumentCount(AppCommandContext context, int expected, string usage)
    {
        if (context.Command.Arguments.Count != expected)
        {
            throw new CommandArgumentException($"Usage: {usage}");
        }
    }

    /// <summary>
    /// Resolves a raw argument to a fully-qualified path against the current Files location. Relative
    /// targets resolve against the visible folder; the command bar must be on a filesystem location.
    /// </summary>
    protected static string ResolvePath(AppCommandContext context, string rawPath)
    {
        if (!context.CurrentLocation.IsFileSystem)
        {
            throw new CommandArgumentException(
                "This command only works in a filesystem location, not the current provider location.");
        }

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new CommandArgumentException("A path argument is empty.");
        }

        return Path.GetFullPath(rawPath, context.CurrentLocation.FileSystemPath!);
    }

    /// <summary>Requires that <paramref name="path"/> exists, returning its kind.</summary>
    protected FileSystemEntryKind RequireExists(string path, string label)
    {
        var kind = Operations.GetKind(path);
        if (kind == FileSystemEntryKind.None)
        {
            throw new CommandArgumentException($"{label} not found: {path}");
        }

        return kind;
    }

    /// <summary>
    /// Refuses to continue when a target already exists. Version one does not silently overwrite;
    /// the interactive Replace / Keep Both / Skip conflict view (DECISIONS.md, 2026-08-24) is a
    /// later UI-layer concern that will wrap these operations.
    /// </summary>
    protected void EnsureAbsent(string path)
    {
        if (Operations.GetKind(path) != FileSystemEntryKind.None)
        {
            throw new CommandArgumentException($"Destination already exists: {path}");
        }
    }

    /// <summary>
    /// Computes the concrete destination path for a transfer. When the destination is an existing
    /// directory the source keeps its name inside it (<c>/copy build.exe @thisfolder</c>); otherwise
    /// the destination is treated as the full target path.
    /// </summary>
    protected string ComputeTransferTarget(string source, string destination)
    {
        if (Operations.GetKind(destination) == FileSystemEntryKind.Directory)
        {
            return Path.Combine(destination, GetLeafName(source));
        }

        return destination;
    }

    /// <summary>Returns the final path segment, tolerating a trailing directory separator.</summary>
    protected static string GetLeafName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>
    /// Failures expected from one independent target. These are isolated by batch commands; an
    /// unexpected programming failure still escapes rather than being presented as routine I/O.
    /// </summary>
    protected static bool IsTargetFailure(Exception exception) =>
        exception is CommandArgumentException or IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException or NotSupportedException;

    /// <summary>Whether a failed target may have written before the platform operation threw.</summary>
    protected static bool MayHaveWritten(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
