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
    [Description("Report that this agent is here. Call it first: Filekin does not know you are here until you do, and it will not give you the turn. Filekin already knows which session this is, so no identifier is passed.")]
    public Task<AgentToolProjectState> ClockInAsync(CancellationToken cancellationToken) =>
        service.ClockInAsync(cancellationToken);

    [McpServerTool(
        Name = "filekin_read_state",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Read this project's coordination state: who holds the turn, what each agent has left, messages, and whether Filekin has asked you to hand over or stop. Check it again as you work.")]
    public Task<AgentToolProjectState> ReadStateAsync(CancellationToken cancellationToken) =>
        service.ReadStateAsync(cancellationToken);

    [McpServerTool(
        Name = "filekin_send_message",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Send a non-secret coordination message to the other agent. It does not start that agent: only a handoff brings it in, so never wait on a reply from an agent that is not running.")]
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
    [Description("Hand the work over: call it when your part is done, or when the state says Filekin asked you to. Write what you did, what is left, and how you checked it. Then end your turn — the other agent is not running, and Filekin starts it and moves the turn only after this session stops.")]
    public Task<AgentToolProjectState> SubmitHandoffAsync(
        [Description("One of: work_completed, usage_threshold, user_requested. When Filekin asked for the handoff, its own reason is recorded instead.")]
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
    [Description("Report that you cannot continue and need the user, instead of guessing. The turn stays with you, because a question is not proof that this session stopped.")]
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
    [Description("Report that the user's objective is done. Filekin closes the project after this session stops; until then the turn stays with you.")]
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
