using System.ComponentModel;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using ModelContextProtocol.Server;

namespace Filekin.Mcp;

[McpServerToolType]
public sealed class FilekinAgentTools(AgentCoordinationToolService service)
{
    [McpServerTool(
        Name = "filekin_clock_in",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Attach this agent's native session to its fixed Filekin project. Never pass credentials or secret values.")]
    public Task<AgentToolProjectState> ClockInAsync(
        [Description("The native Codex or Claude Code session identifier; never a credential or secret.")]
        string nativeSessionId,
        CancellationToken cancellationToken) =>
        service.ClockInAsync(nativeSessionId, cancellationToken);

    [McpServerTool(
        Name = "filekin_read_state",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Read coordination state for this process's fixed Filekin project and agent identity.")]
    public Task<AgentToolProjectState> ReadStateAsync(CancellationToken cancellationToken) =>
        service.ReadStateAsync(cancellationToken);

    [McpServerTool(
        Name = "filekin_send_message",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Send a non-secret coordination message to the other agent in this Filekin project.")]
    public Task<AgentToolProjectState> SendMessageAsync(
        [Description("Message text. Do not include credentials, tokens, or other secrets.")]
        string text,
        CancellationToken cancellationToken) =>
        service.SendMessageAsync(text, cancellationToken);

    [McpServerTool(
        Name = "filekin_submit_handoff",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Record a handoff to the other agent. This does not release the working-tree lease; Filekin releases it only after the provider process stops.")]
    public Task<AgentToolProjectState> SubmitHandoffAsync(
        [Description("One of: work_completed, usage_threshold, user_requested.")]
        string reason,
        [Description("Concise description of the handoff.")]
        string summary,
        [Description("Work that is already complete.")]
        string completedWork,
        [Description("Work the recipient should continue.")]
        string remainingWork,
        [Description("Checks already run and their outcomes.")]
        string verification,
        [Description("Known blockers, or an empty string.")]
        string blockers,
        CancellationToken cancellationToken) =>
        service.SubmitHandoffAsync(
            ParseReason(reason),
            summary,
            completedWork,
            remainingWork,
            verification,
            blockers,
            cancellationToken);

    [McpServerTool(
        Name = "filekin_accept_handoff",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Accept the pending handoff after Filekin has assigned this agent the working-tree lease.")]
    public Task<AgentToolProjectState> AcceptHandoffAsync(CancellationToken cancellationToken) =>
        service.AcceptHandoffAsync(cancellationToken);

    [McpServerTool(
        Name = "filekin_report_blocked",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Report that this agent cannot continue. Reporting does not release the working-tree lease.")]
    public Task<AgentToolProjectState> ReportBlockedAsync(
        [Description("The concrete blocking condition. Do not include credentials or secrets.")]
        string reason,
        CancellationToken cancellationToken) =>
        service.ReportBlockedAsync(reason, cancellationToken);

    [McpServerTool(
        Name = "filekin_report_usage_limit",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Record a provider-native subscription usage-limit callback. This does not release the working-tree lease.")]
    public Task<AgentToolProjectState> ReportUsageLimitAsync(
        [Description("The native session identifier supplied by the provider lifecycle event; never a credential or secret.")]
        string nativeSessionId,
        CancellationToken cancellationToken) =>
        service.ReportUsageLimitAsync(nativeSessionId, cancellationToken);

    [McpServerTool(
        Name = "filekin_report_completed",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Report completion. This is not proof that the provider process stopped, so the working-tree lease remains held.")]
    public Task<AgentToolProjectState> ReportCompletedAsync(CancellationToken cancellationToken) =>
        service.ReportCompletedAsync(cancellationToken);

    public static AgentHandoffReason ParseReason(string reason) => reason?.ToLowerInvariant() switch
    {
        "work_completed" => AgentHandoffReason.WorkCompleted,
        "usage_threshold" => AgentHandoffReason.UsageThreshold,
        "user_requested" => AgentHandoffReason.UserRequested,
        _ => throw new ArgumentException(
            "Handoff reason must be 'work_completed', 'usage_threshold', or 'user_requested'.",
            nameof(reason)),
    };
}
