namespace Filekin.Infrastructure.Windows.Agents;

internal sealed record ClaudeBackgroundSession(
    string Id,
    string? SessionId,
    string WorkingDirectory,
    string Kind,
    string State,
    string? Status,
    string? WaitingFor,
    int? ProcessId,
    DateTimeOffset StartedAt);
