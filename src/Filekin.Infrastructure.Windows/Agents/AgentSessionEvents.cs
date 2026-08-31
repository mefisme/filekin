using System.Collections.ObjectModel;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>The kind of fact shown in a read-only Agent Session task surface.</summary>
public enum AgentSessionEventKind
{
    Response,
    Tool,
    Question,
    Error,
    Message,
    Handoff,
    Status,
}

/// <summary>Provider-neutral lifecycle for one displayed session fact.</summary>
public enum AgentSessionEventStatus
{
    Information,
    InProgress,
    Completed,
    Failed,
    NeedsAttention,
}

/// <summary>
/// One immutable view of a provider event. Reusing an <see cref="Id"/> replaces the prior view, so
/// streamed text and running tools update one row instead of producing a row for every delta.
/// </summary>
public sealed record AgentSessionEvent(
    string Id,
    DateTimeOffset At,
    AgentSessionEventKind Kind,
    AgentSessionEventStatus Status,
    string Title,
    string Summary,
    string? Detail = null);

/// <summary>
/// Replayable, in-memory event boundary for one exact native session. Provider adapters publish
/// immutable snapshots here; WPF can take a snapshot and then observe replacements without knowing
/// anything about JSON-RPC, CLI processes, or provider payloads.
/// </summary>
public sealed class AgentSessionEventFeed
{
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private readonly List<AgentSessionEvent> _items = [];
    private readonly object _sync = new();

    public event EventHandler<AgentSessionEvent>? EventReceived;

    public IReadOnlyList<AgentSessionEvent> Snapshot()
    {
        lock (_sync)
        {
            return new ReadOnlyCollection<AgentSessionEvent>(_items.ToArray());
        }
    }

    internal void Publish(AgentSessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionEvent.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionEvent.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionEvent.Summary);

        lock (_sync)
        {
            if (_indexes.TryGetValue(sessionEvent.Id, out var index))
            {
                _items[index] = sessionEvent;
            }
            else
            {
                _indexes.Add(sessionEvent.Id, _items.Count);
                _items.Add(sessionEvent);
            }
        }

        EventReceived?.Invoke(this, sessionEvent);
    }
}

/// <summary>The live presentation boundary for the exact native session Filekin started.</summary>
public sealed record AgentSessionObservation(
    string NativeSessionId,
    AgentSessionEventFeed Events);
