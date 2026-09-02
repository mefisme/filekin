using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One agent row in the <c>/agents</c> control room: whether the tool is running, what the job is
/// doing, what it has left, and the controls that belong to it. A row is updated in place rather
/// than rebuilt, so a list somebody has open does not close under them.
/// </summary>
/// <remarks>
/// A row states two facts and never merges them (owner decision, 2026-09-01).
/// <see cref="Connection"/> answers "is the tool running right now", and <see cref="Work"/> answers
/// "what about the job". One mixed column had to leave one of them out, so a finished or paused
/// agent read as though it were still connected. Running is running: it does not matter whether
/// Filekin started the session, is watching it, or was closed while it carried on.
/// </remarks>
public sealed class AgentParticipantViewModel : ObservableObject
{
    /// <summary>The first entry of both lists: leave the choice to the tool's own configuration.</summary>
    public const string ToolDefault = "Default";

    private readonly Action<AgentParticipantViewModel>? _chosen;
    private IReadOnlyList<AgentModelChoice> _models = [];
    private bool _applyingState;
    private bool _holdsTheTurn;
    private bool _isSessionOpenHere;
    private bool _isCliTabOpenHere;
    private AgentSessionLiveness _unwatchedLiveness;
    private bool _hasRunInThisWindow;
    private bool _jobIsFinished;
    private bool _hasWorkedOnObjective;
    private bool _isChoosing;
    private string _selectedEffort = ToolDefault;
    private string _selectedModel = ToolDefault;
    private string _usage = string.Empty;
    private string? _nativeSessionId;
    private AgentConnectionState _connectionState = AgentConnectionState.Offline;
    private AgentTurnState _turnState = AgentTurnState.ClockedOut;

    public AgentParticipantViewModel(
        AgentParticipant participant,
        bool holdsTheTurn,
        bool jobIsFinished = false,
        Action<AgentParticipantViewModel>? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Provider = participant.Provider;
        Name = DisplayName(participant.Provider);
        _chosen = chosen;
        Update(participant, holdsTheTurn, jobIsFinished);
    }

    public AgentProvider Provider { get; }

    public string Name { get; }

    /// <summary>
    /// Whether the tool is running right now. This is never left out: an agent that has gone must
    /// say so, or the job state beside it reads as though the tool were still there.
    /// </summary>
    public string Connection =>
        !_isSessionOpenHere && !_isCliTabOpenHere &&
        _unwatchedLiveness == AgentSessionLiveness.Unknown
            ? "No answer"
            : !IsRunningNow
                ? "Not connected"
                : _connectionState == AgentConnectionState.Unavailable
                    ? "No answer"
                    : "Running";

    public string ConnectionHelpText =>
        !_isSessionOpenHere && !_isCliTabOpenHere &&
        _unwatchedLiveness == AgentSessionLiveness.Unknown
            ? $"Couldn't check whether {Name} is still running."
            : Connection;

    /// <summary>What the job is doing for this agent, which is a different fact from running.</summary>
    public string Work
    {
        get
        {
            // Only the agent that finished the work says Done. A finished job is a fact about the
            // project, and painting it across every row made an agent that never took a turn claim
            // the finish (owner, 2026-09-01). The heading sentence says the job is finished.
            if (_turnState == AgentTurnState.Completed)
            {
                return "Done";
            }

            return _turnState switch
            {
                AgentTurnState.Active => "Working",
                AgentTurnState.HandoffRequested => "Handing over",
                AgentTurnState.Blocked or AgentTurnState.NeedsAttention => "Needs you",
                AgentTurnState.CompletionReported => "Finishing",
                AgentTurnState.StopRequested => "Stopping",

                // Being here with no turn and waiting for one are the same thing to read: this
                // agent is not the one working. Clocked out while nothing runs is a stop, but only
                // for an agent that has actually taken a turn on this objective.
                _ => IsRunningNow
                    ? "Waiting"
                    : _hasWorkedOnObjective
                        ? "Stopped"
                        : "Not started",
            };
        }
    }

    /// <summary>
    /// Whether a session for this agent is alive: driven by Filekin, open in a terminal tab here, or
    /// carried on by the tool itself after Filekin was closed. All three are running.
    /// </summary>
    /// <remarks>
    /// This is asked of the session, never of the stored connection state. A saved "connected" is a
    /// memory of a window that may be gone — Filekin can be closed without an agent being told — so
    /// only a session Filekin holds, a tab it opened, or a tool that answers for itself counts.
    /// </remarks>
    public bool IsRunningNow =>
        _isSessionOpenHere ||
        _isCliTabOpenHere ||
        _unwatchedLiveness == AgentSessionLiveness.Running;

    /// <summary>How much of each subscription window is left, or that it is unknown.</summary>
    public string Usage
    {
        get => _usage;
        private set => SetProperty(ref _usage, value);
    }

    public bool HoldsTheTurn
    {
        get => _holdsTheTurn;
        private set
        {
            if (SetProperty(ref _holdsTheTurn, value))
            {
                ShowState();
            }
        }
    }

    public string? NativeSessionId
    {
        get => _nativeSessionId;
        private set
        {
            if (SetProperty(ref _nativeSessionId, value))
            {
                ShowState();
            }
        }
    }

    /// <summary>
    /// Whether the tool is running this agent's session while Filekin is not driving it. It happens
    /// whenever Filekin is closed and reopened, because a Claude background session outlives the
    /// window. Only the tool knows, so only the tool can set this.
    /// </summary>
    public AgentSessionLiveness UnwatchedLiveness
    {
        get => _unwatchedLiveness;
        set
        {
            if (SetProperty(ref _unwatchedLiveness, value))
            {
                ShowState();
            }
        }
    }

    /// <summary>
    /// Whether this Filekin window owns the tool process for this conversation. Codex cannot be
    /// resumed into a second client while this is true, because Filekin's App Server holds it.
    /// </summary>
    public bool IsSessionOpenHere
    {
        get => _isSessionOpenHere;
        set
        {
            if (SetProperty(ref _isSessionOpenHere, value))
            {
                if (value)
                {
                    _unwatchedLiveness = AgentSessionLiveness.NotRunning;
                }

                ShowState();
            }
        }
    }

    /// <summary>Whether this agent's CLI is open in one of this window's terminal tabs.</summary>
    public bool IsCliTabOpenHere
    {
        get => _isCliTabOpenHere;
        set
        {
            if (SetProperty(ref _isCliTabOpenHere, value))
            {
                if (value)
                {
                    _unwatchedLiveness = AgentSessionLiveness.NotRunning;
                }

                ShowState();
            }
        }
    }

    /// <summary>
    /// Whether a session for this agent has run in this Filekin window since it was opened.
    /// </summary>
    /// <remarks>
    /// This is what makes Resume CLI honest (owner decision, 2026-09-01). Closing a CLI stops the
    /// work, so offering to open it again is right: it is the same window, the same run, and the
    /// person only closed a tab. A freshly opened Filekin has run nothing, so it offers nothing to
    /// reopen — Continue is how work carries on there, and it uses the same saved conversation.
    /// </remarks>
    public bool HasRunInThisWindow
    {
        get => _hasRunInThisWindow;
        set
        {
            if (SetProperty(ref _hasRunInThisWindow, value))
            {
                ShowState();
            }
        }
    }

    /// <summary>
    /// What the CLI button will do now, in the words for this exact moment (owner decision,
    /// 2026-09-01). A running agent is opened; a stopped one is resumed. One label for both states
    /// hid which of the two was about to happen, and that is what made the row unreadable.
    /// </summary>
    public string SessionActionLabel => _isCliTabOpenHere
        ? "Go to CLI tab"

        // Resume is promised only where it can be kept. A disconnected agent with nothing to reopen
        // said "Resume CLI" too, which read as though the work were still waiting somewhere.
        : !IsRunningNow && CanOpenSession
            ? "Resume CLI"
            : "Open CLI";

    /// <summary>
    /// What the button does, or why it cannot be pressed. A greyed control that explains itself is
    /// the difference between a limit and a fault.
    /// </summary>
    public string SessionActionHelpText
    {
        get
        {
            if (_isCliTabOpenHere)
            {
                return "Goes to the terminal tab that has this session.";
            }

            if (_jobIsFinished)
            {
                return "This job is finished. Write a new objective to work here again.";
            }

            if (IsRunningNow)
            {
                return Provider == AgentProvider.ClaudeCode
                    ? "Opens a window on the Claude session that is running. Filekin keeps driving it."
                    // Codex allows one client per conversation, so there is no second view of it.
                    : "Codex is running under Filekin. One Codex at a time can hold a conversation, so its CLI cannot be opened as well.";
            }

            if (string.IsNullOrWhiteSpace(_nativeSessionId))
            {
                return "This agent has not worked in this folder yet.";
            }

            return CanOpenSession
                ? "Opens this conversation again in a terminal tab you drive. It keeps what it knew."
                : "Nothing is running to open. Press Continue: it carries this same conversation on.";
        }
    }

    /// <summary>
    /// Whether the CLI button can be pressed. There has to be something to open or something this
    /// window can reopen: a saved conversation on its own is memory, not a running tool, and a
    /// button offering it made a closed agent look like one that was waiting.
    /// </summary>
    public bool CanOpenSession =>
        _isCliTabOpenHere ||
        (!_jobIsFinished && !string.IsNullOrWhiteSpace(NativeSessionId) && Provider switch
        {
            // Claude can be opened while it runs, because attaching is another window on the one
            // background session. Codex cannot: Filekin's App Server already holds that thread.
            AgentProvider.ClaudeCode => IsRunningNow,
            AgentProvider.Codex => !IsRunningNow && _hasRunInThisWindow,
            _ => false,
        });

    /// <summary>
    /// Whether there is anything for End to end. A saved conversation is memory, not a running
    /// session, so it is not something to end — and a button that can always be pressed reads as
    /// proof that a session is always there, which was exactly the wrong thing to say.
    /// </summary>
    public bool CanEndSession => IsRunningNow;

    /// <summary>
    /// Whether asking the tool could still change this row. An agent Filekin can already see needs
    /// no asking, and one that has never run here has nothing to find.
    /// </summary>
    public bool MightBeRunningUnwatched =>
        !_isSessionOpenHere && !_isCliTabOpenHere && !string.IsNullOrWhiteSpace(NativeSessionId);

    /// <summary>The models this tool reports, with Default first.</summary>
    public ObservableCollection<string> ModelChoices { get; } = [ToolDefault];

    /// <summary>How hard the chosen model may be asked to think, with Default first.</summary>
    public ObservableCollection<string> EffortChoices { get; } = [ToolDefault];

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value ?? ToolDefault))
            {
                return;
            }

            ShowEffortsForTheChosenModel();
            OnPropertyChanged(nameof(ModelSummary));
            Choose();
        }
    }

    public string SelectedEffort
    {
        get => _selectedEffort;
        set
        {
            if (SetProperty(ref _selectedEffort, value ?? ToolDefault))
            {
                OnPropertyChanged(nameof(ModelSummary));
                Choose();
            }
        }
    }

    /// <summary>The whole choice in one short label, which is all the row shows until it is opened.</summary>
    public string ModelSummary =>
        IsDefault(_selectedModel)
            ? ToolDefault
            : IsDefault(_selectedEffort)
                ? _selectedModel
                : $"{_selectedModel} · {_selectedEffort}";

    /// <summary>Whether this row's model list is open.</summary>
    public bool IsChoosing
    {
        get => _isChoosing;
        set => SetProperty(ref _isChoosing, value);
    }

    /// <summary>The model to launch with, or <see langword="null"/> for the tool's own choice.</summary>
    public string? ChosenModel => IsDefault(_selectedModel) ? null : _selectedModel;

    public string? ChosenEffort =>
        IsDefault(_selectedModel) || IsDefault(_selectedEffort) ? null : _selectedEffort;

    public static string DisplayName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => provider.ToString(),
    };

    /// <summary>The shortest name that still names the tool, for cells too narrow for the full one.</summary>
    public static string ShortName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude",
        _ => provider.ToString(),
    };

    /// <summary>Refreshes this row from the project without replacing it.</summary>
    /// <param name="jobIsFinished">Whether the whole project is finished, which both rows must say.</param>
    public void Update(AgentParticipant participant, bool holdsTheTurn, bool jobIsFinished)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _applyingState = true;
        try
        {
            _connectionState = participant.ConnectionState;
            _turnState = participant.TurnState;
            _jobIsFinished = jobIsFinished;
            _hasWorkedOnObjective = participant.HasWorkedOnObjective;
            _nativeSessionId = participant.NativeSessionId;

            // An agent with no saved conversation has nothing to find, so the earlier answer to
            // "is it running unwatched" is spent. A stale yes is what kept a finished agent saying
            // it was still running.
            if (string.IsNullOrWhiteSpace(participant.NativeSessionId))
            {
                _unwatchedLiveness = AgentSessionLiveness.NotRunning;
            }

            Usage = UsageText(participant.Usage);
            HoldsTheTurn = holdsTheTurn;
            ShowState();
            SelectedModel = participant.PreferredModel ?? ToolDefault;
            SelectedEffort = participant.PreferredEffort ?? ToolDefault;
        }
        finally
        {
            _applyingState = false;
        }
    }

    /// <summary>Offers what this tool reports it can run. An empty list leaves only Default.</summary>
    public void ShowModels(IReadOnlyList<AgentModelChoice> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = models;
        var chosen = _selectedModel;

        ModelChoices.Clear();
        ModelChoices.Add(ToolDefault);
        foreach (var model in models)
        {
            ModelChoices.Add(model.Id);
        }

        // A model this install no longer offers is still listed, because it is what would be used.
        if (!IsDefault(chosen) && !ModelChoices.Contains(chosen, StringComparer.Ordinal))
        {
            ModelChoices.Add(chosen);
        }

        _applyingState = true;
        try
        {
            SelectedModel = chosen;
            // The selected model often has not changed, but its newly loaded capabilities have.
            // Rebuild the effort list even when SetProperty correctly treats the model value as the
            // same value, otherwise a saved effort is absent from the picker after reopening it.
            ShowEffortsForTheChosenModel();
        }
        finally
        {
            _applyingState = false;
        }
    }

    /// <summary>Every word and enabled state on the row comes from the same few facts.</summary>
    private void ShowState()
    {
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ConnectionHelpText));
        OnPropertyChanged(nameof(Work));
        OnPropertyChanged(nameof(NativeSessionId));
        OnPropertyChanged(nameof(SessionActionLabel));
        OnPropertyChanged(nameof(SessionActionHelpText));
        OnPropertyChanged(nameof(CanOpenSession));
        OnPropertyChanged(nameof(CanEndSession));
        OnPropertyChanged(nameof(IsRunningNow));
        OnPropertyChanged(nameof(MightBeRunningUnwatched));
    }

    private static bool IsDefault(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, ToolDefault, StringComparison.Ordinal);

    private void ShowEffortsForTheChosenModel()
    {
        var chosen = _selectedEffort;
        var efforts = _models
            .FirstOrDefault(model => string.Equals(model.Id, _selectedModel, StringComparison.Ordinal))
            ?.Efforts ?? [];

        EffortChoices.Clear();
        EffortChoices.Add(ToolDefault);
        foreach (var effort in efforts)
        {
            EffortChoices.Add(effort);
        }

        if (!EffortChoices.Contains(chosen, StringComparer.Ordinal))
        {
            _selectedEffort = ToolDefault;
            OnPropertyChanged(nameof(SelectedEffort));
            OnPropertyChanged(nameof(ModelSummary));
        }
    }

    private void Choose()
    {
        if (!_applyingState)
        {
            _chosen?.Invoke(this);
        }
    }

    private static string UsageText(AgentUsageSnapshot? usage)
    {
        if (usage is not { IsKnown: true })
        {
            return "Unknown";
        }

        return string.Join(
            "  ·  ",
            usage.Windows.Select(window => string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1:0}%",
                WindowLabel(window),
                window.RemainingPercent)));
    }

    /// <summary>
    /// Names a window by how long it is, which is the part a person can act on. Providers label the
    /// same idea differently: Claude reports a five-hour and a seven-day window, Codex calls its two
    /// windows primary and secondary, and neither label means anything on its own.
    /// </summary>
    private static string WindowLabel(AgentUsageWindow window) =>
        window.WindowDuration is { } duration && duration > TimeSpan.Zero
            ? DurationLabel(duration)
            : WindowName(window.Name);

    private static string DurationLabel(TimeSpan duration)
    {
        if (duration.TotalHours < 24)
        {
            var hours = Math.Round(duration.TotalHours);
            return string.Format(
                CultureInfo.CurrentCulture,
                hours == 1 ? "{0:0} hour" : "{0:0} hours",
                hours);
        }

        var days = Math.Round(duration.TotalDays);
        return string.Format(
            CultureInfo.CurrentCulture,
            days == 1 ? "{0:0} day" : "{0:0} days",
            days);
    }

    private static string WindowName(string name)
    {
        var separator = name.IndexOf(':', StringComparison.Ordinal);
        var trimmed = separator >= 0 ? name[(separator + 1)..] : name;
        return FriendlyName(trimmed) switch
        {
            "five hour" => "5 hours",
            "seven day" => "7 days",
            var other => other.Length == 0 ? name : other,
        };
    }

    private static string FriendlyName(string name) => name.Replace('_', ' ').Trim();
}
