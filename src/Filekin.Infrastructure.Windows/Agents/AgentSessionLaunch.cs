using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Everything one native agent session needs to start, gathered by Filekin before any process runs.
/// The owner's approval travels with the request, so no launch path can reach a provider without it.
/// </summary>
public sealed record AgentSessionLaunchRequest(
    AgentProvider Provider,
    Guid ProjectId,
    string ProjectFolderPath,
    string DisplayName,
    string Prompt,
    AgentMcpLaunchConfiguration McpServer,
    SharedCheckoutConsent Consent);

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
}
