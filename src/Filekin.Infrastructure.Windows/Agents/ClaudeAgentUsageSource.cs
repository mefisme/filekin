using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Holds Claude Code status-line quota observations after confirming that the installed CLI uses a
/// Claude.ai subscription. Before the first provider response, usage remains honestly unknown.
/// </summary>
public sealed class ClaudeAgentUsageSource : IAgentUsageSource
{
    private readonly ClaudeCliClient _client;
    private readonly string _folderPath;
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

    internal ClaudeAgentUsageSource(ClaudeCliClient client, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        _client = client;
        _folderPath = Path.GetFullPath(folderPath);
    }

    public AgentProvider Provider => AgentProvider.ClaudeCode;

    public async Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSubscriptionAccountAsync(cancellationToken).ConfigureAwait(false);
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
        var snapshot = ClaudeCliProtocol.ParseStatusLineUsage(
            document.RootElement,
            observedAt ?? DateTimeOffset.UtcNow);
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
