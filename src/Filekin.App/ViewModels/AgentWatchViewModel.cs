using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One line of what an agent is doing, for the watch screen. A line is replaced in place when the
/// provider revises it, because a streamed answer and a running command each arrive as many updates
/// to one fact rather than as many facts.
/// </summary>
public sealed class AgentWatchRowViewModel : ObservableObject
{
    private string _text;
    private string _detail;
    private bool _isWaiting;
    private bool _isFailure;

    internal AgentWatchRowViewModel(AgentSessionEvent source, string agentName)
    {
        Id = source.Id;
        At = source.At;
        Time = source.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        Who = Speaker(source, agentName);
        _text = Line(source);
        _detail = source.Detail ?? string.Empty;
        _isWaiting = source.Status == AgentSessionEventStatus.InProgress;
        _isFailure = source.Status is AgentSessionEventStatus.Failed
            or AgentSessionEventStatus.NeedsAttention;
    }

    /// <summary>The provider's own id for this fact. Reusing it revises this line.</summary>
    public string Id { get; }

    public DateTimeOffset At { get; }

    /// <summary>The clock time, so a long run can be read back in order.</summary>
    public string Time { get; }

    /// <summary>Who this line is from: the agent, the person, or the shell it ran a command in.</summary>
    public string Who { get; }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>Command output or a longer body, shown under the line when there is one.</summary>
    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public bool HasDetail => _detail.Length > 0;

    /// <summary>Whether this is still happening, so the screen can show it is not finished.</summary>
    public bool IsWaiting
    {
        get => _isWaiting;
        private set => SetProperty(ref _isWaiting, value);
    }

    /// <summary>Whether this went wrong. A failure that reads like ordinary progress is a trap.</summary>
    public bool IsFailure
    {
        get => _isFailure;
        private set => SetProperty(ref _isFailure, value);
    }

    internal void Revise(AgentSessionEvent source)
    {
        Text = Line(source);
        Detail = source.Detail ?? string.Empty;
        OnPropertyChanged(nameof(HasDetail));
        IsWaiting = source.Status == AgentSessionEventStatus.InProgress;
        IsFailure = source.Status is AgentSessionEventStatus.Failed
            or AgentSessionEventStatus.NeedsAttention;
    }

    /// <summary>
    /// The provider titles a tool line with the tool and a message line with the speaker. Only the
    /// person's own lines are titled "You", and that title is written by Filekin rather than by a
    /// provider, so it is the one label here that model text can never forge.
    /// </summary>
    private static string Speaker(AgentSessionEvent source, string agentName) => source.Kind switch
    {
        AgentSessionEventKind.Message when string.Equals(source.Title, "You", StringComparison.Ordinal) => "You",
        AgentSessionEventKind.Tool => "$",
        AgentSessionEventKind.Error => "Error",
        AgentSessionEventKind.Question => "Asks",
        _ => agentName,
    };

    /// <summary>
    /// What the line says. A tool line leads with the command it ran, because "ran a command" on its
    /// own tells a person watching nothing they did not already know.
    /// </summary>
    private static string Line(AgentSessionEvent source) =>
        source.Summary.Length > 0 ? source.Summary : source.Title;
}

/// <summary>
/// The watch screen for one agent: what it is doing as it does it, and a line to say something back.
/// </summary>
/// <remarks>
/// This exists because Codex cannot be read any other way while it works. Codex allows one client per
/// conversation, so while Filekin's App Server holds the thread its CLI cannot be opened, and the
/// person is left with a row that says "working" and nothing else. Claude has no such gap — attaching
/// is a second window on the one session — so nothing here is offered for Claude.
///
/// It is not a second transcript of a session somebody could already read, which is what DECISIONS.md
/// 2026-09-01 removed. Nothing is parsed off a screen and no input is synthesized: every line comes
/// from the App Server event stream Filekin is already given, and the reply goes back down the same
/// wire through <see cref="AgentRunService.SendPromptAsync"/>.
/// </remarks>
public sealed class AgentWatchViewModel : ObservableObject, IDisposable
{
    private readonly AgentSessionEventFeed _feed;
    private readonly Dictionary<string, AgentWatchRowViewModel> _rows = new(StringComparer.Ordinal);
    private readonly string _agentName;
    private string _draft = string.Empty;
    private string _status = string.Empty;
    private bool _isSending;
    private bool _holdsTheTurn;
    private bool _isDisposed;

    internal AgentWatchViewModel(
        Guid projectId,
        AgentProvider provider,
        AgentSessionObservation observation,
        bool holdsTheTurn)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ProjectId = projectId;
        Provider = provider;
        _agentName = AgentParticipantViewModel.ShortName(provider);
        _feed = observation.Events;
        _holdsTheTurn = holdsTheTurn;

        foreach (var item in _feed.Snapshot())
        {
            Take(item);
        }

        // The provider raises this off Filekin's UI thread, so the marshalling belongs to whoever
        // owns the screen rather than to this list.
        _feed.EventReceived += OnFeedEvent;
    }

    /// <summary>Raised for each provider event, on the provider's thread. The view marshals it.</summary>
    public event EventHandler<AgentSessionEvent>? EventReceived;

    public Guid ProjectId { get; }

    public AgentProvider Provider { get; }

    /// <summary>The heading: whose work this is.</summary>
    public string Title => $"{_agentName} · watching";

    public ObservableCollection<AgentWatchRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Whether there is nothing to show yet, so the screen can say so.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>What the screen says while there is nothing to show yet.</summary>
    public string EmptyText =>
        $"Nothing from {_agentName} yet. What it does next appears here as it happens.";

    /// <summary>What the person is typing back.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value))
            {
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    /// <summary>Whether this agent holds the turn. Nothing can be said to one that does not.</summary>
    public bool HoldsTheTurn
    {
        get => _holdsTheTurn;
        internal set
        {
            if (SetProperty(ref _holdsTheTurn, value))
            {
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(SendHint));
            }
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (SetProperty(ref _isSending, value))
            {
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public bool CanSend => !_isSending && _holdsTheTurn && _draft.Trim().Length > 0;

    /// <summary>Why the line cannot be sent, or what sending it does.</summary>
    public string SendHint => _holdsTheTurn
        ? $"Goes straight to the {_agentName} session that is running. It reads it in this turn."
        : $"{_agentName} is not holding the turn, so there is nothing running to say this to.";

    /// <summary>The last thing that went wrong here, or nothing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _status.Length > 0;

    /// <summary>
    /// Sends what is typed, through the caller's own send. The delegate is passed in so this type
    /// never reaches the run service itself and stays testable without one.
    /// </summary>
    public async Task SendAsync(Func<string, Task<string?>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        var text = _draft.Trim();
        if (_isSending || !_holdsTheTurn || text.Length == 0)
        {
            return;
        }

        IsSending = true;
        Status = string.Empty;
        try
        {
            if (await send(text).ConfigureAwait(true) is { } refusal)
            {
                Status = refusal;
                return;
            }

            // Cleared only once it has gone. A box emptied on a failed send loses what was typed.
            Draft = string.Empty;
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>Adds or revises one provider fact. Called on the UI thread by the view.</summary>
    public void Take(AgentSessionEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_rows.TryGetValue(item.Id, out var existing))
        {
            existing.Revise(item);
            return;
        }

        var row = new AgentWatchRowViewModel(item, _agentName);
        _rows.Add(item.Id, row);
        Rows.Add(row);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _feed.EventReceived -= OnFeedEvent;
    }

    private void OnFeedEvent(object? sender, AgentSessionEvent item) => EventReceived?.Invoke(this, item);
}
