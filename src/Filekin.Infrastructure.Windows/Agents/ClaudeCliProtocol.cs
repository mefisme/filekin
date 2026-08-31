using System.Text;
using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal static class ClaudeCliProtocol
{
    private const string BackgroundedPrefix = "backgrounded";
    private const int MaximumRecentOutputLength = 16_000;

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

    public static string ParseBackgroundLaunchId(string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = StripAnsiEscapeSequences(rawLine);
            var separator = line.IndexOf('\u00b7');
            if (separator < 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    BackgroundedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idField = line[(separator + 1)..].Trim();
            var nextSeparator = idField.IndexOf('\u00b7');
            var id = (nextSeparator >= 0 ? idField[..nextSeparator] : idField).Trim();
            if (id.Length > 0 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Claude Code did not return a background-session id.");
    }

    /// <summary>
    /// Normalizes the documented <c>claude logs &lt;id&gt;</c> output for a read-only view. This removes
    /// terminal decoration but does not parse a terminal screen or infer tool events from rendered
    /// text. The provider currently offers this recent-output snapshot, not typed background tool
    /// events, so Filekin presents it honestly as one provider update.
    /// </summary>
    public static string? NormalizeBackgroundLogs(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var normalized = StripAnsiEscapeSequences(output).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= MaximumRecentOutputLength
            ? normalized
            : $"…{normalized[^MaximumRecentOutputLength..]}";
    }

    private static string StripAnsiEscapeSequences(string value)
    {
        StringBuilder? result = null;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\u001b')
            {
                result?.Append(value[index]);
                continue;
            }

            result ??= new StringBuilder(value.Length).Append(value, 0, index);
            if (index + 1 >= value.Length)
            {
                continue;
            }

            if (value[index + 1] != '[')
            {
                index++;
                continue;
            }

            index += 2;
            while (index < value.Length && value[index] is < '@' or > '~')
            {
                index++;
            }
        }

        return result?.ToString() ?? value;
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
            !usedElement.TryGetDouble(out var usedPercent) ||
            !double.IsFinite(usedPercent) ||
            usedPercent < 0)
        {
            return;
        }

        // Claude documents these two windows as 0 to 100. A value above 100 would still mean the
        // window is spent, so it is recorded as fully used rather than dropped or invented away.
        usedPercent = Math.Min(usedPercent, 100);

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
