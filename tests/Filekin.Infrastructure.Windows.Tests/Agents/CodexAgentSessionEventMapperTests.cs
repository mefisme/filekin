using System.Text.Json;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class CodexAgentSessionEventMapperTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse(
        "2026-08-31T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [TestMethod]
    public void AgentMessageDeltasReplaceOneProviderNeutralResponse()
    {
        var mapper = new CodexAgentSessionEventMapper();

        var started = mapper.MapNotification(Notification(
            "item/started",
            """{"item":{"id":"answer-1","type":"agentMessage","text":""}}"""), ObservedAt);
        var first = mapper.MapNotification(Notification(
            "item/agentMessage/delta",
            """{"itemId":"answer-1","delta":"Hello"}"""), ObservedAt.AddSeconds(1));
        var second = mapper.MapNotification(Notification(
            "item/agentMessage/delta",
            """{"itemId":"answer-1","delta":" there"}"""), ObservedAt.AddSeconds(2));
        var completed = mapper.MapNotification(Notification(
            "item/completed",
            """{"item":{"id":"answer-1","type":"agentMessage","text":"Hello there"}}"""), ObservedAt.AddSeconds(3));

        Assert.AreEqual("codex:item:answer-1", started!.Id);
        Assert.AreEqual(started.Id, first!.Id);
        Assert.AreEqual("Hello there", second!.Summary);
        Assert.AreEqual(AgentSessionEventStatus.Completed, completed!.Status);
    }

    [TestMethod]
    public void CommandCompletionPreservesCommandAndActualOutput()
    {
        var mapper = new CodexAgentSessionEventMapper();
        _ = mapper.MapNotification(Notification(
            "item/started",
            """{"item":{"id":"command-1","type":"commandExecution","command":"dotnet test","status":"inProgress"}}"""), ObservedAt);
        var output = mapper.MapNotification(Notification(
            "item/commandExecution/outputDelta",
            """{"itemId":"command-1","delta":"Passed!"}"""), ObservedAt.AddSeconds(1));

        var completed = mapper.MapNotification(Notification(
            "item/completed",
            """{"item":{"id":"command-1","type":"commandExecution","command":"dotnet test","status":"completed"}}"""), ObservedAt.AddSeconds(2));

        Assert.AreEqual(AgentSessionEventKind.Tool, completed!.Kind);
        Assert.AreEqual(AgentSessionEventStatus.Completed, completed.Status);
        StringAssert.Contains(output!.Detail, "dotnet test");
        StringAssert.Contains(completed.Detail, "dotnet test");
        StringAssert.Contains(completed.Detail, "Passed!");
    }

    [TestMethod]
    public void ApprovalRequestCarriesAnExplicitProviderRequest()
    {
        using var document = JsonDocument.Parse(
            """{"reason":"Run tests","command":["dotnet","test"],"cwd":"D:\\GitHub\\filekin"}""");

        var sessionEvent = CodexAgentSessionEventMapper.MapRequest(
            new CodexAppServerRequest(
                17,
                "item/commandExecution/requestApproval",
                document.RootElement.Clone()),
            ObservedAt);

        Assert.AreEqual(AgentSessionEventKind.Question, sessionEvent.Kind);
        Assert.AreEqual(AgentSessionEventStatus.NeedsAttention, sessionEvent.Status);
        Assert.AreEqual(17, sessionEvent.PendingRequest!.Id);
        Assert.AreEqual(AgentSessionRequestKind.Approval, sessionEvent.PendingRequest.Kind);
        StringAssert.Contains(sessionEvent.Detail, "dotnet test");
    }

    [TestMethod]
    public void UserInputRequestPreservesQuestionsAndOptions()
    {
        using var document = JsonDocument.Parse(
            """{"questions":[{"id":"target","question":"Which target?","options":[{"label":"Debug","description":"Fast"},{"label":"Release","description":"Final"}]}]}""");

        var sessionEvent = CodexAgentSessionEventMapper.MapRequest(
            new CodexAppServerRequest(23, "item/tool/requestUserInput", document.RootElement.Clone()),
            ObservedAt);

        var request = sessionEvent.PendingRequest!;
        Assert.AreEqual(AgentSessionRequestKind.UserInput, request.Kind);
        Assert.AreEqual("Which target?", request.Questions[0].Prompt);
        Assert.HasCount(2, request.Questions[0].Options);
        Assert.AreEqual("Debug", request.Questions[0].Options[0]);
        Assert.AreEqual("Release", request.Questions[0].Options[1]);
    }

    [TestMethod]
    public void ErrorNotificationBecomesAFailedErrorRow()
    {
        var mapper = new CodexAgentSessionEventMapper();

        var sessionEvent = mapper.MapNotification(Notification(
            "error",
            """{"error":{"message":"The provider failed."}}"""), ObservedAt);

        Assert.AreEqual(AgentSessionEventKind.Error, sessionEvent!.Kind);
        Assert.AreEqual(AgentSessionEventStatus.Failed, sessionEvent.Status);
        Assert.AreEqual("The provider failed.", sessionEvent.Summary);
    }

    private static CodexAppServerNotification Notification(string method, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new CodexAppServerNotification(method, document.RootElement.Clone());
    }
}
