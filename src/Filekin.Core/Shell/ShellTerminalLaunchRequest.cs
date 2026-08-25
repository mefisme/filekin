namespace Filekin.Core.Shell;

public sealed record ShellTerminalLaunchRequest(
    ShellLocation InitialLocation,
    string? CommandText = null);
