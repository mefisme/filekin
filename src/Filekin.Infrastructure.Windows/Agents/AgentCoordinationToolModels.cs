using System.Text.Json.Serialization;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

public sealed record AgentToolIdentity(Guid ProjectId, AgentProvider Provider);

public sealed record AgentToolParticipantState(
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider Provider,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentConnectionState>))]
    AgentConnectionState ConnectionState,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentTurnState>))]
    AgentTurnState TurnState,
    double? MinimumRemainingPercent,
    DateTimeOffset? UsageObservedAt);

public sealed record AgentToolMessage(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider From,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider To,
    DateTimeOffset SentAt,
    string Text);

public sealed record AgentToolHandoff(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider From,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider To,
    DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentHandoffReason>))]
    AgentHandoffReason Reason,
    string Summary,
    string CompletedWork,
    string RemainingWork,
    string Verification,
    string Blockers,
    DateTimeOffset? AcceptedAt);

public sealed record AgentToolProjectState(
    Guid ProjectId,
    string FolderPath,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProjectStatus>))]
    AgentProjectStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider Caller,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
    AgentProvider? ActiveAgent,
    string? AttentionReason,
    IReadOnlyList<AgentToolParticipantState> Participants,
    IReadOnlyList<AgentToolMessage> Messages,
    AgentToolHandoff? PendingHandoff,
    AgentToolHandoff? LastHandoff);
