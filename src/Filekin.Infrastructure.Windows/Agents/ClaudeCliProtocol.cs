using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal static class ClaudeCliProtocol
{
    public static ClaudeSubscriptionAccount ParseAccount(JsonElement result) =>
        new(
            ReadBoolean(result, "loggedIn"),
            ReadString(result, "authMethod"),
            ReadString(result, "apiProvider"),
            ReadString(result, "subscriptionType"));

    public static AgentUsageSnapshot ParseStatusLineUsage(
        JsonElement statusLine,
        DateTimeOffset observedAt)
    {
        var windows = new List<AgentUsageWindow>();
        if (statusLine.TryGetProperty("rate_limits", out var rateLimits) &&
            rateLimits.ValueKind == JsonValueKind.Object)
        {
            AddWindow(windows, "five_hour", TimeSpan.FromHours(5), rateLimits);
            AddWindow(windows, "seven_day", TimeSpan.FromDays(7), rateLimits);
        }

        return new AgentUsageSnapshot(AgentProvider.ClaudeCode, observedAt, windows);
    }

    public static IReadOnlyList<ClaudeBackgroundSession> ParseBackgroundSessions(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Claude Code did not return a session array.");
        }

        var sessions = new List<ClaudeBackgroundSession>();
        foreach (var item in result.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var workingDirectory = ReadString(item, "cwd");
            var kind = ReadString(item, "kind");
            var state = ReadString(item, "state");
            if (id is null || workingDirectory is null || kind is null || state is null ||
                !item.TryGetProperty("startedAt", out var startedAtElement) ||
                !startedAtElement.TryGetInt64(out var startedAtMilliseconds))
            {
                throw new InvalidOperationException(
                    "Claude Code returned an incomplete background-session entry.");
            }

            int? processId = null;
            if (item.TryGetProperty("pid", out var processIdElement) &&
                processIdElement.TryGetInt32(out var parsedProcessId))
            {
                processId = parsedProcessId;
            }

            sessions.Add(new ClaudeBackgroundSession(
                id,
                ReadString(item, "sessionId"),
                workingDirectory,
                kind,
                state,
                ReadString(item, "status"),
                ReadString(item, "waitingFor"),
                processId,
                DateTimeOffset.FromUnixTimeMilliseconds(startedAtMilliseconds)));
        }

        return sessions;
    }

    private static void AddWindow(
        List<AgentUsageWindow> windows,
        string name,
        TimeSpan duration,
        JsonElement rateLimits)
    {
        if (!rateLimits.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var usedElement) ||
            !usedElement.TryGetDouble(out var usedPercent))
        {
            return;
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resets_at", out var resetElement) &&
            resetElement.TryGetInt64(out var resetSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
        }

        windows.Add(new AgentUsageWindow(
            $"claude:{name}",
            usedPercent,
            duration,
            resetsAt));
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
