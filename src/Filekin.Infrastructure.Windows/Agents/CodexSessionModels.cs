namespace Filekin.Infrastructure.Windows.Agents;

internal sealed record CodexThreadSession(string ThreadId, string SessionId, string? Name);

internal sealed record CodexTurnHandle(string ThreadId, string TurnId);

internal sealed record CodexTurnCompletion(
    string? ThreadId,
    string TurnId,
    string Status,
    string? ErrorMessage);
