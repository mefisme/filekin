using Filekin.Core.Commands.References;

namespace Filekin.Core.Commands.App.Locations;

/// <summary>Manages the saved Locations that become sidebar entries and command-bar references.</summary>
public sealed class LocationCommand : IAppCommand
{
    private readonly IUserLocationEditor _locations;

    public LocationCommand(IUserLocationEditor locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        _locations = locations;
    }

    public string Name => "location";

    public async Task<AppCommandResult> ExecuteAsync(
        AppCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = context.Command.Arguments;
        if (arguments.Count == 0)
        {
            return AppCommandResult.Fail(Usage);
        }

        UserLocationEditResult result;
        try
        {
            switch (arguments[0].ToLowerInvariant())
            {
                case "add" when arguments.Count == 3:
                    result = await _locations.AddAsync(
                        arguments[1],
                        ResolvePath(context, arguments[2]),
                        cancellationToken).ConfigureAwait(false);
                    break;

                case "set" when arguments.Count == 3:
                    result = await _locations.SetPathAsync(
                        arguments[1],
                        ResolvePath(context, arguments[2]),
                        cancellationToken).ConfigureAwait(false);
                    break;

                case "rename" when arguments.Count == 3:
                    result = await _locations.RenameAsync(
                        arguments[1],
                        arguments[2],
                        cancellationToken).ConfigureAwait(false);
                    break;

                case "remove" when arguments.Count == 2:
                    result = await _locations.RemoveAsync(arguments[1], cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    return AppCommandResult.Fail(Usage);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return AppCommandResult.Fail("The Location path is not valid.");
        }

        return result.Succeeded
            ? AppCommandResult.Ok(result.Message)
            : AppCommandResult.Fail(result.Message);
    }

    private static string ResolvePath(AppCommandContext context, string path)
    {
        if (!context.CurrentLocation.IsFileSystem)
        {
            return path;
        }

        return Path.GetFullPath(path, context.CurrentLocation.FileSystemPath!);
    }

    private const string Usage =
        "Use /location add <name> <path>, set <name> <path>, rename <name> <new-name>, or remove <name>.";
}
