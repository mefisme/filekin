namespace Filekin.Core.Commands.App;

/// <summary>
/// The parsed identity and authoritative result of one command handled by the common app-command
/// dispatcher. Presentation layers may carry this value without reconstructing filesystem mutations
/// from result text.
/// </summary>
public sealed record AppCommandExecutionDetail
{
    public AppCommandExecutionDetail(string commandName, AppCommandResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(result);
        CommandName = commandName;
        Result = result;
    }

    /// <summary>The lower-case command name produced by <see cref="AppCommandParser"/>.</summary>
    public string CommandName { get; }

    public AppCommandResult Result { get; }
}
