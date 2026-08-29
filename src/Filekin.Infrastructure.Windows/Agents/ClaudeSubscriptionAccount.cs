namespace Filekin.Infrastructure.Windows.Agents;

internal sealed record ClaudeSubscriptionAccount(
    bool LoggedIn,
    string? AuthMethod,
    string? ApiProvider,
    string? SubscriptionType)
{
    public bool UsesClaudeSubscription =>
        LoggedIn &&
        string.Equals(AuthMethod, "claude.ai", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ApiProvider, "firstParty", StringComparison.OrdinalIgnoreCase);
}
