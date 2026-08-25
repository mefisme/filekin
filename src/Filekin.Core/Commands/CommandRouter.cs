using Filekin.Core.Shell;
using Filekin.Core.Terminal;

namespace Filekin.Core.Commands;

/// <summary>
/// Routes Files command-bar input across the two confirmed execution paths: the persistent
/// PowerShell runspace for finite work, and ConPTY-backed terminal sessions for known interactive
/// tools. It also consumes the provider-delegation terminal launch a finite command can produce.
/// Application-owned <c>/</c> commands are recognized and returned for a separate subsystem; this
/// router does not execute them.
/// </summary>
public sealed class CommandRouter
{
    private readonly IShellBackend _shell;
    private readonly ITerminalHost _terminalHost;
    private readonly ICommandClassifier _classifier;

    public CommandRouter(IShellBackend shell, ITerminalHost terminalHost, ICommandClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(terminalHost);
        ArgumentNullException.ThrowIfNull(classifier);

        _shell = shell;
        _terminalHost = terminalHost;
        _classifier = classifier;
    }

    public async Task<CommandRouterResult> RouteAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var classification = _classifier.Classify(input);

        switch (classification.Route)
        {
            case CommandRoute.AppCommand:
                return CommandRouterResult.ForAppCommand(input);

            case CommandRoute.InteractiveTerminal:
                return await StartInteractiveTerminalAsync(input, classification, cancellationToken).ConfigureAwait(false);

            case CommandRoute.FiniteShell:
            default:
                return await ExecuteFiniteAsync(input, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CommandRouterResult> StartInteractiveTerminalAsync(
        string input,
        CommandClassification classification,
        CancellationToken cancellationToken)
    {
        // A terminal inherits the current Files location once at creation. The command-bar shell
        // location and the Files location are the same filesystem-backed context by invariant.
        var location = await _shell.GetLocationAsync(cancellationToken).ConfigureAwait(false);

        var launch = new ShellTerminalLaunchRequest(location, input.Trim());
        var request = new TerminalSessionRequest(launch, title: BuildTitle(classification.Executable, location));
        var session = _terminalHost.Start(request);

        return CommandRouterResult.ForTerminal(session);
    }

    private async Task<CommandRouterResult> ExecuteFiniteAsync(string input, CancellationToken cancellationToken)
    {
        var result = await _shell.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);

        // A finite command that navigated to a non-filesystem provider is delegated to a fresh
        // ConPTY-backed PowerShell at that provider path; the runspace has already restored the
        // Files filesystem location.
        if (result.TerminalLaunchRequest is { } terminalLaunchRequest)
        {
            var delegatedSession = _terminalHost.Start(new TerminalSessionRequest(terminalLaunchRequest));
            return CommandRouterResult.ForFiniteWithDelegation(result, delegatedSession);
        }

        return CommandRouterResult.ForFinite(result);
    }

    private static string? BuildTitle(string? executable, ShellLocation location)
    {
        if (string.IsNullOrEmpty(executable))
        {
            return null;
        }

        // Prefer "tool · launch-folder" (for example "claude · App"); fall back to the tool alone.
        if (location.FileSystemPath is { } fileSystemPath)
        {
            var folder = Path.GetFileName(
                fileSystemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (!string.IsNullOrEmpty(folder))
            {
                return $"{executable} · {folder}";
            }
        }

        return executable;
    }
}
