using System.Runtime.CompilerServices;
using Filekin.Core.Agents;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>Reads Codex quota state only when the local tool confirms ChatGPT subscription auth.</summary>
public sealed class CodexAgentUsageSource : IAgentUsageSource, IAsyncDisposable
{
    private readonly CodexAppServerClient _client;

    public CodexAgentUsageSource()
        : this(new CodexAppServerClient())
    {
    }

    internal CodexAgentUsageSource(CodexAppServerClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public AgentProvider Provider => AgentProvider.Codex;

    public async Task<AgentUsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var account = await _client.ReadAccountAsync(cancellationToken).ConfigureAwait(false);
        EnsureSubscriptionAccount(account);

        var result = await _client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
        return CodexAppServerProtocol.ParseRateLimits(result, DateTimeOffset.UtcNow);
    }

    public async IAsyncEnumerable<AgentUsageSnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var account = await _client.ReadAccountAsync(cancellationToken).ConfigureAwait(false);
        EnsureSubscriptionAccount(account);

        await foreach (var notification in _client.ReadNotificationsAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (string.Equals(
                    notification.Method,
                    "account/rateLimits/updated",
                    StringComparison.Ordinal))
            {
                yield return CodexAppServerProtocol.ParseRateLimits(
                    notification.Parameters,
                    DateTimeOffset.UtcNow);
            }
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static void EnsureSubscriptionAccount(CodexSubscriptionAccount account)
    {
        if (!account.UsesChatGptSubscription)
        {
            throw new InvalidOperationException(
                "Codex is not authenticated with a ChatGPT subscription. Filekin will not silently use API-key billing.");
        }
    }
}
