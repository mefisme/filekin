using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class ClaudeBackgroundAgentListTests
{
    // Recorded from `claude agents --json` on a real machine. Two things here were found the hard
    // way: a background session carries two different identities, and "done" does not mean gone.
    private const string Reported = """
    [
      {
        "pid": 48964,
        "cwd": "D:\\github\\filekin",
        "kind": "interactive",
        "sessionId": "69b9755f-5cae-4487-be92-38a27adffbe7",
        "name": "Filekin Work",
        "status": "busy"
      },
      {
        "pid": 33480,
        "id": "64be37e4",
        "cwd": "D:\\GitHub\\agent-test",
        "kind": "background",
        "startedAt": 1788294584616,
        "sessionId": "64be37e4-553a-49f0-8aa6-664942a96882",
        "name": "Filekin agent-test",
        "status": "idle",
        "state": "done"
      },
      {
        "id": "6fb04d44",
        "cwd": "D:\\Doom-RE",
        "kind": "background",
        "sessionId": "6fb04d44-95de-4c3c-b9f5-9561bbc09f54",
        "name": "(backgrounded)",
        "state": "blocked"
      }
    ]
    """;

    private static ClaudeBackgroundAgent Find(string predicateId) =>
        ClaudeCliProtocol.ParseBackgroundAgents(Reported).Single(agent => agent.Id == predicateId);

    [TestMethod]
    public void TheAttachHandleIsNotTheConversationId()
    {
        var session = Find("64be37e4");

        Assert.AreEqual("64be37e4", session.Id, "claude attach takes the short handle.");
        Assert.AreEqual("64be37e4-553a-49f0-8aa6-664942a96882", session.SessionId);
        Assert.AreNotEqual(session.Id, session.SessionId, "Filekin stores the conversation, not the handle.");
    }

    [TestMethod]
    public void EveryReportedAgentIsRead() =>
        Assert.AreEqual(3, ClaudeCliProtocol.ParseBackgroundAgents(Reported).Count);

    [TestMethod]
    public void ADoneSessionIsStillRunningAndStillAttachable()
    {
        // "done" describes the turn, not the process. This exact entry reported "done" while its
        // process was alive and still holding its own Filekin MCP writer, after Filekin had closed.
        // Refusing it would refuse the ordinary case: an idle session waiting to be opened.
        var session = Find("64be37e4");

        Assert.AreEqual("done", session.State);
        Assert.AreEqual(33480, session.Pid);
        Assert.IsTrue(session.IsLiveBackgroundSession, "A finished turn is not a finished session.");
    }

    [TestMethod]
    public void AnEntryWithNoProcessIsNotAttachable() =>
        Assert.IsFalse(
            Find("6fb04d44").IsLiveBackgroundSession,
            "Claude reports no pid for a session that is not running.");

    [TestMethod]
    public void AnInteractiveSessionIsNotFilekinsToOpen() =>
        Assert.IsFalse(
            ClaudeCliProtocol.ParseBackgroundAgents(Reported)
                .Single(agent => agent.Name == "Filekin Work")
                .IsLiveBackgroundSession,
            "An interactive session is somebody's own terminal, not a Filekin background session.");

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("[]")]
    public void AnEmptyAnswerIsNoSessions(string output) =>
        Assert.AreEqual(0, ClaudeCliProtocol.ParseBackgroundAgents(output).Count);

    [TestMethod]
    [DataRow("{}")]
    [DataRow("not json at all")]
    public void AnAnswerFilekinCannotReadNeverYieldsAnId(string output)
    {
        try
        {
            Assert.AreEqual(0, ClaudeCliProtocol.ParseBackgroundAgents(output).Count);
        }
        catch (System.Text.Json.JsonException)
        {
            // Unreadable output reaches the caller as a failure, which becomes "no session to open".
            // What must never happen is a wrong id reaching a command line.
        }
    }
}
