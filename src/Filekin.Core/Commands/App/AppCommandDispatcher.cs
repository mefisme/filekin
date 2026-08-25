using Filekin.Core.Shell;

namespace Filekin.Core.Commands.App;

/// <summary>
/// Routes a parsed application command to its registered handler. Command names are matched
/// case-insensitively; an unknown command or a bare <c>/</c> returns an error result rather than
/// throwing, so the command bar can report it like any other command outcome.
/// </summary>
public sealed class AppCommandDispatcher : IAppCommandDispatcher
{
    private readonly Dictionary<string, IAppCommand> _commands;

    public AppCommandDispatcher(IEnumerable<IAppCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var map = new Dictionary<string, IAppCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (!map.TryAdd(command.Name, command))
            {
                throw new ArgumentException($"Duplicate application command registered: /{command.Name}", nameof(commands));
            }
        }

        _commands = map;
    }

    public Task<AppCommandResult> DispatchAsync(
        string input,
        ShellLocation currentLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(currentLocation);

        if (!AppCommandParser.TryParse(input, out var parsed))
        {
            return Task.FromResult(AppCommandResult.Fail("Enter a command after '/'."));
        }

        if (!_commands.TryGetValue(parsed.Name, out var command))
        {
            return Task.FromResult(AppCommandResult.Fail($"Unknown command: /{parsed.Name}"));
        }

        var context = new AppCommandContext(currentLocation, parsed);
        return command.ExecuteAsync(context, cancellationToken);
    }
}
