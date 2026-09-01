using System.Text.Json;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

internal static class CodexAppServerProtocol
{
    /// <param name="model">
    /// The model the user chose, or <see langword="null"/> to leave the choice to Codex's own
    /// configuration. Filekin sends the field only when there is a choice to send.
    /// </param>
    public static JsonElement CreateThreadStartParameters(
        string folderPath,
        string? model = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["cwd"] = Path.GetFullPath(folderPath),
            ["serviceName"] = "filekin",
        };

        // Only what the user actually chose is sent. Anything Filekin leaves out stays Codex's own.
        if (!string.IsNullOrWhiteSpace(model))
        {
            parameters["model"] = model.Trim();
        }

        return JsonSerializer.SerializeToElement(parameters);
    }

    /// <summary>
    /// Reads the models this Codex install actually offers. Filekin lists what Codex reports and
    /// never invents a model name.
    /// </summary>
    public static IReadOnlyList<AgentModelChoice> ParseModels(JsonElement result)
    {
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AgentModelChoice>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (entry.TryGetProperty("hidden", out var hidden) &&
                hidden.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if ((ReadString(entry, "model") ?? ReadString(entry, "id")) is not { Length: > 0 } id ||
                models.Any(model => string.Equals(model.Id, id, StringComparison.Ordinal)))
            {
                continue;
            }

            var efforts = new List<string>();
            if (entry.TryGetProperty("supportedReasoningEfforts", out var supported) &&
                supported.ValueKind == JsonValueKind.Array)
            {
                foreach (var effort in supported.EnumerateArray())
                {
                    var name = effort.ValueKind == JsonValueKind.String
                        ? effort.GetString()
                        : ReadString(effort, "reasoningEffort");
                    if (name is { Length: > 0 } && !efforts.Contains(name, StringComparer.Ordinal))
                    {
                        efforts.Add(name);
                    }
                }
            }

            models.Add(new AgentModelChoice(id, ReadString(entry, "displayName") ?? id, efforts));
        }

        return models;
    }

    public static JsonElement CreateThreadResumeParameters(string threadId, string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return JsonSerializer.SerializeToElement(new
        {
            threadId,
            cwd = Path.GetFullPath(folderPath),
            serviceName = "filekin",
        });
    }

    /// <summary>
    /// Builds one turn request. By default Filekin sends no approval or sandbox setting at all, so the
    /// owner's own Codex configuration stays in charge.
    /// </summary>
    /// <param name="trustFolder">
    /// Set only when the owner has said this folder is safe to work in. It scopes the run to that
    /// folder through Codex's own sandbox: work inside it needs no prompting, work outside it fails.
    /// Filekin still approves nothing on the owner's behalf, and asks for no network access.
    /// </param>
    public static JsonElement CreateTurnStartParameters(
        string threadId,
        string folderPath,
        string prompt,
        string? effort = null,
        bool trustFolder = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var workingDirectory = Path.GetFullPath(folderPath);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["threadId"] = threadId,
            ["input"] = new[] { new { type = "text", text = prompt } },
            ["cwd"] = workingDirectory,
        };
        if (!string.IsNullOrWhiteSpace(effort))
        {
            // App Server owns model selection on the thread, but effort is a turn override.
            parameters["effort"] = effort.Trim();
        }

        if (trustFolder)
        {
            // Codex's own workspace-write sandbox, and nothing added to it. The workspace is this
            // turn's working directory, which is the approved folder, so that is already the boundary.
            // Naming extra writable roots produces a root set its Windows restricted-token sandbox
            // refuses to enforce, and then every single file operation fails before it runs.
            parameters["sandboxPolicy"] = new
            {
                type = "workspaceWrite",
                networkAccess = false,
            };

            // Never ask, and never approve for the owner either. With the folder as the boundary
            // there is nothing left to approve: inside it the work is already allowed, and outside it
            // the work simply fails and is reported back to the agent.
            parameters["approvalPolicy"] = "never";
        }

        return JsonSerializer.SerializeToElement(parameters);
    }

    public static CodexSubscriptionAccount ParseAccount(JsonElement result)
    {
        if (!result.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
        {
            return new CodexSubscriptionAccount(false, null, null);
        }

        return new CodexSubscriptionAccount(
            true,
            ReadString(account, "type"),
            ReadString(account, "planType"));
    }

    public static AgentUsageSnapshot ParseRateLimits(JsonElement result, DateTimeOffset observedAt)
    {
        var windows = new List<AgentUsageWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var limitsById) &&
            limitsById.ValueKind == JsonValueKind.Object)
        {
            foreach (var limit in limitsById.EnumerateObject())
            {
                AddWindow(windows, limit.Name, "primary", limit.Value);
                AddWindow(windows, limit.Name, "secondary", limit.Value);
            }
        }
        else if (result.TryGetProperty("rateLimits", out var limits) &&
                 limits.ValueKind == JsonValueKind.Object)
        {
            var limitId = ReadString(limits, "limitId") ?? "codex";
            AddWindow(windows, limitId, "primary", limits);
            AddWindow(windows, limitId, "secondary", limits);
        }

        return new AgentUsageSnapshot(AgentProvider.Codex, observedAt, windows);
    }

    public static CodexThreadSession ParseThread(JsonElement result)
    {
        if (!result.TryGetProperty("thread", out var thread) ||
            ReadString(thread, "id") is not { } threadId)
        {
            throw new InvalidOperationException("Codex App Server did not return a thread id.");
        }

        return new CodexThreadSession(
            threadId,
            ReadString(thread, "sessionId") ?? threadId,
            ReadString(thread, "name"));
    }

    public static CodexTurnHandle ParseTurn(JsonElement result, string threadId)
    {
        if (!result.TryGetProperty("turn", out var turn) || ReadString(turn, "id") is not { } turnId)
        {
            throw new InvalidOperationException("Codex App Server did not return a turn id.");
        }

        return new CodexTurnHandle(threadId, turnId);
    }

    public static bool TryParseTurnCompletion(
        CodexAppServerNotification notification,
        out CodexTurnCompletion? completion)
    {
        completion = null;
        if (!string.Equals(notification.Method, "turn/completed", StringComparison.Ordinal) ||
            !notification.Parameters.TryGetProperty("turn", out var turn) ||
            ReadString(turn, "id") is not { } turnId ||
            ReadString(turn, "status") is not { } status)
        {
            return false;
        }

        string? errorMessage = null;
        if (turn.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            errorMessage = ReadString(error, "message");
        }

        completion = new CodexTurnCompletion(
            ReadString(notification.Parameters, "threadId") ?? ReadString(turn, "threadId"),
            turnId,
            status,
            errorMessage);
        return true;
    }

    public static bool TryParseServerRequest(
        JsonElement message,
        out CodexAppServerRequest? request)
    {
        request = null;
        if (!message.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt64(out var id) ||
            !message.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String ||
            !message.TryGetProperty("params", out var parameters))
        {
            return false;
        }

        request = new CodexAppServerRequest(id, methodElement.GetString()!, parameters.Clone());
        return true;
    }

    private static void AddWindow(
        List<AgentUsageWindow> windows,
        string limitId,
        string windowName,
        JsonElement limit)
    {
        if (!limit.TryGetProperty(windowName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!window.TryGetProperty("usedPercent", out var usedPercentElement) ||
            !usedPercentElement.TryGetDouble(out var usedPercent))
        {
            return;
        }

        TimeSpan? duration = null;
        if (window.TryGetProperty("windowDurationMins", out var durationElement) &&
            durationElement.TryGetDouble(out var durationMinutes))
        {
            duration = TimeSpan.FromMinutes(durationMinutes);
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetsAtElement) &&
            resetsAtElement.TryGetInt64(out var resetsAtSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtSeconds);
        }

        windows.Add(new AgentUsageWindow(
            $"{limitId}:{windowName}",
            usedPercent,
            duration,
            resetsAt));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
