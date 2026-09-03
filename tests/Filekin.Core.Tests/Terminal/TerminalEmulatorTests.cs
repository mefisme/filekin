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
    public void SelectedLinesAreReadableByAbsoluteIndexAfterScrolling()
    {
        var terminal = new TerminalEmulator(6, 2);

        Write(terminal, "one\r\ntwo\r\nthree\r\nfour");

        // Two lines have scrolled into scrollback, so the live viewport starts at absolute line 2.
        var screen = terminal.CreateSnapshot();
        Assert.AreEqual(2L, screen.FirstVisibleLine);

        // A whole-line range spanning scrollback and the live screen still resolves.
        Assert.AreEqual("one|two|three|four", string.Join('|', terminal.GetLines(0, 0, 3, 6)));

        // A partial range inside one line, with the end column exclusive.
        Assert.AreEqual("hre", string.Join('|', terminal.GetLines(2, 1, 2, 4)));

        // Reversed drag coordinates select the same text.
        Assert.AreEqual("hre", string.Join('|', terminal.GetLines(2, 4, 2, 1)));
    }

    [TestMethod]
    public void ClearedLinesDoNotResolveToLaterContent()
    {
        var terminal = new TerminalEmulator(6, 2);
        Write(terminal, "one\r\ntwo\r\nthree");

        // A full reset discards every retained line; absolute indices must stay monotonic so a stale
        // selection resolves to nothing rather than silently pointing at new output.
        Write(terminal, $"{Esc}c");
        Write(terminal, "new");

        Assert.IsEmpty(terminal.GetLines(0, 0, 0, 6));

        var live = terminal.CreateSnapshot().FirstVisibleLine;
        Assert.AreEqual("new", string.Join('|', terminal.GetLines(live, 0, live, 6)));
    }

    [TestMethod]
    public void MouseTrackingModesAreTrackedIndependently()
    {
        var terminal = new TerminalEmulator(8, 2);
        Assert.AreEqual(TerminalMouseTracking.None, terminal.MouseTracking);

        // Claude Code and other full-screen tools turn these on together at startup.
        Write(terminal, $"{Esc}[?1000h{Esc}[?1002h{Esc}[?1003h{Esc}[?1006h");
        Assert.AreEqual(TerminalMouseTracking.AnyEvent, terminal.MouseTracking);
        Assert.IsTrue(terminal.MouseSgrEncoding);

        // Turning off the widest mode falls back to the next one still enabled rather than to None.
        Write(terminal, $"{Esc}[?1003l");
        Assert.AreEqual(TerminalMouseTracking.ButtonEvent, terminal.MouseTracking);

        Write(terminal, $"{Esc}[?1002l{Esc}[?1000l");
        Assert.AreEqual(TerminalMouseTracking.None, terminal.MouseTracking);
    }

    [TestMethod]
    public void MouseReportsUseSgrWhenRequestedAndLegacyOtherwise()
    {
        // SGR carries the button, a one-based position, and press versus release in the final byte.
        Assert.AreEqual(
            $"{Esc}[<64;1;1M",
            TerminalMouseReport.Encode(TerminalMouseButton.WheelUp, true, false, 0, 0, false, false, false, true));
        Assert.AreEqual(
            $"{Esc}[<0;13;5m",
            TerminalMouseReport.Encode(TerminalMouseButton.Left, false, false, 12, 4, false, false, false, true));

        // Motion adds 32 and the modifier bits are shift 4, alt 8, control 16.
        Assert.AreEqual(
            $"{Esc}[<44;2;3M",
            TerminalMouseReport.Encode(TerminalMouseButton.Left, true, true, 1, 2, true, true, false, true));

        // The legacy encoding offsets by 32 and cannot say which button was released.
        Assert.AreEqual(
            $"{Esc}[M !!",
            TerminalMouseReport.Encode(TerminalMouseButton.Left, true, false, 0, 0, false, false, false, false));
        Assert.AreEqual(
            $"{Esc}[M#!!",
            TerminalMouseReport.Encode(TerminalMouseButton.Left, false, false, 0, 0, false, false, false, false));

        // It also cannot address a cell past column 223, which is why programs ask for SGR.
        Assert.IsNull(
            TerminalMouseReport.Encode(TerminalMouseButton.Left, true, false, 300, 0, false, false, false, false));
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

    [TestMethod]
    public void AFrameTheProgramAsksToDrawInOneGoIsNotShownHalfBuilt()
    {
        var terminal = new TerminalEmulator(8, 2);
        var draws = 0;
        terminal.ScreenChanged += (_, _) => draws++;

        // A tool that repaints itself opens the frame, sends it in whatever pieces the pipe
        // happens to deliver, and closes it. Only the closed frame belongs on screen.
        Write(terminal, Esc + "[?2026h");
        Write(terminal, "ab");
        Write(terminal, "cd");

        Assert.AreEqual(0, draws, "Nothing the program has not finished may be drawn.");
        Assert.IsTrue(terminal.IsSynchronizedFrameOpen);

        Write(terminal, Esc + "[?2026l");

        Assert.AreEqual(1, draws, "The finished frame is drawn once.");
        Assert.IsFalse(terminal.IsSynchronizedFrameOpen);
        Assert.AreEqual("abcd    ", Text(terminal.CreateSnapshot(), 0));
    }

    [TestMethod]
    public void AWholeFrameInOnePieceStillDrawsOnce()
    {
        var terminal = new TerminalEmulator(8, 2);
        var draws = 0;
        terminal.ScreenChanged += (_, _) => draws++;

        Write(terminal, Esc + "[?2026h" + "hi" + Esc + "[?2026l");

        Assert.AreEqual(1, draws);
        Assert.AreEqual("hi      ", Text(terminal.CreateSnapshot(), 0));
    }

    [TestMethod]
    public void AProgramThatDiesMidFrameDoesNotFreezeTheScreen()
    {
        var terminal = new TerminalEmulator(8, 2);
        var draws = 0;
        terminal.ScreenChanged += (_, _) => draws++;

        Write(terminal, Esc + "[?2026h");

        // Far past what any real frame holds, so the closing pair is never coming.
        terminal.Process(new byte[300 * 1024]);

        Assert.AreEqual(1, draws, "The screen gives up on the frame rather than stopping for good.");
        Assert.IsFalse(terminal.IsSynchronizedFrameOpen);

        Write(terminal, "back");

        Assert.AreEqual(2, draws, "And it keeps drawing afterwards.");
    }

    [TestMethod]
    public void AResetEndsAFrameTheProgramLeftOpen()
    {
        var terminal = new TerminalEmulator(8, 2);
        var draws = 0;
        terminal.ScreenChanged += (_, _) => draws++;

        Write(terminal, Esc + "[?2026h");
        Assert.AreEqual(0, draws);

        Write(terminal, Esc + "[!p");

        Assert.IsFalse(terminal.IsSynchronizedFrameOpen);
        Assert.AreEqual(1, draws);
    }

    private const string Esc = "\u001b";

    private static void Write(TerminalEmulator terminal, string text) =>
        terminal.Process(Encoding.UTF8.GetBytes(text));

    private static string Text(TerminalSnapshot snapshot, int row) =>
        string.Concat(Enumerable.Range(0, snapshot.Columns).Select(column => snapshot[row, column].Text));
}
