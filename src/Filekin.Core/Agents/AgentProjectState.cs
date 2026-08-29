using System.Collections.ObjectModel;

namespace Filekin.Core.Agents;

/// <summary>
/// Immutable snapshot of one coordinated folder. Runtime adapters and WPF consume this state; they
/// do not own its transitions.
/// </summary>
public sealed class AgentProjectState
{
    internal AgentProjectState(
        Guid id,
        string folderPath,
        AgentProjectStatus status,
        IDictionary<AgentProvider, AgentParticipant> participants,
        WorkingTreeLease? lease,
        AgentHandoffReason? requestedHandoffReason,
        AgentHandoff? pendingHandoff,
        AgentHandoff? lastHandoff,
        IEnumerable<AgentMessage> messages,
        string? attentionReason)
    {
        Id = id;
        FolderPath = folderPath;
        Status = status;
        Participants = new ReadOnlyDictionary<AgentProvider, AgentParticipant>(
            new Dictionary<AgentProvider, AgentParticipant>(participants));
        Lease = lease;
        RequestedHandoffReason = requestedHandoffReason;
        PendingHandoff = pendingHandoff;
        LastHandoff = lastHandoff;
        Messages = Array.AsReadOnly(messages.ToArray());
        AttentionReason = attentionReason;
    }

    public Guid Id { get; }

    public string FolderPath { get; }

    public AgentProjectStatus Status { get; }

    public IReadOnlyDictionary<AgentProvider, AgentParticipant> Participants { get; }

    public WorkingTreeLease? Lease { get; }

    public AgentHandoffReason? RequestedHandoffReason { get; }

    public AgentHandoff? PendingHandoff { get; }

    public AgentHandoff? LastHandoff { get; }

    public IReadOnlyList<AgentMessage> Messages { get; }

    public string? AttentionReason { get; }

    public AgentProvider? ActiveAgent => Lease?.Owner;

    public AgentParticipant Participant(AgentProvider provider) => Participants[provider];
}
