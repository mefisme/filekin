using System.Globalization;
using System.Text;

namespace Filekin.Core.Terminal.Emulation;

/// <summary>
/// Streaming VT screen-state interpreter for ConPTY output. It maintains primary and alternate
/// cell buffers, cursor/margins, SGR attributes, input modes, and normal-buffer scrollback.
/// Rendering stays UI-specific; this type is deterministic and platform neutral.
/// </summary>
public sealed class TerminalEmulator
{
    private const int MaxSequenceLength = 256;
    private const int MaxScrollbackRows = 10_000;

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _sequence = new();
    private readonly StringBuilder _osc = new();
    private ScreenBuffer _primary;
    private ScreenBuffer _alternate;
    private ScreenBuffer _active;
    private ParserState _state;
    private bool _usingAlternate;
    private bool _cursorVisible = true;
    private bool _applicationCursorKeys;
    private bool _applicationKeypad;
    private bool _bracketedPaste;
    private char? _pendingHighSurrogate;

    public TerminalEmulator(int columns = 80, int rows = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _primary = new ScreenBuffer(columns, rows, keepScrollback: true);
        _alternate = new ScreenBuffer(columns, rows, keepScrollback: false);
        _active = _primary;
    }

    public event EventHandler? ScreenChanged;

    public event EventHandler<TerminalResponseEventArgs>? ResponseGenerated;

    public int Columns => _active.Columns;

    public int Rows => _active.Rows;

    public bool ApplicationCursorKeys => _applicationCursorKeys;

    public bool ApplicationKeypad => _applicationKeypad;

    public bool BracketedPaste => _bracketedPaste;

    public bool IsAlternateScreen => _usingAlternate;

    public void Process(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        _decoder.Convert(bytes, chars, flush: false, out _, out var charsUsed, out _);
        for (var index = 0; index < charsUsed; index++)
        {
            ProcessCharacter(chars[index]);
        }

        ScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        _primary.Resize(columns, rows);
        _alternate.Resize(columns, rows);
        ScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a viewport. <paramref name="scrollOffset"/> is the number of normal-buffer rows above
    /// the live bottom; it is ignored while an alternate screen is active.
    /// </summary>
    public TerminalSnapshot CreateSnapshot(int scrollOffset = 0)
    {
        var offset = _usingAlternate ? 0 : Math.Clamp(scrollOffset, 0, _primary.Scrollback.Count);
        var cells = _active.CopyViewport(offset);
        var liveCursor = offset == 0;
        return new TerminalSnapshot(
            Columns,
            Rows,
            cells,
            _active.CursorColumn,
            _active.CursorRow,
            liveCursor && _cursorVisible,
            _usingAlternate ? 0 : _primary.Scrollback.Count);
    }

    private void ProcessCharacter(char character)
    {
        switch (_state)
        {
            case ParserState.Ground:
                ProcessGround(character);
                break;
            case ParserState.Escape:
                ProcessEscape(character);
                break;
            case ParserState.Csi:
                ProcessCsi(character);
                break;
            case ParserState.Osc:
                ProcessOsc(character);
                break;
            case ParserState.OscEscape:
                if (character == '\\')
                {
                    FinishOsc();
                }
                else
                {
                    _osc.Append('\u001b').Append(character);
                    _state = ParserState.Osc;
                }

                break;
            case ParserState.IgnoreOne:
                _state = ParserState.Ground;
                break;
        }
    }

    private void ProcessGround(char character)
    {
        switch (character)
        {
            case '\u001b':
                FlushPendingSurrogate();
                _state = ParserState.Escape;
                return;
            case '\a':
                return;
            case '\b':
                FlushPendingSurrogate();
                _active.CancelPendingWrap();
                _active.CursorColumn = Math.Max(0, _active.CursorColumn - 1);
                return;
            case '\t':
                FlushPendingSurrogate();
                _active.CancelPendingWrap();
                _active.CursorColumn = Math.Min(Columns - 1, ((_active.CursorColumn / 8) + 1) * 8);
                return;
            case '\n':
            case '\v':
            case '\f':
                FlushPendingSurrogate();
                _active.LineFeed();
                return;
            case '\r':
                FlushPendingSurrogate();
                _active.CancelPendingWrap();
                _active.CursorColumn = 0;
                return;
        }

        if (char.IsControl(character))
        {
            return;
        }

        if (char.IsHighSurrogate(character))
        {
            FlushPendingSurrogate();
            _pendingHighSurrogate = character;
            return;
        }

        if (char.IsLowSurrogate(character) && _pendingHighSurrogate is { } high)
        {
            _pendingHighSurrogate = null;
            WriteText(string.Concat(high, character), Rune.GetRuneAt(string.Concat(high, character), 0));
            return;
        }

        FlushPendingSurrogate();
        WriteText(character.ToString(CultureInfo.InvariantCulture), new Rune(character));
    }

    private void ProcessEscape(char character)
    {
        _state = ParserState.Ground;
        switch (character)
        {
            case '[':
                _sequence.Clear();
                _state = ParserState.Csi;
                break;
            case ']':
                _osc.Clear();
                _state = ParserState.Osc;
                break;
            case '7':
                _active.SaveCursor();
                break;
            case '8':
                _active.RestoreCursor();
                break;
            case 'D':
                _active.LineFeed();
                break;
            case 'E':
                _active.CursorColumn = 0;
                _active.LineFeed();
                break;
            case 'M':
                _active.ReverseIndex();
                break;
            case 'c':
                Reset();
                break;
            case '=':
                _applicationKeypad = true;
                break;
            case '>':
                _applicationKeypad = false;
                break;
            case '(':
            case ')':
            case '*':
            case '+':
                _state = ParserState.IgnoreOne;
                break;
        }
    }

    private void ProcessCsi(char character)
    {
        if (character is >= '@' and <= '~')
        {
            ExecuteCsi(_sequence.ToString(), character);
            _sequence.Clear();
            _state = ParserState.Ground;
            return;
        }

        if (_sequence.Length >= MaxSequenceLength || character == '\u001b')
        {
            _sequence.Clear();
            _state = character == '\u001b' ? ParserState.Escape : ParserState.Ground;
            return;
        }

        _sequence.Append(character);
    }

    private void ProcessOsc(char character)
    {
        if (character == '\a')
        {
            FinishOsc();
            return;
        }

        if (character == '\u001b')
        {
            _state = ParserState.OscEscape;
            return;
        }

        if (_osc.Length < MaxSequenceLength * 4)
        {
            _osc.Append(character);
        }
    }

    private void FinishOsc()
    {
        // Window-title and hyperlink OSC commands do not alter the cell grid. Tab titles intentionally
        // describe launch intent rather than continuously tracking a shell-controlled title.
        _osc.Clear();
        _state = ParserState.Ground;
    }

    private void ExecuteCsi(string parameterText, char final)
    {
        // A leading <, =, > or ? marks a private-parameter sequence. Its final byte means something
        // different from the standard one, so it must never fall through to the shared handlers —
        // xterm's `CSI > 4 ; 2 m` (modifyOtherKeys) is not SGR 4 + SGR 2.
        var prefix = parameterText.Length > 0 && parameterText[0] is '<' or '=' or '>' or '?'
            ? parameterText[0]
            : '\0';
        var intermediates = parameterText.Where(static c => c is >= ' ' and <= '/').ToArray();
        var numericText = prefix == '\0' ? parameterText : parameterText[1..];
        if (intermediates.Length > 0)
        {
            numericText = new string(numericText.TakeWhile(static c => c is not (>= ' ' and <= '/')).ToArray());
        }

        var parameters = ParseParameters(numericText);
        int P(int index, int fallback = 1) => index < parameters.Length && parameters[index] > 0
            ? parameters[index]
            : fallback;

        if (prefix != '\0')
        {
            // DEC private mode set/reset is the only prefixed family that changes screen state here.
            // Keyboard-protocol, mouse-tracking and device-query requests are deliberately ignored.
            if (prefix == '?' && final is 'h' or 'l')
            {
                SetMode(parameters, final == 'h');
            }

            return;
        }

        switch (final)
        {
            case 'A':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Max(0, _active.CursorRow - P(0));
                break;
            case 'B':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Min(Rows - 1, _active.CursorRow + P(0));
                break;
            case 'C':
            case 'a':
                _active.CancelPendingWrap();
                _active.CursorColumn = Math.Min(Columns - 1, _active.CursorColumn + P(0));
                break;
            case 'D':
                _active.CancelPendingWrap();
                _active.CursorColumn = Math.Max(0, _active.CursorColumn - P(0));
                break;
            case 'E':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Min(Rows - 1, _active.CursorRow + P(0));
                _active.CursorColumn = 0;
                break;
            case 'F':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Max(0, _active.CursorRow - P(0));
                _active.CursorColumn = 0;
                break;
            case 'G':
            case '`':
                _active.CancelPendingWrap();
                _active.CursorColumn = Math.Clamp(P(0) - 1, 0, Columns - 1);
                break;
            case 'd':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Clamp(P(0) - 1, 0, Rows - 1);
                break;
            case 'H':
            case 'f':
                _active.CancelPendingWrap();
                _active.CursorRow = Math.Clamp(P(0) - 1, 0, Rows - 1);
                _active.CursorColumn = Math.Clamp(P(1) - 1, 0, Columns - 1);
                break;
            case 'J':
                _active.EraseDisplay(parameters.ElementAtOrDefault(0));
                break;
            case 'K':
                _active.EraseLine(parameters.ElementAtOrDefault(0));
                break;
            case 'L':
                _active.InsertLines(P(0));
                break;
            case 'M':
                _active.DeleteLines(P(0));
                break;
            case '@':
                _active.InsertCharacters(P(0));
                break;
            case 'P':
                _active.DeleteCharacters(P(0));
                break;
            case 'X':
                _active.EraseCharacters(P(0));
                break;
            case 'S':
                _active.ScrollUp(P(0));
                break;
            case 'T':
                _active.ScrollDown(P(0));
                break;
            case 'm':
                ApplySgr(parameters);
                break;
            case 'r':
                _active.SetMargins(P(0), parameters.Length > 1 && parameters[1] > 0 ? parameters[1] : Rows);
                break;
            case 's':
                _active.SaveCursor();
                break;
            case 'u':
                _active.RestoreCursor();
                break;
            case 'n' when P(0, 0) == 6:
                ResponseGenerated?.Invoke(
                    this,
                    new TerminalResponseEventArgs($"\u001b[{_active.CursorRow + 1};{_active.CursorColumn + 1}R"));
                break;
            case 'c':
                ResponseGenerated?.Invoke(this, new TerminalResponseEventArgs("\u001b[?1;0c"));
                break;
            case 'p' when intermediates.Contains('!'):
                SoftReset();
                break;
        }
    }

    private void ApplySgr(int[] parameters)
    {
        if (parameters.Length == 0)
        {
            parameters = [0];
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var value = parameters[index];
            switch (value)
            {
                case 0:
                    _active.ResetStyle();
                    break;
                case 1:
                    _active.Attributes |= TerminalAttributes.Bold;
                    break;
                case 2:
                    _active.Attributes |= TerminalAttributes.Dim;
                    break;
                case 3:
                    _active.Attributes |= TerminalAttributes.Italic;
                    break;
                case 4:
                    _active.Attributes |= TerminalAttributes.Underline;
                    break;
                case 7:
                    _active.Attributes |= TerminalAttributes.Inverse;
                    break;
                case 8:
                    _active.Attributes |= TerminalAttributes.Hidden;
                    break;
                case 9:
                    _active.Attributes |= TerminalAttributes.StrikeThrough;
                    break;
                case 22:
                    _active.Attributes &= ~(TerminalAttributes.Bold | TerminalAttributes.Dim);
                    break;
                case 23:
                    _active.Attributes &= ~TerminalAttributes.Italic;
                    break;
                case 24:
                    _active.Attributes &= ~TerminalAttributes.Underline;
                    break;
                case 27:
                    _active.Attributes &= ~TerminalAttributes.Inverse;
                    break;
                case 28:
                    _active.Attributes &= ~TerminalAttributes.Hidden;
                    break;
                case 29:
                    _active.Attributes &= ~TerminalAttributes.StrikeThrough;
                    break;
                case >= 30 and <= 37:
                    _active.Foreground = TerminalColor.Indexed((byte)(value - 30));
                    break;
                case 39:
                    _active.Foreground = TerminalColor.Default;
                    break;
                case >= 40 and <= 47:
                    _active.Background = TerminalColor.Indexed((byte)(value - 40));
                    break;
                case 49:
                    _active.Background = TerminalColor.Default;
                    break;
                case >= 90 and <= 97:
                    _active.Foreground = TerminalColor.Indexed((byte)(value - 90 + 8));
                    break;
                case >= 100 and <= 107:
                    _active.Background = TerminalColor.Indexed((byte)(value - 100 + 8));
                    break;
                case 38:
                    index = ApplyExtendedColor(parameters, index, foreground: true);
                    break;
                case 48:
                    index = ApplyExtendedColor(parameters, index, foreground: false);
                    break;
            }
        }
    }

    private int ApplyExtendedColor(int[] parameters, int index, bool foreground)
    {
        if (index + 2 < parameters.Length && parameters[index + 1] == 5)
        {
            var color = TerminalColor.Indexed((byte)Math.Clamp(parameters[index + 2], 0, 255));
            _active.SetColor(foreground, color);
            return index + 2;
        }

        if (index + 4 < parameters.Length && parameters[index + 1] == 2)
        {
            var color = TerminalColor.Rgb(
                (byte)Math.Clamp(parameters[index + 2], 0, 255),
                (byte)Math.Clamp(parameters[index + 3], 0, 255),
                (byte)Math.Clamp(parameters[index + 4], 0, 255));
            _active.SetColor(foreground, color);
            return index + 4;
        }

        return index;
    }

    private void SetMode(int[] parameters, bool enabled)
    {
        foreach (var parameter in parameters)
        {
            switch (parameter)
            {
                case 1:
                    _applicationCursorKeys = enabled;
                    break;
                case 25:
                    _cursorVisible = enabled;
                    break;
                case 47:
                case 1047:
                case 1049:
                    UseAlternateScreen(enabled, clear: parameter == 1049);
                    break;
                case 2004:
                    _bracketedPaste = enabled;
                    break;
            }
        }
    }

    private void UseAlternateScreen(bool enabled, bool clear)
    {
        if (enabled == _usingAlternate)
        {
            return;
        }

        if (enabled)
        {
            _primary.SaveCursor();
            if (clear)
            {
                _alternate.Clear();
            }

            _active = _alternate;
        }
        else
        {
            _active = _primary;
            _primary.RestoreCursor();
        }

        _usingAlternate = enabled;
    }

    private void Reset()
    {
        _primary.Clear();
        _alternate.Clear();
        _active = _primary;
        _usingAlternate = false;
        SoftReset();
    }

    private void SoftReset()
    {
        _cursorVisible = true;
        _applicationCursorKeys = false;
        _applicationKeypad = false;
        _bracketedPaste = false;
        _active.SetMargins(1, Rows);
        _active.ResetStyle();
        _active.CursorColumn = 0;
        _active.CursorRow = 0;
    }

    private void WriteText(string text, Rune rune)
    {
        if (IsCombining(rune))
        {
            _active.AppendCombining(text);
            return;
        }

        _active.Write(text, IsWide(rune) ? 2 : 1);
    }

    private void FlushPendingSurrogate()
    {
        if (_pendingHighSurrogate is not { } high)
        {
            return;
        }

        _pendingHighSurrogate = null;
        _active.Write(high.ToString(CultureInfo.InvariantCulture), 1);
    }

    private static int[] ParseParameters(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = text.Split(';');
        var result = new int[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            _ = int.TryParse(segments[index], NumberStyles.None, CultureInfo.InvariantCulture, out result[index]);
        }

        return result;
    }

    private static bool IsCombining(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsWide(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x1100 and <= 0x115F or
            0x2329 or 0x232A or
            >= 0x2E80 and <= 0xA4CF and not 0x303F or
            >= 0xAC00 and <= 0xD7A3 or
            >= 0xF900 and <= 0xFAFF or
            >= 0xFE10 and <= 0xFE19 or
            >= 0xFE30 and <= 0xFE6F or
            >= 0xFF00 and <= 0xFF60 or
            >= 0xFFE0 and <= 0xFFE6 or
            >= 0x1F300 and <= 0x1FAFF or
            >= 0x20000 and <= 0x3FFFD;
    }

    private enum ParserState
    {
        Ground,
        Escape,
        Csi,
        Osc,
        OscEscape,
        IgnoreOne,
    }

    private sealed class ScreenBuffer
    {
        private readonly bool _keepScrollback;
        private TerminalCell[] _cells;
        private int _savedColumn;
        private int _savedRow;
        private bool _wrapPending;

        public ScreenBuffer(int columns, int rows, bool keepScrollback)
        {
            Columns = columns;
            Rows = rows;
            _keepScrollback = keepScrollback;
            _cells = Enumerable.Repeat(TerminalCell.Empty, columns * rows).ToArray();
            ScrollBottom = rows - 1;
        }

        public int Columns { get; private set; }

        public int Rows { get; private set; }

        public int CursorColumn { get; set; }

        public int CursorRow { get; set; }

        public int ScrollTop { get; private set; }

        public int ScrollBottom { get; private set; }

        public TerminalColor Foreground { get; set; } = TerminalColor.Default;

        public TerminalColor Background { get; set; } = TerminalColor.Default;

        public TerminalAttributes Attributes { get; set; }

        public List<TerminalCell[]> Scrollback { get; } = [];

        public void Write(string text, int width)
        {
            if (_wrapPending)
            {
                CursorColumn = 0;
                LineFeed();
                _wrapPending = false;
            }

            if (width == 2 && CursorColumn == Columns - 1)
            {
                CursorColumn = 0;
                LineFeed();
            }

            ClearWideCellAt(CursorRow, CursorColumn);
            _cells[Index(CursorRow, CursorColumn)] = new TerminalCell(text, Foreground, Background, Attributes, false);
            if (width == 2 && CursorColumn + 1 < Columns)
            {
                ClearWideCellAt(CursorRow, CursorColumn + 1);
                _cells[Index(CursorRow, CursorColumn + 1)] = new TerminalCell(
                    string.Empty,
                    Foreground,
                    Background,
                    Attributes,
                    IsContinuation: true);
            }

            var next = CursorColumn + width;
            if (next >= Columns)
            {
                CursorColumn = Columns - 1;
                _wrapPending = true;
            }
            else
            {
                CursorColumn = next;
            }
        }

        public void AppendCombining(string text)
        {
            var column = _wrapPending ? CursorColumn : CursorColumn - 1;
            if (column < 0)
            {
                return;
            }

            if (_cells[Index(CursorRow, column)].IsContinuation)
            {
                column--;
            }

            if (column < 0)
            {
                return;
            }

            var cell = _cells[Index(CursorRow, column)];
            _cells[Index(CursorRow, column)] = cell with { Text = cell.Text + text };
        }

        public void LineFeed()
        {
            _wrapPending = false;
            if (CursorRow == ScrollBottom)
            {
                ScrollUp(1);
            }
            else
            {
                CursorRow = Math.Min(Rows - 1, CursorRow + 1);
            }
        }

        public void ReverseIndex()
        {
            _wrapPending = false;
            if (CursorRow == ScrollTop)
            {
                ScrollDown(1);
            }
            else
            {
                CursorRow = Math.Max(0, CursorRow - 1);
            }
        }

        public void ScrollUp(int count)
        {
            count = Math.Clamp(count, 1, ScrollBottom - ScrollTop + 1);
            for (var iteration = 0; iteration < count; iteration++)
            {
                if (_keepScrollback && ScrollTop == 0 && ScrollBottom == Rows - 1)
                {
                    Scrollback.Add(CopyRow(0));
                    if (Scrollback.Count > MaxScrollbackRows)
                    {
                        Scrollback.RemoveAt(0);
                    }
                }

                for (var row = ScrollTop; row < ScrollBottom; row++)
                {
                    CopyRow(row + 1, row);
                }

                ClearRow(ScrollBottom);
            }
        }

        public void ScrollDown(int count)
        {
            count = Math.Clamp(count, 1, ScrollBottom - ScrollTop + 1);
            for (var iteration = 0; iteration < count; iteration++)
            {
                for (var row = ScrollBottom; row > ScrollTop; row--)
                {
                    CopyRow(row - 1, row);
                }

                ClearRow(ScrollTop);
            }
        }

        public void EraseDisplay(int mode)
        {
            switch (mode)
            {
                case 0:
                    EraseLine(0);
                    for (var row = CursorRow + 1; row < Rows; row++)
                    {
                        ClearRow(row);
                    }

                    break;
                case 1:
                    EraseLine(1);
                    for (var row = 0; row < CursorRow; row++)
                    {
                        ClearRow(row);
                    }

                    break;
                case 2:
                case 3:
                    Array.Fill(_cells, TerminalCell.Empty);
                    if (mode == 3)
                    {
                        Scrollback.Clear();
                    }

                    break;
            }
        }

        public void EraseLine(int mode)
        {
            var start = mode == 0 ? CursorColumn : 0;
            var end = mode == 1 ? CursorColumn : Columns - 1;
            if (mode == 2)
            {
                start = 0;
                end = Columns - 1;
            }

            Fill(CursorRow, start, end, BlankWithCurrentStyle());
        }

        public void EraseCharacters(int count) =>
            Fill(CursorRow, CursorColumn, Math.Min(Columns - 1, CursorColumn + count - 1), BlankWithCurrentStyle());

        public void InsertCharacters(int count)
        {
            count = Math.Min(count, Columns - CursorColumn);
            Array.Copy(
                _cells,
                Index(CursorRow, CursorColumn),
                _cells,
                Index(CursorRow, CursorColumn + count),
                Columns - CursorColumn - count);
            Fill(CursorRow, CursorColumn, CursorColumn + count - 1, BlankWithCurrentStyle());
        }

        public void DeleteCharacters(int count)
        {
            count = Math.Min(count, Columns - CursorColumn);
            Array.Copy(
                _cells,
                Index(CursorRow, CursorColumn + count),
                _cells,
                Index(CursorRow, CursorColumn),
                Columns - CursorColumn - count);
            Fill(CursorRow, Columns - count, Columns - 1, BlankWithCurrentStyle());
        }

        public void InsertLines(int count)
        {
            if (CursorRow < ScrollTop || CursorRow > ScrollBottom)
            {
                return;
            }

            count = Math.Min(count, ScrollBottom - CursorRow + 1);
            for (var row = ScrollBottom; row >= CursorRow + count; row--)
            {
                CopyRow(row - count, row);
            }

            for (var row = CursorRow; row < CursorRow + count; row++)
            {
                ClearRow(row);
            }
        }

        public void DeleteLines(int count)
        {
            if (CursorRow < ScrollTop || CursorRow > ScrollBottom)
            {
                return;
            }

            count = Math.Min(count, ScrollBottom - CursorRow + 1);
            for (var row = CursorRow; row <= ScrollBottom - count; row++)
            {
                CopyRow(row + count, row);
            }

            for (var row = ScrollBottom - count + 1; row <= ScrollBottom; row++)
            {
                ClearRow(row);
            }
        }

        public void SetMargins(int topOneBased, int bottomOneBased)
        {
            var top = Math.Clamp(topOneBased - 1, 0, Rows - 1);
            var bottom = Math.Clamp(bottomOneBased - 1, 0, Rows - 1);
            if (top >= bottom)
            {
                top = 0;
                bottom = Rows - 1;
            }

            ScrollTop = top;
            ScrollBottom = bottom;
            CursorColumn = 0;
            CursorRow = 0;
            _wrapPending = false;
        }

        public void SaveCursor()
        {
            _savedColumn = CursorColumn;
            _savedRow = CursorRow;
        }

        public void RestoreCursor()
        {
            CursorColumn = Math.Clamp(_savedColumn, 0, Columns - 1);
            CursorRow = Math.Clamp(_savedRow, 0, Rows - 1);
            _wrapPending = false;
        }

        public void ResetStyle()
        {
            Foreground = TerminalColor.Default;
            Background = TerminalColor.Default;
            Attributes = TerminalAttributes.None;
        }

        public void CancelPendingWrap() => _wrapPending = false;

        public void SetColor(bool foreground, TerminalColor color)
        {
            if (foreground)
            {
                Foreground = color;
            }
            else
            {
                Background = color;
            }
        }

        public void Clear()
        {
            Array.Fill(_cells, TerminalCell.Empty);
            Scrollback.Clear();
            CursorColumn = 0;
            CursorRow = 0;
            ScrollTop = 0;
            ScrollBottom = Rows - 1;
            _savedColumn = 0;
            _savedRow = 0;
            _wrapPending = false;
            ResetStyle();
        }

        public void Resize(int columns, int rows)
        {
            if (columns == Columns && rows == Rows)
            {
                return;
            }

            var resized = Enumerable.Repeat(TerminalCell.Empty, columns * rows).ToArray();
            var copyColumns = Math.Min(columns, Columns);
            var copyRows = Math.Min(rows, Rows);
            for (var row = 0; row < copyRows; row++)
            {
                Array.Copy(_cells, row * Columns, resized, row * columns, copyColumns);
            }

            Columns = columns;
            Rows = rows;
            _cells = resized;
            CursorColumn = Math.Clamp(CursorColumn, 0, columns - 1);
            CursorRow = Math.Clamp(CursorRow, 0, rows - 1);
            ScrollTop = 0;
            ScrollBottom = rows - 1;
            _wrapPending = false;

            for (var index = 0; index < Scrollback.Count; index++)
            {
                var old = Scrollback[index];
                var row = Enumerable.Repeat(TerminalCell.Empty, columns).ToArray();
                Array.Copy(old, row, Math.Min(old.Length, columns));
                Scrollback[index] = row;
            }
        }

        public TerminalCell[] CopyViewport(int scrollOffset)
        {
            if (scrollOffset == 0 || !_keepScrollback)
            {
                return (TerminalCell[])_cells.Clone();
            }

            var combinedRowCount = Scrollback.Count + Rows;
            var firstRow = combinedRowCount - Rows - scrollOffset;
            var result = new TerminalCell[Columns * Rows];
            for (var row = 0; row < Rows; row++)
            {
                var sourceRow = firstRow + row;
                if (sourceRow < Scrollback.Count)
                {
                    Array.Copy(Scrollback[sourceRow], 0, result, row * Columns, Columns);
                }
                else
                {
                    Array.Copy(_cells, (sourceRow - Scrollback.Count) * Columns, result, row * Columns, Columns);
                }
            }

            return result;
        }

        private TerminalCell BlankWithCurrentStyle() =>
            new(" ", Foreground, Background, Attributes, IsContinuation: false);

        private void Fill(int row, int startColumn, int endColumn, TerminalCell cell)
        {
            startColumn = Math.Clamp(startColumn, 0, Columns - 1);
            endColumn = Math.Clamp(endColumn, 0, Columns - 1);
            for (var column = startColumn; column <= endColumn; column++)
            {
                ClearWideCellAt(row, column);
                _cells[Index(row, column)] = cell;
            }
        }

        private void ClearWideCellAt(int row, int column)
        {
            var index = Index(row, column);
            if (_cells[index].IsContinuation && column > 0)
            {
                _cells[index - 1] = TerminalCell.Empty;
            }
            else if (column + 1 < Columns && _cells[index + 1].IsContinuation)
            {
                _cells[index + 1] = TerminalCell.Empty;
            }
        }

        private void ClearRow(int row) =>
            Array.Fill(_cells, TerminalCell.Empty, Index(row, 0), Columns);

        private TerminalCell[] CopyRow(int row)
        {
            var copy = new TerminalCell[Columns];
            Array.Copy(_cells, Index(row, 0), copy, 0, Columns);
            return copy;
        }

        private void CopyRow(int source, int destination) =>
            Array.Copy(_cells, Index(source, 0), _cells, Index(destination, 0), Columns);

        private int Index(int row, int column) => (row * Columns) + column;
    }
}
