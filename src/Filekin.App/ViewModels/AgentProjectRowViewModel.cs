using System.Globalization;
using System.IO;
using Filekin.Core.Agents;

namespace Filekin.App.ViewModels;

/// <summary>
/// One row in the <c>/projects</c> rich view: a folder agents have been set up in, stating the same
/// two facts every row in the Agent Control Room states — whether an agent is connected, and what
/// the work is doing. Whether an agent is running is answered by the live sessions passed in, never
/// by a stored connection state, so restarting Filekin cannot make this row claim a session that
/// ended.
/// </summary>
public sealed class AgentProjectRowViewModel
{
    public AgentProjectRowViewModel(
        AgentProjectState project,
        IReadOnlyCollection<AgentProvider> running,
        IReadOnlyCollection<AgentProvider> unknown,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(unknown);

        ProjectId = project.Id;
        FolderPath = project.FolderPath;
        FolderName = FolderNameOf(project.FolderPath);
        Objective = string.IsNullOrWhiteSpace(project.Objective) ? "No objective yet" : project.Objective;
        IsRunning = running.Count > 0;
        IsConnectionUnknown = unknown.Count > 0;
        Connection = IsRunning ? "Running" : IsConnectionUnknown ? "No answer" : "Not connected";
        Work = WorkWord(project, IsRunning);
        Agents = AgentsText(project, running);
        Usage = UsageText(project, now);
        CanRemove = !IsRunning && !IsConnectionUnknown;
        RemoveHelpText = CanRemove
            ? "Remove this agent project. Its coordination memory is deleted; nothing on the folder itself is touched."
            : "This folder still has a live or unreachable session. End it before removing the project.";
        AutomationName = string.Create(
            CultureInfo.CurrentCulture,
            $"{FolderName}, {Connection}, {Work}, {Agents}");
    }

    public Guid ProjectId { get; }

    public string FolderPath { get; }

    public string FolderName { get; }

    /// <summary>What the folder was last asked to do, so one row is enough to recognise it.</summary>
    public string Objective { get; }

    public bool IsRunning { get; }

    public bool IsConnectionUnknown { get; }

    public string Connection { get; }

    public string Work { get; }

    /// <summary>Which agents this folder has, and which of them is running right now.</summary>
    public string Agents { get; }

    public string Usage { get; }

    /// <summary>
    /// Whether removal looks safe from the last refresh. This is a display hint, not the authoritative
    /// answer: the removal path re-checks live session state immediately before deleting anything, so a
    /// row that went stale between refreshes can never let removal through on an old "yes".
    /// </summary>
    public bool CanRemove { get; }

    /// <summary>What Remove does, or why it is disabled right now — shown as its tooltip and automation name.</summary>
    public string RemoveHelpText { get; }

    public string AutomationName { get; }

    /// <summary>The folder's own name, falling back to the whole path for a drive root.</summary>
    private static string FolderNameOf(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? folderPath : name;
    }

    /// <summary>
    /// The project's work in one word, using the same vocabulary as a row in the room. The most
    /// pressing answer wins: something that needs the user is said before work in progress, and work
    /// in progress before anything finished.
    /// </summary>
    private static string WorkWord(AgentProjectState project, bool isRunning)
    {
        if (project.SharedCheckoutConsent is null)
        {
            return "Not set up";
        }

        var turns = project.Participants.Values.Select(participant => participant.TurnState).ToArray();
        if (turns.Any(turn => turn is AgentTurnState.Blocked or AgentTurnState.NeedsAttention))
        {
            return "Needs you";
        }

        if (turns.Contains(AgentTurnState.Active))
        {
            return "Working";
        }

        if (turns.Contains(AgentTurnState.HandoffRequested))
        {
            return "Handing over";
        }

        if (turns.Contains(AgentTurnState.StopRequested))
        {
            return "Stopping";
        }

        if (turns.Contains(AgentTurnState.CompletionReported))
        {
            return "Finishing";
        }

        if (turns.Contains(AgentTurnState.Completed))
        {
            return "Done";
        }

        if (isRunning)
        {
            return "Waiting";
        }

        // An agent that never took a turn on this objective has not stopped anything.
        return project.Participants.Values.Any(participant => participant.HasWorkedOnObjective)
            ? "Stopped"
            : "Not started";
    }

    /// <summary>
    /// Names the agents this folder has actually used, marking the ones running now. A folder nobody
    /// has started yet says so rather than showing an empty cell.
    /// </summary>
    private static string AgentsText(AgentProjectState project, IReadOnlyCollection<AgentProvider> running)
    {
        var named = project.Participants
            .Where(pair => running.Contains(pair.Key) ||
                           pair.Value.HasWorkedOnObjective ||
                           !string.IsNullOrWhiteSpace(pair.Value.NativeSessionId))
            .OrderBy(pair => pair.Key)
            .Select(pair => running.Contains(pair.Key)
                ? AgentParticipantViewModel.DisplayName(pair.Key) + " ●"
                : AgentParticipantViewModel.DisplayName(pair.Key))
            .ToArray();
        return named.Length == 0 ? "None yet" : string.Join("  ·  ", named);
    }

    /// <summary>
    /// The tightest usage window each agent reported, short enough for one cell. Numbers are only as
    /// new as the last time that project was open, so an unread agent says so instead of guessing.
    /// </summary>
    private static string UsageText(AgentProjectState project, DateTimeOffset now)
    {
        var parts = project.Participants
            .OrderBy(pair => pair.Key)
            .Where(pair => pair.Value.Usage is { IsKnown: true })
            .Select(pair => string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1:0}%",
                AgentParticipantViewModel.ShortName(pair.Key),
                pair.Value.Usage!.MinimumRemainingPercentAt(now) ?? 0d))
            .ToArray();
        return parts.Length == 0 ? "unknown" : string.Join("  ·  ", parts);
    }
}
