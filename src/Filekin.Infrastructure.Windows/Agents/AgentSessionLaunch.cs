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
    string? Effort = null);

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
}
