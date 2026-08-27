namespace Filekin.Core.Commands.Completion;

/// <summary>
/// One app-owned command-bar completion. <see cref="Text"/> includes the leading <c>/</c> or
/// <c>@</c>; <see cref="Description"/> is the concise explanation shown beside it.
/// </summary>
public sealed record CommandCompletionSuggestion(string Text, string Description)
{
    public string AutomationName => $"{Text}, {Description}";
}
