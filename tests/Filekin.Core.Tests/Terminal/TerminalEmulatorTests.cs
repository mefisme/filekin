using System.Text;
using Filekin.Core.Terminal.Emulation;

namespace Filekin.Core.Tests.Terminal;

[TestClass]
public sealed class TerminalEmulatorTests
{
    [TestMethod]
    public void PlainTextAndControlCharactersUpdateCellsAndCursor()
    {
        var terminal = new TerminalEmulator(8, 3);

        Write(terminal, "one\r\ntwo\bX\t!");

        var screen = terminal.CreateSnapshot();
        Assert.AreEqual("one", Text(screen, 0).TrimEnd());
        Assert.AreEqual("twX    !", Text(screen, 1));
        Assert.AreEqual(7, screen.CursorColumn);
        Assert.AreEqual(1, screen.CursorRow);
    }

    [TestMethod]
    public void SplitUtf8AndSplitCsiSequencesAreInterpretedAsOneStream()
    {
        var terminal = new TerminalEmulator(8, 2);
        var bytes = Encoding.UTF8.GetBytes("A🙂B\u001b[1DZ");

        terminal.Process(bytes.AsSpan(0, 3));
        terminal.Process(bytes.AsSpan(3, 4));
        terminal.Process(bytes.AsSpan(7, bytes.Length - 7));

        var screen = terminal.CreateSnapshot();
        Assert.AreEqual("A", screen[0, 0].Text);
        Assert.AreEqual("🙂", screen[0, 1].Text);
        Assert.AreEqual("Z", screen[0, 3].Text);
        Assert.IsTrue(screen[0, 2].IsContinuation);
    }

    [TestMethod]
    public void SgrSupportsAnsiIndexedAndTrueColorAttributes()
    {
        var terminal = new TerminalEmulator(8, 2);

        Write(terminal, "\u001b[1;31;48;2;1;2;3mX\u001b[0mY");

        var screen = terminal.CreateSnapshot();
        var styled = screen[0, 0];
        Assert.IsTrue(styled.Attributes.HasFlag(TerminalAttributes.Bold));
        Assert.AreEqual(TerminalColor.Indexed(1), styled.Foreground);
        Assert.AreEqual(TerminalColor.Rgb(1, 2, 3), styled.Background);
        Assert.AreEqual(TerminalAttributes.None, screen[0, 1].Attributes);
        Assert.AreEqual(TerminalColor.Default, screen[0, 1].Foreground);
    }

    [TestMethod]
    public void CursorMovementAndEraseReplaceScreenCells()
    {
        var terminal = new TerminalEmulator(6, 2);

        Write(terminal, "abcdef\u001b[3D\u001b[KZ");

        var screen = terminal.CreateSnapshot();
        Assert.AreEqual("abZ   ", Text(screen, 0));
    }

    [TestMethod]
    public void FullScreenScrollCreatesInspectableScrollback()
    {
        var terminal = new TerminalEmulator(5, 2);

        Write(terminal, "one\r\ntwo\r\nthree");

        var live = terminal.CreateSnapshot();
        Assert.AreEqual(1, live.ScrollbackCount);
        Assert.AreEqual("two", Text(live, 0).TrimEnd());
        Assert.AreEqual("three", Text(live, 1).TrimEnd());

        var scrolled = terminal.CreateSnapshot(scrollOffset: 1);
        Assert.AreEqual("one", Text(scrolled, 0).TrimEnd());
        Assert.AreEqual("two", Text(scrolled, 1).TrimEnd());
        Assert.IsFalse(scrolled.CursorVisible);
    }

    [TestMethod]
    public void AlternateScreenDoesNotDestroyPrimaryScreen()
    {
        var terminal = new TerminalEmulator(8, 2);

        Write(terminal, "prompt\u001b[?1049happ\u001b[?1049l");

        var screen = terminal.CreateSnapshot();
        Assert.IsFalse(terminal.IsAlternateScreen);
        Assert.AreEqual("prompt", Text(screen, 0).TrimEnd());
    }

    [TestMethod]
    public void ModesAndTerminalQueriesAreSurfacedToTheHost()
    {
        var terminal = new TerminalEmulator(8, 2);
        var responses = new List<string>();
        terminal.ResponseGenerated += (_, e) => responses.Add(e.Response);

        Write(terminal, "A\u001b[?1h\u001b[?2004h\u001b[6n\u001b[c");

        Assert.IsTrue(terminal.ApplicationCursorKeys);
        Assert.IsTrue(terminal.BracketedPaste);
        Assert.HasCount(2, responses);
        Assert.AreEqual("\u001b[1;2R", responses[0]);
        Assert.AreEqual("\u001b[?1;0c", responses[1]);
    }

    [TestMethod]
    public void PrivateParameterSequencesAreNotTreatedAsStandardCommands()
    {
        var terminal = new TerminalEmulator(8, 2);
        var responses = new List<string>();
        terminal.ResponseGenerated += (_, e) => responses.Add(e.Response);

        // CSI > 4 ; 2 m is xterm modifyOtherKeys, not SGR 4 (underline) + SGR 2 (dim). Claude Code
        // and other TUIs send it at startup; misreading it styles the whole screen.
        Write(terminal, $"{Esc}[>4;2m{Esc}[>1u{Esc}[<u{Esc}[>0qA");

        var screen = terminal.CreateSnapshot();
        Assert.AreEqual("A", screen[0, 0].Text);
        Assert.AreEqual(TerminalAttributes.None, screen[0, 0].Attributes);
        Assert.IsEmpty(responses);
    }

    [TestMethod]
    public void PrivateModeSetAndResetStillReachTheModeHandlers()
    {
        var terminal = new TerminalEmulator(8, 2);

        Write(terminal, $"{Esc}[?1049h");
        Assert.IsTrue(terminal.IsAlternateScreen);

        Write(terminal, $"{Esc}[?1049l");
        Assert.IsFalse(terminal.IsAlternateScreen);
    }

    [TestMethod]
    public void ResizePreservesVisibleContentAndClampsCursor()
    {
        var terminal = new TerminalEmulator(8, 3);
        Write(terminal, "hello");

        terminal.Resize(4, 2);

        var screen = terminal.CreateSnapshot();
        Assert.AreEqual(4, screen.Columns);
        Assert.AreEqual(2, screen.Rows);
        Assert.AreEqual("hell", Text(screen, 0));
        Assert.AreEqual(3, screen.CursorColumn);
    }

    private const string Esc = "\u001b";

    private static void Write(TerminalEmulator terminal, string text) =>
        terminal.Process(Encoding.UTF8.GetBytes(text));

    private static string Text(TerminalSnapshot snapshot, int row) =>
        string.Concat(Enumerable.Range(0, snapshot.Columns).Select(column => snapshot[row, column].Text));
}
