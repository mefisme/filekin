namespace Filekin.Core.Commands;

/// <summary>
/// The classification of a line of command-bar input: which route it takes and, when the input
/// is a shell invocation, the normalized executable name (path and extension stripped).
/// </summary>
public readonly record struct CommandClassification(CommandRoute Route, string? Executable);
