using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>Plain display values for one immutable provider or coordination event.</summary>
public sealed class AgentSessionEventViewModel
{
    private const int MaximumDetailLines = 6;
    private const int MaximumDetailLength = 600;

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

    /// <summary>
    /// The clock time, to the second. A live run puts many events in the same minute, and without
    /// seconds a list that really is in order does not look like one.
    /// </summary>
    public string Time => At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

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

    /// <summary>
    /// The first few lines of the detail. A long tool result or a whole handoff pushes every other
    /// row off the screen, and this surface is for following what is happening, not for reading a
    /// transcript. The full text stays in the tooltip.
    /// </summary>
    public string? DetailPreview
    {
        get
        {
            if (Detail is not { Length: > 0 } detail)
            {
                return null;
            }

            var lines = detail.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);
            var preview = string.Join(Environment.NewLine, lines.Take(MaximumDetailLines));
            if (preview.Length > MaximumDetailLength)
            {
                preview = preview[..MaximumDetailLength];
            }

            return preview.Length >= detail.TrimEnd().Length
                ? preview
                : preview.TrimEnd() + " …";
        }
    }

    public bool IsFailed => Status == AgentSessionEventStatus.Failed;

    public bool NeedsAttention => Status == AgentSessionEventStatus.NeedsAttention;

    public bool IsInProgress => Status == AgentSessionEventStatus.InProgress;
}
