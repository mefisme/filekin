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
        "No objective yet. Ask for it with filekin_send_message, then stop.";

    /// <remarks>
    /// The objective is what finished looks like and does not change as work moves; the handoff is
    /// what is left right now. An agent that reads the objective as its next task will redo work
    /// that is already done, so the newer of the two is named plainly.
    /// </remarks>
    internal const string TakingOver =
        "You are being handed this work over. Call filekin_accept_handoff after you read the state. "
        + "The handoff says what is left and is newer than the objective, which only says what "
        + "finished looks like.";

    /// <param name="acceptingHandoff">
    /// Set when this agent is being started to pick up a handoff somebody else already wrote. It is
    /// the only thing it cannot work out for itself before it has clocked in.
    /// </param>
    internal static string Create(string objective, bool acceptingHandoff = false)
    {
        ArgumentNullException.ThrowIfNull(objective);

        var trimmed = objective.Trim();
        var work = trimmed.Length == 0
            ? NoObjective
            : $"The user's objective, in their words:{Environment.NewLine}{trimmed}";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            "This is a Filekin agent project: one agent works in this folder at a time.",
            "Call filekin_clock_in, then filekin_read_state. Check the state again as you work: it "
            + "says whether Filekin has asked you to hand over or stop. The Filekin tools describe "
            + "the rest.",
            acceptingHandoff ? TakingOver : "You are starting this work.",
            work);
    }
}
