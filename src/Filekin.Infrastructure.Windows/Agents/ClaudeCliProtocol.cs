using System.Text;
using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// One entry from <c>claude agents --json</c>. <paramref name="Id"/> is the short handle
/// <c>claude attach</c> takes; <paramref name="SessionId"/> is the conversation Filekin stores.
/// </summary>
public sealed record ClaudeBackgroundAgent(
    string? Id,
    string? SessionId,
    string? Cwd,
    string? Kind,
    string? Name,
    string? Status,
    string? State,
    int? Pid)
{
    /// <summary>
    /// Whether this session still exists and can be attached to.
    /// </summary>
    /// <remarks>
    /// Liveness is the process, not <c>state</c>. A background session reports <c>state: "done"</c>
    /// when its <em>turn</em> finished, and then stays alive and idle waiting for more — which is the
    /// ordinary condition of a session somebody wants to open. Observed directly: an agent reporting
    /// <c>"state": "done"</c> was still running as a real process, still holding its own Filekin MCP
    /// writer, after Filekin itself had closed. Claude reports <c>pid</c> for the entries that are
    /// actually running, so that is what this asks.
    /// </remarks>
    public bool IsLiveBackgroundSession =>
        string.Equals(Kind, "background", StringComparison.OrdinalIgnoreCase) &&
        Id is { Length: > 0 } &&
        Pid is > 0;
}

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
                processIdElement.ValueKind == JsonValueKind.Number &&
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

    /// <summary>
    /// The background agents <c>claude agents --json</c> reports. Claude gives a background session
    /// two different identities: <c>id</c>, the short handle <c>attach</c>, <c>logs</c>, <c>stop</c>
    /// and <c>rm</c> take, and <c>sessionId</c>, the conversation <c>--resume</c> continues. Filekin
    /// stores the conversation, so the short handle has to be looked up when it is needed.
    /// </summary>
    public static IReadOnlyList<ClaudeBackgroundAgent> ParseBackgroundAgents(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var agents = new List<ClaudeBackgroundAgent>();
        using var document = JsonDocument.Parse(StripAnsiEscapeSequences(output));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            agents.Add(new ClaudeBackgroundAgent(
                Text(element, "id"),
                Text(element, "sessionId"),
                Text(element, "cwd"),
                Text(element, "kind"),
                Text(element, "name"),
                Text(element, "status"),
                Text(element, "state"),
                Number(element, "pid")));
        }

        return agents;
    }

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
            resetElement.ValueKind == JsonValueKind.Number &&
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
