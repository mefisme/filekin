namespace Filekin.Infrastructure.Windows.Agents;

public sealed record CodexSubscriptionAccount(bool IsAuthenticated, string? AuthenticationMode, string? PlanType)
{
    public bool UsesChatGptSubscription =>
        IsAuthenticated && string.Equals(AuthenticationMode, "chatgpt", StringComparison.OrdinalIgnoreCase);
}
