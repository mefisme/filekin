using System.Text.Json;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class CodexAppServerProtocolTests
{
    [TestMethod]
    public void ParseAccountDistinguishesChatGptSubscriptionFromApiKeyMode()
    {
        using var chatGpt = JsonDocument.Parse(
            """{"account":{"type":"chatgpt","planType":"plus"},"requiresOpenaiAuth":true}""");
        using var apiKey = JsonDocument.Parse(
            """{"account":{"type":"apiKey","planType":null},"requiresOpenaiAuth":true}""");

        var subscription = CodexAppServerProtocol.ParseAccount(chatGpt.RootElement);
        var billedApi = CodexAppServerProtocol.ParseAccount(apiKey.RootElement);

        Assert.IsTrue(subscription.UsesChatGptSubscription);
        Assert.AreEqual("plus", subscription.PlanType);
        Assert.IsFalse(billedApi.UsesChatGptSubscription);
    }

    [TestMethod]
    public void ParseRateLimitsKeepsPrimaryAndSecondaryWindowsSeparate()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "primary": { "usedPercent": 4, "windowDurationMins": 300, "resetsAt": 1787974347 },
                  "secondary": { "usedPercent": 1, "windowDurationMins": 10080, "resetsAt": 1788561147 }
                }
              }
            }
            """);
        var observedAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var snapshot = CodexAppServerProtocol.ParseRateLimits(document.RootElement, observedAt);

        Assert.AreEqual(AgentProvider.Codex, snapshot.Provider);
        Assert.AreEqual(observedAt, snapshot.ObservedAt);
        Assert.HasCount(2, snapshot.Windows);
        Assert.AreEqual("codex:primary", snapshot.Windows[0].Name);
        Assert.AreEqual(4, snapshot.Windows[0].UsedPercent);
        Assert.AreEqual(TimeSpan.FromMinutes(300), snapshot.Windows[0].WindowDuration);
        Assert.AreEqual("codex:secondary", snapshot.Windows[1].Name);
        Assert.AreEqual(1, snapshot.Windows[1].UsedPercent);
        Assert.AreEqual(TimeSpan.FromMinutes(10080), snapshot.Windows[1].WindowDuration);
    }

    [TestMethod]
    public void ParseRateLimitsFallsBackToTheLegacySingleBucketShape()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": { "usedPercent": 25, "windowDurationMins": 15, "resetsAt": 1730947200 },
                "secondary": null
              }
            }
            """);

        var snapshot = CodexAppServerProtocol.ParseRateLimits(document.RootElement, DateTimeOffset.UtcNow);

        Assert.HasCount(1, snapshot.Windows);
        Assert.AreEqual("codex:primary", snapshot.Windows[0].Name);
        Assert.AreEqual(75, snapshot.Windows[0].RemainingPercent);
    }

    [TestMethod]
    public void MissingRateLimitsProducesAnHonestUnknownSnapshot()
    {
        using var document = JsonDocument.Parse("{}");

        var snapshot = CodexAppServerProtocol.ParseRateLimits(document.RootElement, DateTimeOffset.UtcNow);

        Assert.IsFalse(snapshot.IsKnown);
        Assert.IsNull(snapshot.MinimumRemainingPercent);
    }

    [TestMethod]
    public void ParseThreadUsesTheNativeSessionIdAndFallsBackToThreadIdentity()
    {
        using var explicitSession = JsonDocument.Parse(
            """{"thread":{"id":"thr_123","sessionId":"session_123","name":"Filekin work"}}""");
        using var fallbackSession = JsonDocument.Parse(
            """{"thread":{"id":"thr_456","sessionId":null,"name":null}}""");

        var first = CodexAppServerProtocol.ParseThread(explicitSession.RootElement);
        var second = CodexAppServerProtocol.ParseThread(fallbackSession.RootElement);

        Assert.AreEqual("thr_123", first.ThreadId);
        Assert.AreEqual("session_123", first.SessionId);
        Assert.AreEqual("Filekin work", first.Name);
        Assert.AreEqual("thr_456", second.SessionId);
    }

    [TestMethod]
    public void TurnCompletedNotificationPreservesFailureDetails()
    {
        using var parameters = JsonDocument.Parse(
            """
            {
              "threadId": "thr_123",
              "turn": {
                "id": "turn_456",
                "status": "failed",
                "error": { "message": "Provider unavailable" }
              }
            }
            """);
        var notification = new CodexAppServerNotification(
            "turn/completed",
            parameters.RootElement.Clone());

        var parsed = CodexAppServerProtocol.TryParseTurnCompletion(notification, out var completion);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(completion);
        Assert.AreEqual("thr_123", completion.ThreadId);
        Assert.AreEqual("turn_456", completion.TurnId);
        Assert.AreEqual("failed", completion.Status);
        Assert.AreEqual("Provider unavailable", completion.ErrorMessage);
    }

    [TestMethod]
    public void DispatchParametersLeaveNativeApprovalAndSandboxPolicyUntouched()
    {
        var folder = Path.GetFullPath("project");
        var thread = CodexAppServerProtocol.CreateThreadStartParameters(folder);
        var turn = CodexAppServerProtocol.CreateTurnStartParameters("thr_123", folder, "Do the work.");

        Assert.AreEqual(folder, thread.GetProperty("cwd").GetString());
        Assert.AreEqual("filekin", thread.GetProperty("serviceName").GetString());
        Assert.IsFalse(thread.TryGetProperty("approvalPolicy", out _));
        Assert.IsFalse(thread.TryGetProperty("sandbox", out _));
        Assert.IsFalse(turn.TryGetProperty("approvalPolicy", out _));
        Assert.IsFalse(turn.TryGetProperty("sandboxPolicy", out _));
    }

    [TestMethod]
    public void ApprovalRequestIsRecognizedAsAServerRequest()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "id": 41,
              "method": "item/commandExecution/requestApproval",
              "params": {
                "threadId": "thr_123",
                "turnId": "turn_456",
                "itemId": "item_789",
                "command": ["dotnet", "test"]
              }
            }
            """);

        var parsed = CodexAppServerProtocol.TryParseServerRequest(
            document.RootElement,
            out var request);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(request);
        Assert.AreEqual(41, request.Id);
        Assert.AreEqual("item/commandExecution/requestApproval", request.Method);
        Assert.AreEqual("thr_123", request.Parameters.GetProperty("threadId").GetString());
    }
}
