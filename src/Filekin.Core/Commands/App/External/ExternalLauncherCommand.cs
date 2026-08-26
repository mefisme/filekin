namespace Filekin.Core.Commands.App.External;

/// <summary>
/// Shared base for the external escape-hatch commands (<c>/ext</c>, <c>/reveal</c>). Each acts on the
/// current Files folder, which must be a filesystem location; launch failures become
/// <see cref="AppCommandResult"/> errors rather than exceptions. Subclasses decide how they use their
/// arguments.
/// </summary>
public abstract class ExternalLauncherCommand : IAppCommand
{
    protected ExternalLauncherCommand(IExternalLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        Launcher = launcher;
    }

    public abstract string Name { get; }

    protected IExternalLauncher Launcher { get; }

    public Task<AppCommandResult> ExecuteAsync(AppCommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.CurrentLocation.IsFileSystem)
        {
            return Task.FromResult(AppCommandResult.Fail(
                "This command only works in a filesystem location, not the current provider location."));
        }

        try
        {
            return Task.FromResult(Execute(context.CurrentLocation.FileSystemPath!, context.Command.Arguments));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(AppCommandResult.Fail($"/{Name} failed: {ex.Message}"));
        }
    }

    protected abstract AppCommandResult Execute(string folderPath, IReadOnlyList<string> arguments);
}
