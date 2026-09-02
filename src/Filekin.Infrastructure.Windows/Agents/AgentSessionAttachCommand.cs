using System.Text;
using System.Text.RegularExpressions;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>Why Filekin will not open one session in a terminal.</summary>
public enum AgentSessionAttachRefusal
{
    None,
    NoSession,
    UnrecognizedSessionId,
    MissingCoordinationIdentity,
    LiveCodexThread,
    ClaudeSessionNotLive,
}

/// <summary>
/// The provider's own command for opening one exact coordinated session in a real terminal.
/// </summary>
/// <remarks>
/// This is the native CLI attachment the Agent Session decision reserved: it is only ever the
/// provider's own documented command against the provider's own session id, so it is not a screen
/// scrape, not a synthesized keystroke, and never an unrelated duplicate CLI presented as the
/// working agent. Filekin keeps owning coordination while the terminal is open; MCP clock-in,
/// messages, and handoffs are model tool calls and do not care which front end a person is watching.
///
/// The two providers are not symmetric, and the difference decides what this command must carry:
/// <c>claude attach</c> opens the <em>already running</em> background process, which still holds the
/// <c>--mcp-config</c> Filekin started it with, so nothing has to be repeated. <c>codex resume</c>
/// starts a <em>new</em> process that reads only the user's own configuration, and Filekin never
/// writes its coordination server there, so the overrides must be passed again or the resumed
/// session would have the conversation and none of the coordination tools.
/// </remarks>
public static partial class AgentSessionAttachCommand
{
    /// <summary>
    /// Provider session ids are opaque to Filekin, but they are pasted into a shell command line, so
    /// only the hex-and-dash shape both providers actually emit is ever accepted. Anything else is
    /// refused rather than quoted, because a session id is not a place to be clever.
    /// </summary>
    [GeneratedRegex(@"^[0-9a-fA-F][0-9a-fA-F-]{7,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSessionId { get; }

    /// <summary>
    /// The command to run in a hosted PowerShell terminal, or <see langword="null"/> with the reason
    /// in <paramref name="refusal"/>.
    /// </summary>
    /// <param name="provider">The agent whose session is being opened.</param>
    /// <param name="nativeSessionId">The provider's own id for that exact session.</param>
    /// <param name="coordinationIdentity">
    /// This project's Filekin MCP identity. Required for Codex, because a resumed Codex process must
    /// be given the coordination server again. Ignored for Claude, which keeps the one it has.
    /// </param>
    /// <param name="codexThreadIsLive">
    /// Whether Filekin's own App Server still holds this Codex thread. Resuming a live thread would
    /// put a second client on one conversation, which Codex does not support the way Claude's attach
    /// does, so Filekin refuses instead of forking the thread.
    /// </param>
    public static string? Create(
        AgentProvider provider,
        string? nativeSessionId,
        out AgentSessionAttachRefusal refusal,
        AgentMcpLaunchConfiguration? coordinationIdentity = null,
        bool codexThreadIsLive = false)
    {
        refusal = AgentSessionAttachRefusal.None;
        if (string.IsNullOrWhiteSpace(nativeSessionId))
        {
            refusal = AgentSessionAttachRefusal.NoSession;
            return null;
        }

        var id = nativeSessionId.Trim();
        if (!SafeSessionId.IsMatch(id))
        {
            refusal = AgentSessionAttachRefusal.UnrecognizedSessionId;
            return null;
        }

        switch (provider)
        {
            // `claude --bg` prints the id that `claude attach` takes. Attach opens the live
            // background session itself, and the session keeps running when the terminal closes.
            case AgentProvider.ClaudeCode:
                return $"claude attach {id}";

            case AgentProvider.Codex:
                if (codexThreadIsLive)
                {
                    refusal = AgentSessionAttachRefusal.LiveCodexThread;
                    return null;
                }

                if (coordinationIdentity is null ||
                    coordinationIdentity.Provider != AgentProvider.Codex)
                {
                    refusal = AgentSessionAttachRefusal.MissingCoordinationIdentity;
                    return null;
                }

                return CodexResume(id, coordinationIdentity);

            default:
                refusal = AgentSessionAttachRefusal.NoSession;
                return null;
        }
    }

    /// <summary>The one-line reason, in the words the person reads.</summary>
    public static string Explain(AgentProvider provider, AgentSessionAttachRefusal refusal) => refusal switch
    {
        AgentSessionAttachRefusal.NoSession =>
            $"{Name(provider)} has no session to open yet.",
        AgentSessionAttachRefusal.UnrecognizedSessionId =>
            "Filekin does not recognise that session id and will not put it on a command line.",
        AgentSessionAttachRefusal.MissingCoordinationIdentity =>
            "Filekin could not work out this project's coordination tools, and Codex must be given them to resume.",
        AgentSessionAttachRefusal.ClaudeSessionNotLive =>
            "Claude has no running background session for this conversation any more. Press Start work to carry it on.",
        AgentSessionAttachRefusal.LiveCodexThread =>
            "Filekin is still running this Codex thread. Codex resume would start a second copy of it, so end the session first, or watch it here.",
        _ => string.Empty,
    };

    /// <summary>The tab label for the attached provider terminal.</summary>
    public static string Title(AgentProvider provider, string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
        return $"{Name(provider)} CLI · {(folderName.Length == 0 ? folderPath : folderName)}";
    }

    private static string Name(AgentProvider provider) => provider switch
    {
        AgentProvider.ClaudeCode => "Claude Code",
        AgentProvider.Codex => "Codex",
        _ => "Agent",
    };

    private static string CodexResume(string id, AgentMcpLaunchConfiguration coordinationIdentity)
    {
        var identity = CodexAppServerLaunchPlan.Normalize(coordinationIdentity);
        var command = new StringBuilder("codex resume");
        foreach (var configOverride in CodexAppServerLaunchPlan.CoordinationConfigOverrides(identity))
        {
            command.Append(" --config ").Append(SingleQuote(configOverride));
        }

        // The id is last because `codex resume [OPTIONS] [SESSION_ID] [PROMPT]` reads it positionally.
        return command.Append(' ').Append(id).ToString();
    }

    /// <summary>
    /// One PowerShell literal. The overrides carry TOML strings whose quotes and backslashes must
    /// reach Codex exactly as written, and inside single quotes PowerShell expands nothing; only a
    /// single quote itself has to be doubled.
    /// </summary>
    private static string SingleQuote(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
