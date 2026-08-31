using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.Infrastructure.Windows.Tests.Agents;

[TestClass]
public sealed class AgentSessionEventFeedTests
{
    [TestMethod]
    public void SnapshotReplaysRowsAndRepeatedIdsReplaceTheirPriorSnapshot()
    {
        var feed = new AgentSessionEventFeed();
        var received = new List<AgentSessionEvent>();
        feed.EventReceived += (_, sessionEvent) => received.Add(sessionEvent);
        var at = DateTimeOffset.Parse("2026-08-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        feed.Publish(new AgentSessionEvent(
            "tool-1",
            at,
            AgentSessionEventKind.Tool,
            AgentSessionEventStatus.InProgress,
            "Command",
            "Running command…"));
        feed.Publish(new AgentSessionEvent(
            "tool-1",
            at,
            AgentSessionEventKind.Tool,
            AgentSessionEventStatus.Completed,
            "Command",
            "Command completed."));

        var snapshot = feed.Snapshot();
        Assert.HasCount(1, snapshot);
        Assert.AreEqual(AgentSessionEventStatus.Completed, snapshot[0].Status);
        Assert.HasCount(2, received, "Observers receive each immutable replacement.");
    }
}
