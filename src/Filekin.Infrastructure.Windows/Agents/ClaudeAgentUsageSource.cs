using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Reads Claude Code status-line quota observations after confirming that the installed CLI uses a
/// Claude.ai subscription. Observations arrive from the short-lived status-line helper process that
/// Claude runs for this project, so they are read back from app-owned transactional state. Before the
/// first provider response populates a rate-limit window, usage remains honestly unknown.
/// </summary>
public sealed class ClaudeAgentUsageSource : IAgentUsageSource
{
    private readonly ClaudeCliClient _client;
    private readonly string _folderPath;
    private readonly IAgentUsageObservationStore? _observationStore;
    private readonly Guid _projectId;
    private readonly Channel<AgentUsageSnapshot> _observations =
        Channel.CreateUnbounded<AgentUsageSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });
    private AgentUsageSnapshot _latest =
        new(AgentProvider.ClaudeCode, DateTimeOffset.MinValue, []);

    public ClaudeAgentUsageSource(string folderPath)
        : this(new ClaudeCliClient(), folderPath)
    {
    }

    public ClaudeAgentUsageSource(
        IAgentUsageObservationStore observationStore,
        Guid projectId,
        string folderPath)
        : this(new ClaudeCliClient(), folderPath, observationStore, projectId)
    {
    }

    internal ClaudeAgentUsageSource(
        ClaudeCliClient client,
        string folderPath,
        IAgentUsageObservationStore? observationStore = null,
        Guid projectId = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (observationStore is not null && projectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Stored quota observations are read per project, so the project id is required.",
                nameof(projectId));
        }

        _client = client;
        _folderPath = Path.GetFullPath(folderPath);
        _observationStore = observationStore;
        _projectId = projectId;
    }

    public AgentProvider Provider => AgentProvider.ClaudeCode;

    public async Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSubscriptionAccountAsync(cancellationToken).ConfigureAwait(false);
        if (_observationStore is not null)
        {
            var stored = await _observationStore
                .ReadUsageObservationAsync(_projectId, AgentProvider.ClaudeCode, cancellationToken)
                .ConfigureAwait(false);
            if (stored is not null)
            {
                Adopt(stored);
            }
        }

        return Volatile.Read(ref _latest);
    }

    public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureSubscriptionAccountAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var snapshot in _observations.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return snapshot;
        }
    }

    /// <summary>Accepts the documented JSON payload supplied to a Claude Code status-line command.</summary>
    public void ObserveStatusLine(string json, DateTimeOffset? observedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        Adopt(ClaudeCliProtocol.ParseStatusLineUsage(
            document.RootElement,
            observedAt ?? DateTimeOffset.UtcNow));
    }

    private void Adopt(AgentUsageSnapshot snapshot)
    {
        if (snapshot.ObservedAt <= Volatile.Read(ref _latest).ObservedAt)
        {
            return;
        }

        Volatile.Write(ref _latest, snapshot);
        if (!_observations.Writer.TryWrite(snapshot))
        {
            throw new InvalidOperationException("The Claude usage observation could not be recorded.");
        }
    }

    private async Task EnsureSubscriptionAccountAsync(CancellationToken cancellationToken)
    {
        var account = await _client.ReadAccountAsync(_folderPath, cancellationToken).ConfigureAwait(false);
        if (!account.UsesClaudeSubscription)
        {
            throw new InvalidOperationException(
                "Claude Code is not authenticated with a Claude.ai subscription. Filekin will not silently use API-key billing.");
        }
    }
}
