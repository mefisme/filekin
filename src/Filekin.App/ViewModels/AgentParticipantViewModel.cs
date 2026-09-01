using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One agent row in the <c>/agents</c> control room: what it is doing, what it has left, and the
/// controls that belong to it. Facts on the left, controls on the right. A row is updated in place
/// rather than rebuilt, so a list somebody has open does not close under them.
/// </summary>
public sealed class AgentParticipantViewModel : ObservableObject
{
    /// <summary>The first entry of both lists: leave the choice to the tool's own configuration.</summary>
    public const string ToolDefault = "Default";

    private readonly Action<AgentParticipantViewModel>? _chosen;
    private IReadOnlyList<AgentModelChoice> _models = [];
    private bool _applyingState;
    private bool _holdsTheTurn;
    private bool _isChoosing;
    private string _selectedEffort = ToolDefault;
    private string _selectedModel = ToolDefault;
    private string _state = string.Empty;
    private string _usage = string.Empty;
    private string? _nativeSessionId;

    public AgentParticipantViewModel(
        AgentParticipant participant,
        bool holdsTheTurn,
        Action<AgentParticipantViewModel>? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Provider = participant.Provider;
        Name = DisplayName(participant.Provider);
        _chosen = chosen;
        Update(participant, holdsTheTurn);
    }

    public AgentProvider Provider { get; }

    public string Name { get; }

    /// <summary>What this agent is doing, in one phrase. Being absent is a state, not a second column.</summary>
    public string State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>How much of each subscription window is left, or that it is unknown.</summary>
    public string Usage
    {
        get => _usage;
        private set => SetProperty(ref _usage, value);
    }

    public bool HoldsTheTurn
    {
        get => _holdsTheTurn;
        private set => SetProperty(ref _holdsTheTurn, value);
    }

    public string? NativeSessionId
    {
        get => _nativeSessionId;
        private set
        {
            if (SetProperty(ref _nativeSessionId, value))
            {
                OnPropertyChanged(nameof(CanOpenSession));
            }
        }
    }

    public bool CanOpenSession => !string.IsNullOrWhiteSpace(NativeSessionId);

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

    /// <summary>Refreshes this row from the project without replacing it.</summary>
    public void Update(AgentParticipant participant, bool holdsTheTurn)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _applyingState = true;
        try
        {
            State = StateText(participant);
            Usage = UsageText(participant.Usage);
            HoldsTheTurn = holdsTheTurn;
            NativeSessionId = participant.NativeSessionId;
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

    private static string StateText(AgentParticipant participant) => participant.ConnectionState switch
    {
        AgentConnectionState.Offline => "Not here",
        AgentConnectionState.Unavailable => "Cannot be reached",
        _ => TurnText(participant.TurnState),
    };

    private static string TurnText(AgentTurnState state) => state switch
    {
        AgentTurnState.ClockedOut => "Here, no turn yet",
        AgentTurnState.Waiting => "Waiting",
        AgentTurnState.Active => "Working now",
        AgentTurnState.HandoffRequested => "Asked to hand over",
        AgentTurnState.Blocked => "Needs you",
        AgentTurnState.NeedsAttention => "Needs you",
        AgentTurnState.CompletionReported => "Says the work is done",
        AgentTurnState.Completed => "Done",
        AgentTurnState.StopRequested => "Stopping",
        _ => "Unknown",
    };

    private static string UsageText(AgentUsageSnapshot? usage)
    {
        if (usage is not { IsKnown: true })
        {
            return "Usage left: unknown";
        }

        // Named once, not on every window: "5 hours 92%  ·  7 days 60%" after one plain heading.
        return "Usage left — " + string.Join(
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
        return trimmed.Replace('_', ' ').Trim() switch
        {
            "five hour" => "5 hours",
            "seven day" => "7 days",
            var other => other.Length == 0 ? name : other,
        };
    }
}
