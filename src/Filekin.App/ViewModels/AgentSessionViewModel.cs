using System.Collections.ObjectModel;
using System.Windows.Threading;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One persistent read-only task for the exact native agent session Filekin opened. Provider events
/// cross an immutable neutral boundary; project messages and handoffs are merged as coordination
/// facts. This surface deliberately has no reply or approval operation.
/// </summary>
public sealed class AgentSessionViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AgentSessionEventFeed? _feed;
    private bool _disposed;
    private bool _isSelected;
    private string _turn = string.Empty;
    private string _usage = string.Empty;

    public AgentSessionViewModel(
        AgentProjectState project,
        AgentProvider provider,
        string nativeSessionId,
        AgentSessionObservation? observation,
        Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSessionId);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ProjectId = project.Id;
        Provider = provider;
        NativeSessionId = nativeSessionId;
        ProviderName = AgentParticipantViewModel.DisplayName(provider);
        Title = $"{ProviderName} session";
        FolderPath = project.FolderPath;
        _dispatcher = dispatcher;

        if (observation is not null &&
            string.Equals(observation.NativeSessionId, nativeSessionId, StringComparison.Ordinal))
        {
            _feed = observation.Events;
            _feed.EventReceived += OnEventReceived;
            foreach (var sessionEvent in _feed.Snapshot())
            {
                Upsert(sessionEvent);
            }
        }
        else
        {
            Upsert(new AgentSessionEvent(
                $"session-unavailable:{nativeSessionId}",
                DateTimeOffset.Now,
                AgentSessionEventKind.Status,
                AgentSessionEventStatus.Information,
                "Live provider stream unavailable",
                "This session was not started by the current Filekin window.",
                "Coordination messages and handoffs are still shown. Reopen the provider's own session UI for its live activity."));
        }

        Update(project);
    }

    public Guid ProjectId { get; }

    public AgentProvider Provider { get; }

    public string NativeSessionId { get; }

    public string ProviderName { get; }

    public string Title { get; }

    public string FolderPath { get; }

    public ObservableCollection<AgentSessionEventViewModel> Events { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Turn
    {
        get => _turn;
        private set => SetProperty(ref _turn, value);
    }

    public string Usage
    {
        get => _usage;
        private set => SetProperty(ref _usage, value);
    }

    public void Update(AgentProjectState project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Id != ProjectId)
        {
            return;
        }

        var participant = project.Participant(Provider);
        var participantView = new AgentParticipantViewModel(participant, project.ActiveAgent == Provider);
        Turn = participantView.State;
        Usage = participantView.Usage;

        // Coordination records cannot identify a superseded provider session. Only add new project
        // facts while this exact session is still the participant's current native identity.
        if (participant.NativeSessionId is { } currentNativeSessionId &&
            !string.Equals(currentNativeSessionId, NativeSessionId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var message in project.Messages.Where(message => message.From == Provider || message.To == Provider))
        {
            Upsert(new AgentSessionEvent(
                $"message:{message.Id:D}",
                message.SentAt,
                AgentSessionEventKind.Message,
                AgentSessionEventStatus.Completed,
                $"{AgentParticipantViewModel.DisplayName(message.From)} → {AgentParticipantViewModel.DisplayName(message.To)}",
                message.Text));
        }

        if (project.LastHandoff is { } last && (last.From == Provider || last.To == Provider))
        {
            Upsert(HandoffEvent(last, pending: false));
        }

        if (project.PendingHandoff is { } pending && (pending.From == Provider || pending.To == Provider))
        {
            Upsert(HandoffEvent(pending, pending: true));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_feed is not null)
        {
            _feed.EventReceived -= OnEventReceived;
        }
    }

    private void OnEventReceived(object? sender, AgentSessionEvent sessionEvent)
    {
        if (_dispatcher.CheckAccess())
        {
            Upsert(sessionEvent);
            return;
        }

        _ = _dispatcher.BeginInvoke(() => Upsert(sessionEvent));
    }

    private void Upsert(AgentSessionEvent sessionEvent)
    {
        var row = new AgentSessionEventViewModel(sessionEvent);
        var existing = Events
            .Select((candidate, index) => (candidate, index))
            .FirstOrDefault(pair => string.Equals(pair.candidate.Id, sessionEvent.Id, StringComparison.Ordinal));
        if (existing.candidate is not null)
        {
            Events[existing.index] = row;
            return;
        }

        var insertAt = 0;
        while (insertAt < Events.Count && Events[insertAt].At <= sessionEvent.At)
        {
            insertAt++;
        }

        Events.Insert(insertAt, row);
    }

    private static AgentSessionEvent HandoffEvent(AgentHandoff handoff, bool pending)
    {
        var detail = string.Join(
            Environment.NewLine,
            new[]
            {
                Value("Completed", handoff.CompletedWork),
                Value("Remaining", handoff.RemainingWork),
                Value("Verification", handoff.Verification),
                Value("Blockers", handoff.Blockers),
            }.Where(value => value is not null));
        return new AgentSessionEvent(
            $"handoff:{handoff.Id:D}",
            handoff.CreatedAt,
            AgentSessionEventKind.Handoff,
            pending ? AgentSessionEventStatus.InProgress : AgentSessionEventStatus.Completed,
            $"{AgentParticipantViewModel.DisplayName(handoff.From)} → {AgentParticipantViewModel.DisplayName(handoff.To)}",
            handoff.Summary,
            detail.Length == 0 ? null : detail);
    }

    private static string? Value(string name, string value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{name}: {value}";
}
