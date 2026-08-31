using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>Plain display values for one immutable provider or coordination event.</summary>
public sealed class AgentSessionEventViewModel
{
    public AgentSessionEventViewModel(AgentSessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        Id = sessionEvent.Id;
        At = sessionEvent.At;
        Kind = sessionEvent.Kind;
        Status = sessionEvent.Status;
        Title = sessionEvent.Title;
        Summary = sessionEvent.Summary;
        Detail = sessionEvent.Detail;
    }

    public string Id { get; }

    public DateTimeOffset At { get; }

    public string Time => At.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    public AgentSessionEventKind Kind { get; }

    public string KindText => Kind switch
    {
        AgentSessionEventKind.Response => "REPLY",
        AgentSessionEventKind.Tool => "TOOL",
        AgentSessionEventKind.Question => "QUESTION",
        AgentSessionEventKind.Error => "ERROR",
        AgentSessionEventKind.Message => "MESSAGE",
        AgentSessionEventKind.Handoff => "HANDOFF",
        _ => "STATUS",
    };

    public AgentSessionEventStatus Status { get; }

    public string Title { get; }

    public string Summary { get; }

    public string? Detail { get; }

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool IsFailed => Status == AgentSessionEventStatus.Failed;

    public bool NeedsAttention => Status == AgentSessionEventStatus.NeedsAttention;

    public bool IsInProgress => Status == AgentSessionEventStatus.InProgress;
}
