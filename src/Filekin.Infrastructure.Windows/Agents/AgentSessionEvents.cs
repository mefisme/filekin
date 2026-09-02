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

/// <summary>The user action a provider request is waiting for.</summary>
public enum AgentSessionRequestKind
{
    Approval,
    UserInput,
    Unsupported,
}

/// <summary>One provider question inside a user-input request.</summary>
public sealed record AgentSessionQuestion(
    string Id,
    string Prompt,
    IReadOnlyList<string> Options);

/// <summary>
/// Provider-neutral control data attached only while a request is pending. The provider request id
/// never comes from model text; it is the JSON-RPC id Filekin received from the native provider.
/// </summary>
public sealed record AgentSessionPendingRequest(
    long Id,
    string Method,
    AgentSessionRequestKind Kind,
    IReadOnlyList<AgentSessionQuestion> Questions);

/// <summary>An explicit answer to a provider-owned pending request.</summary>
public sealed record AgentSessionRequestResponse(
    long RequestId,
    string? Decision = null,
    string? Answer = null);

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
    string? Detail = null,
    AgentSessionPendingRequest? PendingRequest = null);

/// <summary>
/// Replayable, in-memory event boundary for one provider conversation. Provider adapters publish
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

/// <summary>
/// The replayable observation boundary for the provider conversation Filekin owns. It remains useful
/// to headless relay tests and service consumers even though the app uses the provider's own terminal
/// instead of rendering a second transcript.
/// </summary>
public sealed record AgentSessionObservation(
    string NativeSessionId,
    AgentSessionEventFeed Events,
    DateTimeOffset StartedAt);
