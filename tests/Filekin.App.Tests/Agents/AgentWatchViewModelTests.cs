using Filekin.App.ViewModels;
using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;

namespace Filekin.App.Tests.Agents;

/// <summary>
/// The screen a person watches Codex on while it works, and says something back from.
/// </summary>
/// <remarks>
/// It exists because Codex allows one client per conversation: while Filekin's App Server holds the
/// thread there is no CLI to open, so a row saying "working" was the whole of what a person could
/// read. Every line here comes from the App Server event stream Filekin is already given.
/// </remarks>
[TestClass]
public sealed class AgentWatchViewModelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 3, 14, 2, 0, TimeSpan.Zero);
    private static readonly string[] OneTrimmedMessage = ["check the resume path too"];

    [TestMethod]
    public void AnEmptyScreenSaysSoRatherThanShowingNothing()
    {
        var watch = Watching();

        Assert.IsTrue(watch.IsEmpty);
        Assert.IsFalse(watch.HasRows);
        StringAssert.Contains(
            watch.EmptyText,
            "Codex",
            StringComparison.Ordinal,
            "A blank panel reads as broken. It has to name whose work is missing.");
    }

    [TestMethod]
    public void EachThingTheAgentDoesBecomesALine()
    {
        var watch = Watching();

        watch.Take(Event("item-1", AgentSessionEventKind.Tool, "dotnet build"));
        watch.Take(Event("item-2", AgentSessionEventKind.Response, "The emulator ignored 2026."));

        Assert.AreEqual(2, watch.Rows.Count);
        Assert.AreEqual("dotnet build", watch.Rows[0].Text);
        Assert.AreEqual("$", watch.Rows[0].Who, "A command line is titled by the shell it ran in.");
        Assert.AreEqual("Codex", watch.Rows[1].Who);
    }

    [TestMethod]
    public void OneFactRevisedManyTimesStaysOneLine()
    {
        var watch = Watching();

        // A streamed answer and a running command each arrive as many updates to one fact. A line per
        // update would scroll a person off their own screen while the agent was still mid-sentence.
        watch.Take(Event("item-1", AgentSessionEventKind.Tool, "dotnet build", AgentSessionEventStatus.InProgress));
        watch.Take(Event(
            "item-1",
            AgentSessionEventKind.Tool,
            "dotnet build",
            AgentSessionEventStatus.Completed,
            detail: "Build succeeded. 0 warnings"));

        Assert.AreEqual(1, watch.Rows.Count, "The same id revises the line it already made.");
        Assert.IsFalse(watch.Rows[0].IsWaiting, "And the line stops saying it is still happening.");
        Assert.AreEqual("Build succeeded. 0 warnings", watch.Rows[0].Detail);
        Assert.IsTrue(watch.Rows[0].HasDetail);
    }

    [TestMethod]
    public void AFailureDoesNotReadLikeOrdinaryProgress()
    {
        var watch = Watching();

        watch.Take(Event("item-1", AgentSessionEventKind.Error, "The build failed.", AgentSessionEventStatus.Failed));

        Assert.IsTrue(watch.Rows[0].IsFailure);
        Assert.AreEqual("Error", watch.Rows[0].Who);
    }

    [TestMethod]
    public void ThePersonsOwnLineIsTheOneNoModelTextCanForge()
    {
        var watch = Watching();

        // Filekin writes this title itself when it sends a prompt. An agent that titled its own
        // message "You" would otherwise be able to put words in the person's mouth on their screen.
        watch.Take(Event("mine", AgentSessionEventKind.Message, "check the resume path too", title: "You"));
        watch.Take(Event("theirs", AgentSessionEventKind.Message, "looking now", title: "Codex"));

        Assert.AreEqual("You", watch.Rows[0].Who);
        Assert.AreEqual("Codex", watch.Rows[1].Who, "A message the agent titled is still the agent's.");
    }

    [TestMethod]
    public void NothingCanBeSaidToAnAgentThatIsNotHoldingTheTurn()
    {
        var watch = Watching(holdsTheTurn: false);
        watch.Draft = "are you nearly done";

        Assert.IsFalse(watch.CanSend, "There is no running turn for this to reach.");
        StringAssert.Contains(
            watch.SendHint,
            "not holding the turn",
            StringComparison.Ordinal,
            "A dead box has to say why, or it reads as broken.");
    }

    [TestMethod]
    public void AnEmptyLineIsNotSomethingToSend()
    {
        var watch = Watching();

        Assert.IsFalse(watch.CanSend);

        watch.Draft = "   ";

        Assert.IsFalse(watch.CanSend, "Blank space is not a message.");
    }

    [TestMethod]
    public async Task SendingClearsTheBoxOnlyOnceItHasGone()
    {
        var watch = Watching();
        watch.Draft = "  check the resume path too  ";
        var sent = new List<string>();

        await watch.SendAsync(text =>
        {
            sent.Add(text);
            return Task.FromResult<string?>(null);
        });

        CollectionAssert.AreEqual(
            OneTrimmedMessage,
            sent,
            "What is sent is trimmed, so a stray space is not part of the message.");
        Assert.AreEqual(string.Empty, watch.Draft);
        Assert.IsFalse(watch.HasStatus);
    }

    [TestMethod]
    public async Task ARefusedMessageIsKeptAndExplained()
    {
        var watch = Watching();
        watch.Draft = "are you nearly done";

        await watch.SendAsync(_ => Task.FromResult<string?>("Codex is not holding the turn."));

        Assert.AreEqual(
            "are you nearly done",
            watch.Draft,
            "A box emptied on a failed send loses what somebody typed.");
        Assert.AreEqual("Codex is not holding the turn.", watch.Status);
        Assert.IsTrue(watch.HasStatus);
    }

    [TestMethod]
    public async Task TheTurnMovingAwayClosesTheBox()
    {
        var watch = Watching();
        watch.Draft = "one more thing";
        Assert.IsTrue(watch.CanSend);

        watch.HoldsTheTurn = false;

        Assert.IsFalse(watch.CanSend, "The box follows the turn rather than the screen staying open.");
        await Task.CompletedTask;
    }

    [TestMethod]
    public void WhatIsAlreadyOnTheFeedIsThereBeforeTheScreenOpens()
    {
        // Somebody presses Watch part-way through a turn. Starting from the next event would show an
        // empty screen under an agent that has been working for a minute.
        var feed = new AgentSessionEventFeed();
        var watch = new AgentWatchViewModel(
            Guid.NewGuid(),
            AgentProvider.Codex,
            new AgentSessionObservation("11111111-1111-1111-1111-111111111111", feed, At),
            holdsTheTurn: true);

        watch.Take(Event("item-1", AgentSessionEventKind.Tool, "dotnet build"));

        Assert.AreEqual(1, watch.Rows.Count);
        watch.Dispose();
    }

    private static AgentWatchViewModel Watching(bool holdsTheTurn = true) =>
        new(
            Guid.NewGuid(),
            AgentProvider.Codex,
            new AgentSessionObservation("11111111-1111-1111-1111-111111111111", new AgentSessionEventFeed(), At),
            holdsTheTurn);

    private static AgentSessionEvent Event(
        string id,
        AgentSessionEventKind kind,
        string summary,
        AgentSessionEventStatus status = AgentSessionEventStatus.Completed,
        string? title = null,
        string? detail = null) =>
        new(id, At, kind, status, title ?? "Codex", summary, detail);
}
