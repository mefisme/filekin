using System.Text.Json;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeCliProtocolTests
{
    [TestMethod]
    public void ParseAccountDistinguishesClaudeSubscriptionFromApiKeyMode()
    {
        using var subscriptionJson = JsonDocument.Parse(
            """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro","email":"not-retained@example.com"}""");
        using var apiKeyJson = JsonDocument.Parse(
            """{"loggedIn":true,"authMethod":"api_key","apiProvider":"firstParty","subscriptionType":null}""");

        var subscription = ClaudeCliProtocol.ParseAccount(subscriptionJson.RootElement);
        var billedApi = ClaudeCliProtocol.ParseAccount(apiKeyJson.RootElement);

        Assert.IsTrue(subscription.UsesClaudeSubscription);
        Assert.AreEqual("pro", subscription.SubscriptionType);
        Assert.IsFalse(billedApi.UsesClaudeSubscription);
    }

    [TestMethod]
    public void ParseStatusLineUsageKeepsFiveHourAndSevenDayWindowsSeparate()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rate_limits": {
                "five_hour": { "used_percentage": 12.5, "resets_at": 1787974347 },
                "seven_day": { "used_percentage": 41, "resets_at": 1788561147 }
              }
            }
            """);
        var observedAt = DateTimeOffset.Parse(
            "2026-08-28T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);

        var snapshot = ClaudeCliProtocol.ParseStatusLineUsage(document.RootElement, observedAt);

        Assert.AreEqual(AgentProvider.ClaudeCode, snapshot.Provider);
        Assert.HasCount(2, snapshot.Windows);
        Assert.AreEqual("claude:five_hour", snapshot.Windows[0].Name);
        Assert.AreEqual(12.5, snapshot.Windows[0].UsedPercent);
        Assert.AreEqual(TimeSpan.FromHours(5), snapshot.Windows[0].WindowDuration);
        Assert.AreEqual("claude:seven_day", snapshot.Windows[1].Name);
        Assert.AreEqual(41, snapshot.Windows[1].UsedPercent);
        Assert.AreEqual(TimeSpan.FromDays(7), snapshot.Windows[1].WindowDuration);
    }

    [TestMethod]
    public void MissingStatusLineRateLimitsRemainUnknown()
    {
        using var document = JsonDocument.Parse("{\"session_id\":\"session-1\"}");

        var snapshot = ClaudeCliProtocol.ParseStatusLineUsage(
            document.RootElement,
            DateTimeOffset.UtcNow);

        Assert.IsFalse(snapshot.IsKnown);
        Assert.IsNull(snapshot.MinimumRemainingPercent);
    }

    [TestMethod]
    public void ParseBackgroundSessionsPreservesLifecycleAndBlockingState()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {
                "id": "7c5dcf5d",
                "sessionId": "4b871826-5741-4d6e-94c3-feb720be8f4a",
                "cwd": "D:\\GitHub\\filekin",
                "kind": "background",
                "state": "blocked",
                "status": "waiting",
                "waitingFor": "permission prompt",
                "pid": 1234,
                "startedAt": 1787954400000
              }
            ]
            """);

        var sessions = ClaudeCliProtocol.ParseBackgroundSessions(document.RootElement);

        Assert.HasCount(1, sessions);
        Assert.AreEqual("7c5dcf5d", sessions[0].Id);
        Assert.AreEqual("4b871826-5741-4d6e-94c3-feb720be8f4a", sessions[0].SessionId);
        Assert.AreEqual("blocked", sessions[0].State);
        Assert.AreEqual("waiting", sessions[0].Status);
        Assert.AreEqual("permission prompt", sessions[0].WaitingFor);
        Assert.AreEqual(1234, sessions[0].ProcessId);
    }

    [TestMethod]
    public void ParseBackgroundSessionsRejectsIncompleteEntries()
    {
        using var document = JsonDocument.Parse("[{\"cwd\":\"D:\\\\GitHub\"}]");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => ClaudeCliProtocol.ParseBackgroundSessions(document.RootElement));
    }
}
