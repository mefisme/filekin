using Filekin.Core.Shell;

namespace Filekin.Core.Commands.App;

/// <summary>
/// Consumes the raw <c>/</c>-prefixed input surfaced by the command router
/// (<see cref="CommandRouterResult.AppCommandInput"/>) and runs the matching built-in command
/// against the current Files location.
/// </summary>
public interface IAppCommandDispatcher
{
    Task<AppCommandResult> DispatchAsync(
        string input,
        ShellLocation currentLocation,
        CancellationToken cancellationToken = default);
}
