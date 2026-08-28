namespace Filekin.Core.Commands.App;

/// <summary>
/// One target that an app-owned batch command could not process. Successful targets remain in
/// <see cref="AppCommandResult.AffectedPaths"/> while failures stay available for result/history
/// surfaces without flattening the batch into one opaque error string.
/// </summary>
public sealed record AppCommandFailure
{
    public AppCommandFailure(string target, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Target = target;
        Message = message;
    }

    public string Target { get; }

    public string Message { get; }
}
