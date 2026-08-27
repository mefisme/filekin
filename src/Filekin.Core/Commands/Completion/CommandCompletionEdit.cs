namespace Filekin.Core.Commands.Completion;

/// <summary>The command text and caret position produced by accepting a completion.</summary>
public sealed record CommandCompletionEdit(string Text, int CaretIndex);
