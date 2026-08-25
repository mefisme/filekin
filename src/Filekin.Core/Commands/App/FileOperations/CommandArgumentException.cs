namespace Filekin.Core.Commands.App.FileOperations;

/// <summary>
/// Signals an ordinary user-facing argument or precondition problem inside a file-operation command.
/// The base command catches it and reports <see cref="Exception.Message"/> as a command error, so it
/// must always carry a message that is safe and useful to show in the command bar.
/// </summary>
internal sealed class CommandArgumentException : Exception
{
    public CommandArgumentException(string message)
        : base(message)
    {
    }
}
