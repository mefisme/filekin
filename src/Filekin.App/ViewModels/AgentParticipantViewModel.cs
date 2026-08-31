using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Filekin.Core.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One agent's row in the <c>/agents</c> control room. Provider facts arrive as a neutral snapshot;
/// this turns them into the plain words a person reads, and never invents a number. Unknown allowance
/// says so rather than showing a comfortable zero.
/// </summary>
public sealed class AgentParticipantViewModel
{
    public AgentParticipantViewModel(AgentParticipant participant, bool holdsTheTurn)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Provider = participant.Provider;
        NativeSessionId = participant.NativeSessionId;
        Name = DisplayName(participant.Provider);
        Connection = ConnectionText(participant.ConnectionState);
        Turn = TurnText(participant.TurnState);
        Usage = UsageText(participant.Usage);
        HoldsTheTurn = holdsTheTurn;
    }

    public string Name { get; }

    public AgentProvider Provider { get; }

    public string? NativeSessionId { get; }

    public bool CanOpenSession => !string.IsNullOrWhiteSpace(NativeSessionId);

    /// <summary>Whether the tool is installed, signed in, and reachable, in plain words.</summary>
    public string Connection { get; }

    /// <summary>What this agent is doing right now.</summary>
    public string Turn { get; }

    /// <summary>How much of each subscription window is left, or that it is unknown.</summary>
    public string Usage { get; }

    /// <summary>Whether this agent currently owns the one working-tree turn.</summary>
    public bool HoldsTheTurn { get; }

    public static string DisplayName(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => "Codex",
        AgentProvider.ClaudeCode => "Claude Code",
        _ => provider.ToString(),
    };

    private static string ConnectionText(AgentConnectionState state) => state switch
    {
        AgentConnectionState.Offline => "Not connected",
        AgentConnectionState.UsagePending => "Connected, allowance not reported yet",
        AgentConnectionState.Ready => "Ready",
        AgentConnectionState.Unavailable => "Cannot be reached",
        _ => "Unknown",
    };

    private static string TurnText(AgentTurnState state) => state switch
    {
        AgentTurnState.ClockedOut => "Not here",
        AgentTurnState.Waiting => "Waiting",
        AgentTurnState.Active => "Working now",
        AgentTurnState.HandoffRequested => "Asked to hand over",
        AgentTurnState.Blocked => "Needs you",
        AgentTurnState.NeedsAttention => "Needs you",
        AgentTurnState.CompletionReported => "Says the work is done",
        AgentTurnState.Completed => "Done",
        _ => "Unknown",
    };

    private static string UsageText(AgentUsageSnapshot? usage)
    {
        if (usage is not { IsKnown: true })
        {
            return "Allowance unknown";
        }

        return string.Join(
            "  ·  ",
            usage.Windows.Select(window => string.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1:0}% left",
                WindowName(window.Name),
                window.RemainingPercent)));
    }

    /// <summary>
    /// Turns a provider's own window key into something readable. The provider prefix and underscores
    /// are noise to a person; the window itself is kept separate, never merged into one figure.
    /// </summary>
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
