namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// The opening text Filekin sends to whichever agent it starts. It carries two things and nothing
/// else: the coordination rules the agent cannot discover on its own, and the user's own objective,
/// quoted rather than rewritten.
/// </summary>
/// <remarks>
/// Whether the user writes this opening text themselves is still an open product question, so this
/// wording is deliberately small and easy to replace. It states no engineering rules of its own: the
/// project's own instruction files remain the only source of those.
/// </remarks>
internal static class AgentRunPrompt
{
    internal const string NoObjective =
        "The user has not written the objective yet. Ask for it with filekin_send_message, then stop.";

    internal static string Create(string objective)
    {
        ArgumentNullException.ThrowIfNull(objective);

        var trimmed = objective.Trim();
        var work = trimmed.Length == 0
            ? NoObjective
            : $"The user's objective, in their words:{Environment.NewLine}{trimmed}";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            "You are working in a Filekin agent project. Filekin gives one agent at a time permission "
            + "to change this folder, and hands that turn between agents as their subscription allowance "
            + "runs down.",
            "Call filekin_clock_in first, before doing anything else, then filekin_read_state. Filekin "
            + "does not know you are here until you clock in, and it will not give you the turn.",
            "Check filekin_read_state again as you work. If it says a handoff or a stop was requested, "
            + "finish at a safe point and call filekin_submit_handoff with what you did, what is left, "
            + "and how you checked it. If something needs the user, call filekin_report_blocked rather "
            + "than guessing. When the objective is done, call filekin_report_completed.",
            work);
    }
}
