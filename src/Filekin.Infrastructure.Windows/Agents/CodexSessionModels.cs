namespace Filekin.Infrastructure.Windows.Agents;

internal sealed record CodexThreadSession(string ThreadId, string SessionId, string? Name);

internal sealed record CodexTurnHandle(string ThreadId, string TurnId);

internal sealed record CodexTurnCompletion(
    string? ThreadId,
    string TurnId,
    string Status,
    string? ErrorMessage);

internal sealed record CodexAppServerRequest(
    long Id,
    string Method,
    System.Text.Json.JsonElement Parameters);

/// <summary>A structured App Server JSON-RPC error kept intact for supported recovery decisions.</summary>
internal sealed class CodexAppServerRequestException(System.Text.Json.JsonElement error)
    : InvalidOperationException($"Codex App Server request failed: {error.GetRawText()}")
{
    public System.Text.Json.JsonElement Error { get; } = error.Clone();

    public bool IsArchivedThread =>
        Error.TryGetProperty("code", out var code) &&
        code.TryGetInt32(out var value) &&
        value == -32600 &&
        Error.TryGetProperty("message", out var message) &&
        message.ValueKind == System.Text.Json.JsonValueKind.String &&
        message.GetString() is { } text &&
        text.Contains(" is archived.", StringComparison.OrdinalIgnoreCase);
}
