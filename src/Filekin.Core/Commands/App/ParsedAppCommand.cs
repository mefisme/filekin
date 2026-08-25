namespace Filekin.Core.Commands.App;

/// <summary>
/// A parsed application command: the command name (without the leading <c>/</c>, lower-cased for
/// case-insensitive lookup) and its already-tokenized arguments. Argument tokens are produced by
/// <see cref="AppCommandParser"/>, which is quote-aware so filesystem targets may contain spaces.
/// </summary>
public sealed record ParsedAppCommand
{
    public ParsedAppCommand(string name, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(arguments);

        Name = name;
        Arguments = arguments;
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments { get; }
}
