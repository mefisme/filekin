namespace Filekin.Core.Shell;

public sealed record ShellExecutionResult(
    IReadOnlyList<string> Output,
    IReadOnlyList<string> Errors,
    ShellLocation CurrentLocation,
    ShellTerminalLaunchRequest? TerminalLaunchRequest);
