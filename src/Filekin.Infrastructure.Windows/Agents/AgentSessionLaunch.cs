using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Everything one native agent session needs to start, gathered by Filekin before any process runs.
/// The owner's approval travels with the request, so no launch path can reach a provider without it.
/// </summary>
/// <param name="Effort">How hard that model was asked to think, in the tool's own words.</param>
/// <param name="Model">
/// The model the user chose for this agent, or <see langword="null"/> to leave the choice to that
/// tool's own configuration. Filekin passes it at launch and changes no saved setting.
/// </param>
public sealed record AgentSessionLaunchRequest(
    AgentProvider Provider,
    Guid ProjectId,
    string ProjectFolderPath,
    string DisplayName,
    string Prompt,
    AgentMcpLaunchConfiguration McpServer,
    SharedCheckoutConsent Consent,
    string? Model = null,
    string? Effort = null,
    string? ResumeSessionId = null);

/// <summary>
/// One native agent session Filekin started. Filekin never kills the process: it asks the provider to
/// stop, and treats the provider's own report that the session ended as the only proof of a stop.
/// </summary>
public interface IAgentSessionHandle : IAsyncDisposable
{
    AgentProvider Provider { get; }

    /// <summary>The provider's own session identifier, as it reported it.</summary>
    string NativeSessionId { get; }

    /// <summary>Completes when the provider reports this native session has stopped.</summary>
    Task Stopped { get; }

    /// <summary>
    /// Completes when the provider says this session cannot go on without a person: a permission it
    /// must ask about, or a question only the user can answer. Filekin never answers one of these for
    /// the user; it says so plainly instead of leaving a stuck session looking busy.
    /// </summary>
    Task<string> NeedsPerson { get; }

    /// <summary>
    /// The provider's own latest word about this session, in its own words, or <see langword="null"/>
    /// while it has said nothing. Filekin passes it through and does not rewrite it.
    /// </summary>
    string? LastReport { get; }

    /// <summary>
    /// Replayable read-only events for this exact native session. The feed contains provider-neutral
    /// immutable snapshots and never accepts replies or approval decisions.
    /// </summary>
    AgentSessionEventFeed Events { get; }

    /// <summary>
    /// Asks the provider to stop this session at a safe point. It never terminates a process and
    /// never completes <see cref="Stopped"/> by itself.
    /// </summary>
    Task RequestStopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional signal from a provider that says when a turn ended, separately from when the session
/// ended. Filekin releases the working-tree lease on this, so a session a person is reading is no
/// longer stopped merely to prove that its turn is over (owner decision, 2026-09-02).
/// </summary>
/// <remarks>
/// A provider without this says only that it stopped, and its turn still moves on the proven stop
/// exactly as before.
/// </remarks>
public interface ITurnScopedAgentSessionHandle
{
    /// <summary>
    /// Completes when the provider reports the turn this session was given is finished while the
    /// session itself stays alive and idle. A finished turn is not a stopped session.
    /// </summary>
    Task TurnFinished { get; }
}

/// <summary>
/// Optional live interaction surface implemented only when the provider exposes a supported session
/// API. Filekin never falls back to terminal scraping or synthesized input.
/// </summary>
public interface IInteractiveAgentSessionHandle
{
    Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default);

    Task RespondAsync(
        AgentSessionRequestResponse response,
        CancellationToken cancellationToken = default);
}

/// <summary>Starts one native agent session for a Filekin project.</summary>
public interface IAgentSessionLauncher
{
    Task<IAgentSessionHandle> LaunchAsync(
        AgentSessionLaunchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks every session this provider still has open in a project folder to stop, including ones
    /// this Filekin window never started and is not watching. Sessions outlive the window that opened
    /// them, and each live session keeps its own Filekin MCP companion alive, so a person needs a way
    /// to end them from here. It never kills a process: it uses the provider's own stop.
    /// </summary>
    /// <returns>
    /// How many sessions were asked to stop, or <see langword="null"/> when this provider has no
    /// cooperative stop of its own and its sessions simply end with their turn.
    /// </returns>
    Task<int?> StopSessionsAsync(
        AgentProvider provider,
        string projectFolderPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many sessions this provider still has open in a project folder, asked of the provider
    /// itself rather than of what this window happens to be watching.
    /// </summary>
    /// <remarks>
    /// A Claude background session stays alive and idle after its turn ends, so the window stops
    /// watching it long before it stops existing. Counting only watched sessions therefore reports
    /// zero at exactly the moment a person closes Filekin and leaves two of them running, which is
    /// what happened. Only the provider knows.
    /// </remarks>
    /// <returns>
    /// The number of open sessions, or <see langword="null"/> when this provider has no session that
    /// outlives its turn and so can leave nothing behind.
    /// </returns>
    Task<int?> CountLiveSessionsAsync(
        AgentProvider provider,
        string projectFolderPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The background agents Claude reports for one folder. A background session has two identities:
    /// the conversation Filekin stores and resumes, and the short handle <c>claude attach</c> takes.
    /// Only Claude can match them, and its answer also says whether the session is still running.
    /// </summary>
    /// <remarks>The default is empty, for a launcher that has no Claude of its own to ask.</remarks>
    Task<IReadOnlyList<ClaudeBackgroundAgent>> ListClaudeBackgroundAgentsAsync(
        string projectFolderPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClaudeBackgroundAgent>>([]);
}
