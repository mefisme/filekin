using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Filekin.App.Theming;
using Filekin.App.ViewModels;
using Filekin.Core.Terminal;
using Filekin.Core.Terminal.Emulation;
using Filekin.Infrastructure.Windows.Input;

namespace Filekin.App.Controls;

/// <summary>A keyboard-input and cell-rendering WPF surface for one hosted terminal session.</summary>
public sealed class TerminalControl : FrameworkElement
{
    private const double TerminalFontSize = 14;
    private const double HorizontalPadding = 14;
    private const double VerticalPadding = 10;
    private const string Escape = "\u001b";

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(TerminalTabViewModel),
        typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, OnSessionChanged));

    /// <summary>Scrollback lines available above the live screen. Zero means nothing to scroll.</summary>
    public static readonly DependencyProperty ScrollMaximumProperty = DependencyProperty.Register(
        nameof(ScrollMaximum),
        typeof(double),
        typeof(TerminalControl),
        new FrameworkPropertyMetadata(0d));

    /// <summary>
    /// Scroll position for a bound scrollbar, counted from the oldest retained line. It equals
    /// <see cref="ScrollMaximum"/> when the live bottom of the screen is showing.
    /// </summary>
    public static readonly DependencyProperty ScrollValueProperty = DependencyProperty.Register(
        nameof(ScrollValue),
        typeof(double),
        typeof(TerminalControl),
        new FrameworkPropertyMetadata(0d));

    /// <summary>Visible rows, for a bound scrollbar's thumb size and page step.</summary>
    public static readonly DependencyProperty ViewportLinesProperty = DependencyProperty.Register(
        nameof(ViewportLines),
        typeof(double),
        typeof(TerminalControl),
        new FrameworkPropertyMetadata(1d));

    private readonly Typeface _regularTypeface = new("Cascadia Mono, Consolas");
    private readonly Typeface _boldTypeface = new(
        new FontFamily("Cascadia Mono, Consolas"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);
    private readonly Dictionary<Color, Brush> _brushes = [];
    private readonly Dictionary<Color, Pen> _pens = [];
    private readonly GlyphTypeface? _regularGlyphs;
    private readonly GlyphTypeface? _boldGlyphs;
    private readonly List<ushort> _runIndices = [];
    private readonly List<double> _runAdvances = [];
    private readonly List<ushort> _clusterIndices = [];
    private readonly List<double> _clusterAdvances = [];
    private double _cellWidth = 8.5;
    private double _cellHeight = 18;
    private double _baseline = 14;
    private int _scrollOffset;
    private int _lastScrollbackCount;
    private long _anchorLine;
    private int _anchorColumn;
    private long _focusLine;
    private int _focusColumn;
    private bool _hasSelection;
    private bool _isSelecting;
    private bool _wasAlternateScreen;
    private (int Column, int Row) _lastReportedCell = (-1, -1);

    public TerminalControl()
    {
        _regularGlyphs = _regularTypeface.TryGetGlyphTypeface(out var regular) ? regular : null;
        _boldGlyphs = _boldTypeface.TryGetGlyphTypeface(out var bold) ? bold : null;
        Focusable = true;
        Cursor = Cursors.IBeam;
        SnapsToDevicePixels = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    // The palette is static and would outlive this control, so the handler is attached only while the
    // control is in the tree: a closed terminal tab must not be kept alive by it. The paired
    // unsubscribe first is what makes a re-Loaded control subscribe exactly once.
    private void OnUnloaded(object sender, RoutedEventArgs e) => TerminalPalette.Changed -= OnPaletteChanged;

    private void OnPaletteChanged(object? sender, EventArgs e) => InvalidateVisual();

    public TerminalTabViewModel? Session
    {
        get => (TerminalTabViewModel?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public double ScrollMaximum
    {
        get => (double)GetValue(ScrollMaximumProperty);
        set => SetValue(ScrollMaximumProperty, value);
    }

    public double ScrollValue
    {
        get => (double)GetValue(ScrollValueProperty);
        set => SetValue(ScrollValueProperty, value);
    }

    public double ViewportLines
    {
        get => (double)GetValue(ViewportLinesProperty);
        set => SetValue(ViewportLinesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brush(TerminalPalette.Background), null, new Rect(RenderSize));

        if (Session is not { } session)
        {
            return;
        }

        var snapshot = session.Emulator.CreateSnapshot(_scrollOffset);
        UpdateScrollMetrics(snapshot);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var run = new StringBuilder();
        for (var row = 0; row < snapshot.Rows; row++)
        {
            var y = VerticalPadding + (row * _cellHeight);
            var line = snapshot.FirstVisibleLine + row;
            var column = 0;
            while (column < snapshot.Columns)
            {
                var cell = snapshot[row, column];
                if (cell.IsContinuation)
                {
                    column++;
                    continue;
                }

                // Adjacent cells that share style are drawn as one run. A per-cell FormattedText
                // means a full text layout for every character on screen, which cannot keep up with
                // a scrolling shell or a redrawing TUI. Selection is part of the run key so a
                // highlighted span breaks the run exactly where the selection starts and ends.
                var selected = IsSelected(line, column);
                var isWide = column + 1 < snapshot.Columns && snapshot[row, column + 1].IsContinuation;
                var width = 1;
                run.Clear();
                run.Append(cell.Text);
                if (!isWide)
                {
                    while (column + width < snapshot.Columns)
                    {
                        var next = snapshot[row, column + width];
                        var nextIsWide = column + width + 1 < snapshot.Columns
                            && snapshot[row, column + width + 1].IsContinuation;
                        if (next.IsContinuation
                            || nextIsWide
                            || !SameStyle(cell, next)
                            || IsSelected(line, column + width) != selected)
                        {
                            break;
                        }

                        run.Append(next.Text);
                        width++;
                    }
                }

                var x = HorizontalPadding + (column * _cellWidth);
                var cellAdvance = (isWide ? 2 : 1) * _cellWidth;
                var spanWidth = isWide ? cellAdvance : width * _cellWidth;
                DrawRun(drawingContext, run.ToString(), cell, x, y, spanWidth, cellAdvance, pixelsPerDip, selected);
                column += isWide ? 2 : width;
            }
        }

        if (snapshot.CursorVisible && IsKeyboardFocusWithin)
        {
            var cursorRect = new Rect(
                HorizontalPadding + (snapshot.CursorColumn * _cellWidth),
                VerticalPadding + (snapshot.CursorRow * _cellHeight) + _cellHeight - 2,
                _cellWidth,
                2);
            drawingContext.DrawRectangle(Brush(TerminalPalette.Cursor), null, cursorRect);
        }
    }

    private static bool SameStyle(TerminalCell first, TerminalCell second) =>
        first.Attributes == second.Attributes
        && first.Foreground.Equals(second.Foreground)
        && first.Background.Equals(second.Background);

    private void DrawRun(
        DrawingContext drawingContext,
        string text,
        TerminalCell style,
        double x,
        double y,
        double width,
        double cellAdvance,
        double pixelsPerDip,
        bool selected)
    {
        var foreground = ResolveColor(style.Foreground, isForeground: true);
        var background = ResolveColor(style.Background, isForeground: false);
        if (style.Attributes.HasFlag(TerminalAttributes.Inverse))
        {
            (foreground, background) = (background, foreground);
        }

        if (selected)
        {
            background = TerminalPalette.Selection;
        }

        if (background.A > 0)
        {
            drawingContext.DrawRectangle(Brush(background), null, new Rect(x, y, width, _cellHeight));
        }

        if (style.Attributes.HasFlag(TerminalAttributes.Hidden) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (style.Attributes.HasFlag(TerminalAttributes.Dim))
        {
            foreground.A = 150;
        }

        var brush = Brush(foreground);
        var isBold = style.Attributes.HasFlag(TerminalAttributes.Bold);
        var typeface = isBold ? _boldTypeface : _regularTypeface;
        DrawCells(drawingContext, text, typeface, isBold ? _boldGlyphs : _regularGlyphs, brush, x, y, cellAdvance, pixelsPerDip);

        if (style.Attributes.HasFlag(TerminalAttributes.Underline))
        {
            var underlineY = y + _cellHeight - 2;
            drawingContext.DrawLine(Pen(foreground), new Point(x, underlineY), new Point(x + width, underlineY));
        }

        if (style.Attributes.HasFlag(TerminalAttributes.StrikeThrough))
        {
            var strikeY = y + (_cellHeight / 2);
            drawingContext.DrawLine(Pen(foreground), new Point(x, strikeY), new Point(x + width, strikeY));
        }
    }

    /// <summary>
    /// Draws one styled run with every grapheme pinned to its own cell.
    /// </summary>
    /// <remarks>
    /// A shaped text run advances the pen by the font's own advance width, which is almost never
    /// the rounded cell width the grid uses. That difference accumulates across a run, so the drawn
    /// text drifts away from the grid and the caret ends up several columns past the last glyph.
    /// Explicit per-cell advances remove the drift; a glyph the font cannot supply falls back to a
    /// laid-out text object drawn at the same cell origin.
    /// </remarks>
    private void DrawCells(
        DrawingContext drawingContext,
        string text,
        Typeface typeface,
        GlyphTypeface? glyphTypeface,
        Brush brush,
        double x,
        double y,
        double cellAdvance,
        double pixelsPerDip)
    {
        var baselineY = y + _baseline;
        var penX = x;
        var batchOrigin = x;
        _runIndices.Clear();
        _runAdvances.Clear();

        var clusters = StringInfo.GetTextElementEnumerator(text);
        while (clusters.MoveNext())
        {
            var cluster = (string)clusters.Current;
            if (glyphTypeface is not null && TryMapCluster(glyphTypeface, cluster, cellAdvance))
            {
                if (_runIndices.Count == 0)
                {
                    batchOrigin = penX;
                }

                _runIndices.AddRange(_clusterIndices);
                _runAdvances.AddRange(_clusterAdvances);
            }
            else
            {
                FlushGlyphs(drawingContext, glyphTypeface, brush, batchOrigin, baselineY, pixelsPerDip);
                drawingContext.DrawText(
                    new FormattedText(
                        cluster,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        TerminalFontSize,
                        brush,
                        pixelsPerDip),
                    new Point(penX, y));
            }

            penX += cellAdvance;
        }

        FlushGlyphs(drawingContext, glyphTypeface, brush, batchOrigin, baselineY, pixelsPerDip);
    }

    private void FlushGlyphs(
        DrawingContext drawingContext,
        GlyphTypeface? glyphTypeface,
        Brush brush,
        double originX,
        double baselineY,
        double pixelsPerDip)
    {
        if (glyphTypeface is null || _runIndices.Count == 0)
        {
            return;
        }

        var glyphRun = new GlyphRun(
            glyphTypeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: TerminalFontSize,
            pixelsPerDip: (float)pixelsPerDip,
            glyphIndices: [.. _runIndices],
            baselineOrigin: new Point(originX, baselineY),
            advanceWidths: [.. _runAdvances],
            glyphOffsets: null,
            characters: null,
            deviceFontName: null,
            clusterMap: null,
            caretStops: null,
            language: null);
        drawingContext.DrawGlyphRun(brush, glyphRun);
        _runIndices.Clear();
        _runAdvances.Clear();
    }

    /// <summary>Maps one grapheme cluster to glyphs; the base glyph carries the whole cell advance.</summary>
    private bool TryMapCluster(GlyphTypeface glyphTypeface, string cluster, double cellAdvance)
    {
        _clusterIndices.Clear();
        _clusterAdvances.Clear();
        var map = glyphTypeface.CharacterToGlyphMap;
        for (var i = 0; i < cluster.Length; i++)
        {
            int codePoint = cluster[i];
            if (char.IsHighSurrogate(cluster[i]) && i + 1 < cluster.Length && char.IsLowSurrogate(cluster[i + 1]))
            {
                codePoint = char.ConvertToUtf32(cluster[i], cluster[i + 1]);
                i++;
            }

            if (!map.TryGetValue(codePoint, out var glyphIndex) || glyphIndex == 0)
            {
                return false;
            }

            _clusterIndices.Add(glyphIndex);
            _clusterAdvances.Add(_clusterIndices.Count == 1 ? cellAdvance : 0);
        }

        return _clusterIndices.Count > 0;
    }

    /// <summary>
    /// Whether a cell falls inside the selection. Selection is stored in absolute line indices, so
    /// it stays over the same text while new output scrolls the screen; the end column is exclusive.
    /// </summary>
    private bool IsSelected(long line, int column)
    {
        if (!_hasSelection)
        {
            return false;
        }

        var (startLine, startColumn, endLine, endColumn) = NormalizedSelection();
        if (line < startLine || line > endLine)
        {
            return false;
        }

        var from = line == startLine ? startColumn : 0;
        var to = line == endLine ? endColumn : int.MaxValue;
        return column >= from && column < to;
    }

    private (long StartLine, int StartColumn, long EndLine, int EndColumn) NormalizedSelection() =>
        _focusLine < _anchorLine || (_focusLine == _anchorLine && _focusColumn < _anchorColumn)
            ? (_focusLine, _focusColumn, _anchorLine, _anchorColumn)
            : (_anchorLine, _anchorColumn, _focusLine, _focusColumn);

    /// <summary>Maps a point to a grid position. The column rounds so a drag can end past a glyph.</summary>
    private (long Line, int Column) PositionAt(Point point, TerminalSnapshot snapshot)
    {
        var row = (int)Math.Floor((point.Y - VerticalPadding) / _cellHeight);
        var column = (int)Math.Round((point.X - HorizontalPadding) / _cellWidth);
        return (
            snapshot.FirstVisibleLine + Math.Clamp(row, 0, snapshot.Rows - 1),
            Math.Clamp(column, 0, snapshot.Columns));
    }

    /// <summary>
    /// Forwards one mouse event to the hosted program when it has enabled mouse tracking. Holding
    /// Shift overrides tracking so the terminal's own text selection stays reachable, which is the
    /// same escape hatch every other terminal offers.
    /// </summary>
    private bool ReportMouse(MouseEventArgs e, TerminalMouseButton button, bool pressed, bool motion)
    {
        if (Session is not { } session
            || session.Emulator.MouseTracking == TerminalMouseTracking.None
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return false;
        }

        var snapshot = session.Emulator.CreateSnapshot(_scrollOffset);
        var point = e.GetPosition(this);
        var column = (int)Math.Floor((point.X - HorizontalPadding) / _cellWidth);
        var row = (int)Math.Floor((point.Y - VerticalPadding) / _cellHeight);
        var sequence = TerminalMouseReport.Encode(
            button,
            pressed,
            motion,
            Math.Clamp(column, 0, snapshot.Columns - 1),
            Math.Clamp(row, 0, snapshot.Rows - 1),
            shift: false,
            alt: Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            control: Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            session.Emulator.MouseSgrEncoding);
        if (sequence is null)
        {
            return false;
        }

        ClearSelection();
        _ = SendAsync(session, sequence);
        return true;
    }

    /// <summary>Reports pointer motion, throttled to one report per cell the pointer enters.</summary>
    private bool ReportMouseMotion(MouseEventArgs e)
    {
        if (Session is not { } session)
        {
            return false;
        }

        var tracking = session.Emulator.MouseTracking;
        var button = e.LeftButton == MouseButtonState.Pressed
            ? TerminalMouseButton.Left
            : e.MiddleButton == MouseButtonState.Pressed
                ? TerminalMouseButton.Middle
                : e.RightButton == MouseButtonState.Pressed
                    ? TerminalMouseButton.Right
                    : TerminalMouseButton.None;

        var wanted = tracking switch
        {
            TerminalMouseTracking.AnyEvent => true,
            TerminalMouseTracking.ButtonEvent => button != TerminalMouseButton.None,
            _ => false,
        };

        if (!wanted)
        {
            return false;
        }

        var point = e.GetPosition(this);
        var cell = (
            Column: (int)Math.Floor((point.X - HorizontalPadding) / _cellWidth),
            Row: (int)Math.Floor((point.Y - VerticalPadding) / _cellHeight));
        if (cell == _lastReportedCell)
        {
            return tracking != TerminalMouseTracking.None && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        }

        _lastReportedCell = cell;
        return ReportMouse(e, button, pressed: true, motion: true);
    }

    private void ClearSelection()
    {
        if (!_hasSelection)
        {
            return;
        }

        _hasSelection = false;
        InvalidateVisual();
    }

    private bool CopySelection()
    {
        if (!_hasSelection || Session is not { } session)
        {
            return false;
        }

        var (startLine, startColumn, endLine, endColumn) = NormalizedSelection();
        var lines = session.Emulator.GetLines(startLine, startColumn, endLine, endColumn);
        if (lines.Count == 0)
        {
            return false;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            return true;
        }
        catch (ExternalException)
        {
            // Another process may temporarily hold the clipboard.
            return false;
        }
    }

    private void UpdateScrollMetrics(TerminalSnapshot snapshot)
    {
        SetCurrentValue(ScrollMaximumProperty, (double)snapshot.ScrollbackCount);
        SetCurrentValue(ViewportLinesProperty, (double)snapshot.Rows);
        SetCurrentValue(ScrollValueProperty, (double)(snapshot.ScrollbackCount - _scrollOffset));
    }

    /// <summary>
    /// Applies a deliberate scrollbar interaction. The scrollbar observes <see cref="ScrollValue"/>
    /// one-way; keeping its value out of the render-time feedback path prevents WPF coercion against
    /// a newly realized scrollbar maximum from bouncing the terminal viewport between top and bottom.
    /// </summary>
    internal void ScrollToValue(double value)
    {
        var maximum = ScrollMaximum;
        var offset = (int)Math.Clamp(maximum - value, 0, maximum);
        if (offset == _scrollOffset)
        {
            return;
        }

        _scrollOffset = offset;
        InvalidateVisual();
    }

    private Brush Brush(Color color)
    {
        if (_brushes.TryGetValue(color, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _brushes[color] = brush;
        return brush;
    }

    private Pen Pen(Color color)
    {
        if (_pens.TryGetValue(color, out var cached))
        {
            return cached;
        }

        var pen = new Pen(Brush(color), 1);
        pen.Freeze();
        _pens[color] = pen;
        return pen;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Alt combinations never reach here: Windows routes them as system keys and
        // OnPreviewKeyDown already emits their Escape-prefixed form. Prefixing again here would
        // corrupt AltGr, which reports as Control+Alt while producing an ordinary character.
        Send(e.Text);
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var modifiers = Keyboard.Modifiers;

        // Alt makes WPF report Key.System and put the real key in SystemKey. Without resolving that,
        // every Alt shortcut a hosted tool defines (Alt+M, Alt+B, Alt+Enter, …) is silently dropped.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            // Alt+F4 closes the window and Alt+Space opens the system menu. Both belong to Windows,
            // matching what other terminals leave alone.
            if (key is Key.F4 or Key.Space)
            {
                return;
            }

            // Swallow the bare Alt press so WPF does not enter menu mode over the terminal.
            if (key is Key.LeftAlt or Key.RightAlt)
            {
                e.Handled = true;
                return;
            }
        }

        // Ctrl+C copies only when there is a selection. With nothing selected it must fall through
        // as the interrupt byte, which is the only way to stop a running program.
        if (key == Key.C
            && (modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
                || (modifiers == ModifierKeys.Control && _hasSelection)))
        {
            if (CopySelection())
            {
                ClearSelection();
                e.Handled = true;
                return;
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                return;
            }
        }

        if ((modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.V) ||
            (modifiers == ModifierKeys.Control && key == Key.V) ||
            (modifiers == ModifierKeys.Shift && key == Key.Insert))
        {
            PasteClipboard();
            e.Handled = true;
            return;
        }

        var sequence = MapKey(key, modifiers, Session?.Emulator.ApplicationCursorKeys == true)
            ?? AltCharacterSequence(key, modifiers);
        if (sequence is null)
        {
            return;
        }

        Send(sequence);
        e.Handled = true;
    }

    /// <summary>
    /// The Escape-prefixed sequence for Alt plus a printable key, which is how terminals encode a
    /// Meta shortcut. Windows never raises text input for Alt combinations, so the character is read
    /// from the current keyboard layout instead.
    /// </summary>
    private static string? AltCharacterSequence(Key key, ModifierKeys modifiers)
    {
        if (!modifiers.HasFlag(ModifierKeys.Alt) || modifiers.HasFlag(ModifierKeys.Control))
        {
            return null;
        }

        if (KeyboardCharacters.ForVirtualKey(KeyInterop.VirtualKeyFromKey(key)) is not { } character)
        {
            return null;
        }

        if (char.IsLetter(character))
        {
            character = modifiers.HasFlag(ModifierKeys.Shift)
                ? char.ToUpperInvariant(character)
                : char.ToLowerInvariant(character);
        }

        return Escape + character;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _ = Focus();
        e.Handled = true;
        if (Session is not { } session)
        {
            return;
        }

        if (ReportMouse(e, TerminalMouseButton.Left, pressed: true, motion: false))
        {
            _ = CaptureMouse();
            return;
        }

        var (line, column) = PositionAt(e.GetPosition(this), session.Emulator.CreateSnapshot(_scrollOffset));
        _anchorLine = _focusLine = line;
        _anchorColumn = _focusColumn = column;
        _hasSelection = false;
        _isSelecting = true;
        _ = CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _ = Focus();
        if (ReportMouse(e, TerminalMouseButton.Right, pressed: true, motion: false))
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (ReportMouse(e, TerminalMouseButton.Right, pressed: false, motion: false))
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ReportMouseMotion(e))
        {
            return;
        }

        if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed || Session is not { } session)
        {
            return;
        }

        var (line, column) = PositionAt(e.GetPosition(this), session.Emulator.CreateSnapshot(_scrollOffset));
        if (line == _focusLine && column == _focusColumn)
        {
            return;
        }

        _focusLine = line;
        _focusColumn = column;
        _hasSelection = _focusLine != _anchorLine || _focusColumn != _anchorColumn;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (ReportMouse(e, TerminalMouseButton.Left, pressed: false, motion: false))
        {
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isSelecting = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // A program that asked for mouse tracking does its own scrolling, so the wheel goes to it.
        // Full-screen tools have no terminal scrollback to move anyway.
        if (ReportMouse(e, e.Delta > 0 ? TerminalMouseButton.WheelUp : TerminalMouseButton.WheelDown, pressed: true, motion: false))
        {
            e.Handled = true;
            return;
        }

        if (Session is not { } session || session.Emulator.IsAlternateScreen)
        {
            return;
        }

        var amount = Math.Max(1, session.Emulator.Rows / 3);
        _scrollOffset = Math.Clamp(
            _scrollOffset + (e.Delta > 0 ? amount : -amount),
            0,
            session.Emulator.CreateSnapshot().ScrollbackCount);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TerminalControlAutomationPeer(this);

    private static void OnSessionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)sender;
        if (e.OldValue is TerminalTabViewModel oldSession)
        {
            oldSession.Emulator.ScreenChanged -= control.OnScreenChanged;
        }

        control._scrollOffset = 0;
        control._lastScrollbackCount = 0;
        control._hasSelection = false;
        control._isSelecting = false;
        if (e.NewValue is TerminalTabViewModel newSession)
        {
            newSession.Emulator.ScreenChanged += control.OnScreenChanged;
            control._wasAlternateScreen = newSession.Emulator.IsAlternateScreen;
            AutomationProperties.SetName(control, $"Terminal {newSession.Title}");
            AutomationProperties.SetHelpText(
                control,
                "Interactive terminal. Drag to select, Ctrl+C copies a selection and otherwise interrupts, "
                + "Ctrl+V pastes, and the mouse wheel reviews scrollback.");
            control.ResizeTerminal();
        }

        control.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TerminalPalette.Changed -= OnPaletteChanged;
        TerminalPalette.Changed += OnPaletteChanged;
        MeasureCell();
        ResizeTerminal();
        _ = Focus();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        MeasureCell();
        ResizeTerminal();
    }

    private void OnScreenChanged(object? sender, EventArgs e)
    {
        if (Session is not { } session)
        {
            return;
        }

        // Switching buffers renumbers what is on screen, so a selection made in the other buffer no
        // longer refers to anything the user can see.
        if (session.Emulator.IsAlternateScreen != _wasAlternateScreen)
        {
            _wasAlternateScreen = session.Emulator.IsAlternateScreen;
            _hasSelection = false;
        }

        var scrollback = session.Emulator.CreateSnapshot().ScrollbackCount;
        if (_scrollOffset > 0 && scrollback > _lastScrollbackCount)
        {
            _scrollOffset += scrollback - _lastScrollbackCount;
        }

        _lastScrollbackCount = scrollback;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, scrollback);
        InvalidateVisual();
    }

    private void MeasureCell()
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var sample = new FormattedText(
            "M",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            _regularTypeface,
            TerminalFontSize,
            Brushes.White,
            pixelsPerDip);
        // The grid uses a whole-pixel cell so backgrounds and the caret land on device pixels.
        // Rounding to nearest keeps the cell close to the font's own advance width instead of
        // stretching every column, and DrawCells pins each glyph to the cell regardless.
        _cellWidth = Math.Max(1, Math.Round(sample.WidthIncludingTrailingWhitespace, MidpointRounding.AwayFromZero));
        _cellHeight = Math.Ceiling(sample.Height * 1.12);
        _baseline = sample.Baseline;
    }

    private void ResizeTerminal()
    {
        if (Session is not { } session || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var columns = Math.Clamp((int)((ActualWidth - (HorizontalPadding * 2)) / _cellWidth), 2, short.MaxValue);
        var rows = Math.Clamp((int)((ActualHeight - (VerticalPadding * 2)) / _cellHeight), 2, short.MaxValue);
        if (columns == session.Emulator.Columns && rows == session.Emulator.Rows)
        {
            return;
        }

        session.Emulator.Resize(columns, rows);
        try
        {
            session.Resize(new TerminalSize((short)columns, (short)rows));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Resize can race a normal root-shell exit and automatic tab removal.
        }
    }

    private void PasteClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText();
            if (Session?.Emulator.BracketedPaste == true)
            {
                text = "\u001b[200~" + text + "\u001b[201~";
            }

            Send(text);
        }
        catch (ExternalException)
        {
            // Another process may temporarily hold the clipboard. Leave the session untouched.
        }
    }

    private void Send(string text)
    {
        if (_scrollOffset != 0 || _hasSelection)
        {
            // Typing returns the viewport to the live screen and drops the selection, even when the
            // shell echoes nothing back.
            _scrollOffset = 0;
            _hasSelection = false;
            InvalidateVisual();
        }

        if (Session is not { } session)
        {
            return;
        }

        _ = SendAsync(session, text);
    }

    private static async Task SendAsync(TerminalTabViewModel session, string text)
    {
        try
        {
            await session.WriteAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Input can race a normal shell exit; the tab owner will remove the session.
        }
    }

    private static string? MapKey(Key key, ModifierKeys modifiers, bool applicationCursorKeys)
    {
        // AltGr reports as Control+Alt on many layouts and must reach the shell as the character it
        // produces (@, \, {, …), not as a control code, so only plain Control maps to Ctrl+key.
        if (modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt))
        {
            if (key is >= Key.A and <= Key.Z)
            {
                return ((char)(key - Key.A + 1)).ToString(CultureInfo.InvariantCulture);
            }

            if (key == Key.Space)
            {
                return "\0";
            }
        }

        var modifierCode = ModifierCode(modifiers);
        string Cursor(string final) => modifierCode == 1
            ? $"\u001b{(applicationCursorKeys ? "O" : "[")}{final}"
            : $"\u001b[1;{modifierCode}{final}";

        // Alt on a key with no modifier-encoded form is sent as Escape then the ordinary byte,
        // which is how terminals have always encoded Meta. Cursor and function keys carry Alt
        // in their own modifier parameter instead, so they must not be prefixed as well.
        var meta = modifiers.HasFlag(ModifierKeys.Alt) ? Escape : string.Empty;

        return key switch
        {
            Key.Enter => meta + "\r",
            Key.Back => meta + "\u007f",
            Key.Tab when modifiers.HasFlag(ModifierKeys.Shift) => "\u001b[Z",
            Key.Tab => meta + "\t",
            Key.Escape => meta + "\u001b",
            Key.Up => Cursor("A"),
            Key.Down => Cursor("B"),
            Key.Right => Cursor("C"),
            Key.Left => Cursor("D"),
            Key.Home => Cursor("H"),
            Key.End => Cursor("F"),
            Key.Insert => "\u001b[2~",
            Key.Delete => "\u001b[3~",
            Key.Prior => "\u001b[5~",
            Key.Next => "\u001b[6~",
            Key.F1 => "\u001bOP",
            Key.F2 => "\u001bOQ",
            Key.F3 => "\u001bOR",
            Key.F4 => "\u001bOS",
            Key.F5 => "\u001b[15~",
            Key.F6 => "\u001b[17~",
            Key.F7 => "\u001b[18~",
            Key.F8 => "\u001b[19~",
            Key.F9 => "\u001b[20~",
            Key.F10 => "\u001b[21~",
            Key.F11 => "\u001b[23~",
            Key.F12 => "\u001b[24~",
            _ => null,
        };
    }

    private static int ModifierCode(ModifierKeys modifiers)
    {
        var code = 1;
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            code += 1;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            code += 2;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            code += 4;
        }

        return code;
    }

    private static Color ResolveColor(TerminalColor color, bool isForeground)
    {
        return color.Kind switch
        {
            TerminalColorKind.Rgb => Color.FromRgb(color.First, color.Second, color.Third),
            TerminalColorKind.Indexed => IndexedColor(color.First),
            _ when isForeground => TerminalPalette.Foreground,
            _ => Colors.Transparent,
        };
    }

    private static Color IndexedColor(byte index)
    {
        var ansi = TerminalPalette.Ansi;
        if (index < ansi.Length)
        {
            return ansi[index];
        }

        if (index >= 232)
        {
            var level = (byte)(8 + ((index - 232) * 10));
            return Color.FromRgb(level, level, level);
        }

        var cube = index - 16;
        static byte Component(int value) => value == 0 ? (byte)0 : (byte)(55 + (value * 40));
        return Color.FromRgb(
            Component(cube / 36),
            Component((cube / 6) % 6),
            Component(cube % 6));
    }

    private sealed class TerminalControlAutomationPeer : FrameworkElementAutomationPeer
    {
        public TerminalControlAutomationPeer(TerminalControl owner)
            : base(owner)
        {
        }

        protected override string GetClassNameCore() => nameof(TerminalControl);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    }
}
