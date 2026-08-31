using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Converts the documented Codex App Server item/turn/request stream into provider-neutral immutable
/// snapshots. It deliberately omits raw reasoning and experimental process events.
/// </summary>
internal sealed class CodexAgentSessionEventMapper
{
    private const int MaximumDetailLength = 16_000;
    private readonly Dictionary<string, StringBuilder> _commandOutput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StringBuilder> _responseText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _startedAt = new(StringComparer.Ordinal);

    public AgentSessionEvent? MapNotification(
        CodexAppServerNotification notification,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification.Method switch
        {
            "item/started" => MapItem(notification.Parameters, observedAt, completed: false),
            "item/completed" => MapItem(notification.Parameters, observedAt, completed: true),
            "item/agentMessage/delta" => MapResponseDelta(notification.Parameters, observedAt),
            "item/commandExecution/outputDelta" => MapCommandDelta(notification.Parameters, observedAt),
            "turn/started" => MapTurn(notification.Parameters, observedAt, completed: false),
            "turn/completed" => MapTurn(notification.Parameters, observedAt, completed: true),
            "serverRequest/resolved" => MapResolvedRequest(notification.Parameters, observedAt),
            "error" => MapError(notification.Parameters, observedAt),
            "warning" or "configWarning" => MapWarning(notification.Parameters, observedAt),
            _ => null,
        };
    }

    public static AgentSessionEvent MapRequest(CodexAppServerRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = request.Method.Contains("requestApproval", StringComparison.Ordinal)
            ? "Approval needed"
            : "Codex needs your input";
        var summary = ReadString(request.Parameters, "reason")
            ?? ReadString(request.Parameters, "message")
            ?? FriendlyRequestName(request.Method);
        var details = new List<string>();
        if (ReadDisplayText(request.Parameters, "command") is { Length: > 0 } command)
        {
            details.Add(command);
        }

        if (ReadString(request.Parameters, "cwd") is { Length: > 0 } workingDirectory)
        {
            details.Add(workingDirectory);
        }

        details.Add("Answering in Filekin is not built yet. Use the provider's own session UI, or stop the agent here.");
        return new AgentSessionEvent(
            $"codex:request:{request.Id.ToString(CultureInfo.InvariantCulture)}",
            observedAt,
            AgentSessionEventKind.Question,
            AgentSessionEventStatus.NeedsAttention,
            title,
            summary,
            string.Join(Environment.NewLine, details));
    }

    private AgentSessionEvent? MapItem(JsonElement parameters, DateTimeOffset observedAt, bool completed)
    {
        if (!parameters.TryGetProperty("item", out var item) ||
            item.ValueKind != JsonValueKind.Object ||
            ReadString(item, "id") is not { Length: > 0 } itemId ||
            ReadString(item, "type") is not { Length: > 0 } type)
        {
            return null;
        }

        if (!completed)
        {
            _startedAt.TryAdd(itemId, observedAt);
        }

        var at = _startedAt.GetValueOrDefault(itemId, observedAt);
        return type switch
        {
            "agentMessage" => MapAgentMessage(itemId, item, at, completed),
            "plan" => MapPlan(itemId, item, at, completed),
            "commandExecution" => MapCommand(itemId, item, at, completed),
            "fileChange" => MapFileChange(itemId, item, at, completed),
            "mcpToolCall" or "dynamicToolCall" or "collabToolCall" =>
                MapToolCall(itemId, type, item, at, completed),
            "webSearch" => MapWebSearch(itemId, item, at, completed),
            "imageView" => MapImageView(itemId, item, at, completed),
            "enteredReviewMode" or "exitedReviewMode" => MapReview(itemId, type, item, at, completed),
            "contextCompaction" => new AgentSessionEvent(
                $"codex:item:{itemId}",
                at,
                AgentSessionEventKind.Status,
                completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
                "Conversation context",
                completed ? "Context was compacted." : "Compacting context…"),
            _ => null,
        };
    }

    private AgentSessionEvent MapAgentMessage(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed)
    {
        var text = ReadString(item, "text");
        if (text is { Length: > 0 })
        {
            _responseText[itemId] = new StringBuilder(text);
        }

        var current = text ?? _responseText.GetValueOrDefault(itemId)?.ToString();
        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Response,
            completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
            "Codex",
            Limit(current) ?? (completed ? "Response finished." : "Responding…"));
    }

    private static AgentSessionEvent MapPlan(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed) =>
        new(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Response,
            completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
            "Plan",
            Limit(ReadString(item, "text")) ?? (completed ? "Plan finished." : "Planning…"));

    private AgentSessionEvent MapCommand(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed)
    {
        var command = ReadDisplayText(item, "command") ?? "Command";
        _commands[itemId] = command;
        var status = ReadString(item, "status") ?? (completed ? "completed" : "running");
        var output = ReadString(item, "aggregatedOutput")
            ?? _commandOutput.GetValueOrDefault(itemId)?.ToString();
        var detail = JoinDetail(command, output);
        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Tool,
            EventStatus(status, completed),
            "Command",
            completed ? $"Command {FriendlyStatus(status)}." : "Running command…",
            Limit(detail));
    }

    private static AgentSessionEvent MapFileChange(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed)
    {
        var status = ReadString(item, "status") ?? (completed ? "completed" : "in progress");
        var changes = new List<string>();
        if (item.TryGetProperty("changes", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in changesElement.EnumerateArray())
            {
                var path = ReadString(change, "path");
                var kind = ReadString(change, "kind");
                if (path is { Length: > 0 })
                {
                    changes.Add(kind is { Length: > 0 } ? $"{kind}: {path}" : path);
                }
            }
        }

        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Tool,
            EventStatus(status, completed),
            "File changes",
            completed ? $"File changes {FriendlyStatus(status)}." : "Preparing file changes…",
            changes.Count == 0 ? null : Limit(string.Join(Environment.NewLine, changes)));
    }

    private static AgentSessionEvent MapToolCall(
        string itemId,
        string type,
        JsonElement item,
        DateTimeOffset at,
        bool completed)
    {
        var tool = ReadString(item, "tool") ?? FriendlyItemType(type);
        var server = ReadString(item, "server");
        var status = ReadString(item, "status") ?? (completed ? "completed" : "in progress");
        var title = server is { Length: > 0 } ? $"{server} · {tool}" : tool;
        var detail = new List<string>();
        AddJsonDetail(detail, item, "arguments");
        AddJsonDetail(detail, item, "result");
        AddJsonDetail(detail, item, "error");
        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Tool,
            EventStatus(status, completed),
            title,
            completed ? $"Tool {FriendlyStatus(status)}." : "Using tool…",
            detail.Count == 0 ? null : Limit(string.Join(Environment.NewLine, detail)));
    }

    private static AgentSessionEvent MapWebSearch(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed) =>
        new(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Tool,
            completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
            "Web search",
            ReadString(item, "query") ?? (completed ? "Search finished." : "Searching…"));

    private static AgentSessionEvent MapImageView(
        string itemId,
        JsonElement item,
        DateTimeOffset at,
        bool completed) =>
        new(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Tool,
            completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
            "Viewed image",
            ReadString(item, "path") ?? "Image");

    private static AgentSessionEvent MapReview(
        string itemId,
        string type,
        JsonElement item,
        DateTimeOffset at,
        bool completed) =>
        new(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Response,
            completed ? AgentSessionEventStatus.Completed : AgentSessionEventStatus.InProgress,
            type == "enteredReviewMode" ? "Review started" : "Review",
            Limit(ReadString(item, "review")) ?? (completed ? "Review finished." : "Reviewing…"));

    private AgentSessionEvent? MapResponseDelta(JsonElement parameters, DateTimeOffset observedAt)
    {
        if (ReadString(parameters, "itemId") is not { Length: > 0 } itemId ||
            ReadString(parameters, "delta") is not { } delta)
        {
            return null;
        }

        var builder = _responseText.GetValueOrDefault(itemId);
        if (builder is null)
        {
            builder = new StringBuilder();
            _responseText[itemId] = builder;
        }

        builder.Append(delta);
        var at = _startedAt.GetValueOrDefault(itemId, observedAt);
        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            at,
            AgentSessionEventKind.Response,
            AgentSessionEventStatus.InProgress,
            "Codex",
            Limit(builder.ToString()) ?? "Responding…");
    }

    private AgentSessionEvent? MapCommandDelta(JsonElement parameters, DateTimeOffset observedAt)
    {
        if (ReadString(parameters, "itemId") is not { Length: > 0 } itemId ||
            ReadString(parameters, "delta") is not { } delta)
        {
            return null;
        }

        var builder = _commandOutput.GetValueOrDefault(itemId);
        if (builder is null)
        {
            builder = new StringBuilder();
            _commandOutput[itemId] = builder;
        }

        builder.Append(delta);
        return new AgentSessionEvent(
            $"codex:item:{itemId}",
            _startedAt.GetValueOrDefault(itemId, observedAt),
            AgentSessionEventKind.Tool,
            AgentSessionEventStatus.InProgress,
            "Command",
            "Running command…",
            Limit(JoinDetail(_commands.GetValueOrDefault(itemId) ?? "Command", builder.ToString())));
    }

    private static AgentSessionEvent? MapTurn(
        JsonElement parameters,
        DateTimeOffset observedAt,
        bool completed)
    {
        if (!parameters.TryGetProperty("turn", out var turn) ||
            ReadString(turn, "id") is not { Length: > 0 } turnId)
        {
            return null;
        }

        var status = ReadString(turn, "status") ?? (completed ? "completed" : "in progress");
        string? error = null;
        if (turn.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            error = ReadString(errorElement, "message");
        }

        return new AgentSessionEvent(
            $"codex:turn:{turnId}",
            observedAt,
            AgentSessionEventKind.Status,
            EventStatus(status, completed),
            "Turn",
            completed ? $"Turn {FriendlyStatus(status)}." : "Turn started.",
            Limit(error));
    }

    private static AgentSessionEvent? MapResolvedRequest(JsonElement parameters, DateTimeOffset observedAt)
    {
        if (!parameters.TryGetProperty("requestId", out var requestId))
        {
            return null;
        }

        var id = requestId.ValueKind switch
        {
            JsonValueKind.String => requestId.GetString(),
            JsonValueKind.Number => requestId.GetRawText(),
            _ => null,
        };
        return id is null
            ? null
            : new AgentSessionEvent(
                $"codex:request:{id}",
                observedAt,
                AgentSessionEventKind.Question,
                AgentSessionEventStatus.Completed,
                "Request resolved",
                "The request is no longer pending.");
    }

    private static AgentSessionEvent? MapError(JsonElement parameters, DateTimeOffset observedAt)
    {
        var error = parameters.TryGetProperty("error", out var nested) ? nested : parameters;
        var message = ReadString(error, "message");
        return message is null
            ? null
            : new AgentSessionEvent(
                $"codex:error:{Guid.NewGuid():N}",
                observedAt,
                AgentSessionEventKind.Error,
                AgentSessionEventStatus.Failed,
                "Codex error",
                message);
    }

    private static AgentSessionEvent? MapWarning(JsonElement parameters, DateTimeOffset observedAt)
    {
        var message = ReadString(parameters, "message") ?? ReadString(parameters, "summary");
        return message is null
            ? null
            : new AgentSessionEvent(
                $"codex:warning:{Guid.NewGuid():N}",
                observedAt,
                AgentSessionEventKind.Status,
                AgentSessionEventStatus.Information,
                "Codex warning",
                message,
                Limit(ReadString(parameters, "details")));
    }

    private static AgentSessionEventStatus EventStatus(string status, bool completed)
    {
        if (!completed || status.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            return AgentSessionEventStatus.InProgress;
        }

        return status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("declin", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("interrupt", StringComparison.OrdinalIgnoreCase)
            ? AgentSessionEventStatus.Failed
            : AgentSessionEventStatus.Completed;
    }

    private static string FriendlyStatus(string status) =>
        status.Trim().Replace("inProgress", "in progress", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    private static string FriendlyItemType(string type) => type switch
    {
        "mcpToolCall" => "MCP tool",
        "dynamicToolCall" => "Tool",
        "collabToolCall" => "Agent coordination",
        _ => "Tool",
    };

    private static string FriendlyRequestName(string method) => method switch
    {
        "item/tool/requestUserInput" => "Codex asked a question.",
        "item/fileChange/requestApproval" => "Codex is waiting for file-change approval.",
        "item/commandExecution/requestApproval" => "Codex is waiting for command approval.",
        "item/permissions/requestApproval" => "Codex is waiting for permission.",
        _ => $"Codex is waiting on {method}.",
    };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadDisplayText(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Array => string.Join(" ", property.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())),
            _ => null,
        };
    }

    private static void AddJsonDetail(List<string> details, JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        details.Add($"{propertyName}: {property.GetRawText()}");
    }

    private static string? JoinDetail(string first, string? second) =>
        second is { Length: > 0 } ? $"{first}{Environment.NewLine}{second}" : first;

    private static string? Limit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaximumDetailLength
            ? trimmed
            : $"…{trimmed[^MaximumDetailLength..]}";
    }
}
