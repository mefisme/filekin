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
using Filekin.App.ViewModels;
using Filekin.Core.Terminal;
using Filekin.Core.Terminal.Emulation;

namespace Filekin.App.Controls;

/// <summary>A keyboard-input and cell-rendering WPF surface for one hosted terminal session.</summary>
public sealed class TerminalControl : FrameworkElement
{
    private const double TerminalFontSize = 14;
    private const double HorizontalPadding = 14;
    private const double VerticalPadding = 10;

    private static readonly Color[] AnsiColors =
    [
        Color.FromRgb(0x0C, 0x0C, 0x0C), Color.FromRgb(0xC5, 0x0F, 0x1F),
        Color.FromRgb(0x13, 0xA1, 0x0E), Color.FromRgb(0xC1, 0x9C, 0x00),
        Color.FromRgb(0x00, 0x37, 0xDA), Color.FromRgb(0x88, 0x17, 0x98),
        Color.FromRgb(0x3A, 0x96, 0xDD), Color.FromRgb(0xCC, 0xCC, 0xCC),
        Color.FromRgb(0x76, 0x76, 0x76), Color.FromRgb(0xE7, 0x48, 0x56),
        Color.FromRgb(0x16, 0xC6, 0x0C), Color.FromRgb(0xF9, 0xF1, 0xA5),
        Color.FromRgb(0x3B, 0x78, 0xFF), Color.FromRgb(0xB4, 0x00, 0x9E),
        Color.FromRgb(0x61, 0xD6, 0xD6), Color.FromRgb(0xF2, 0xF2, 0xF2),
    ];

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(TerminalTabViewModel),
        typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, OnSessionChanged));

    private readonly Typeface _regularTypeface = new("Cascadia Mono, Consolas");
    private readonly Typeface _boldTypeface = new(
        new FontFamily("Cascadia Mono, Consolas"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);
    private readonly Dictionary<Color, Brush> _brushes = [];
    private readonly Dictionary<Color, Pen> _pens = [];
    private double _cellWidth = 8.5;
    private double _cellHeight = 18;
    private int _scrollOffset;
    private int _lastScrollbackCount;

    public TerminalControl()
    {
        Focusable = true;
        Cursor = Cursors.IBeam;
        SnapsToDevicePixels = true;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public TerminalTabViewModel? Session
    {
        get => (TerminalTabViewModel?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brush(Color.FromRgb(0x12, 0x16, 0x1B)), null, new Rect(RenderSize));

        if (Session is not { } session)
        {
            return;
        }

        var snapshot = session.Emulator.CreateSnapshot(_scrollOffset);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var run = new StringBuilder();
        for (var row = 0; row < snapshot.Rows; row++)
        {
            var y = VerticalPadding + (row * _cellHeight);
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
                // a scrolling shell or a redrawing TUI.
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
                        if (next.IsContinuation || nextIsWide || !SameStyle(cell, next))
                        {
                            break;
                        }

                        run.Append(next.Text);
                        width++;
                    }
                }

                var x = HorizontalPadding + (column * _cellWidth);
                var spanWidth = (isWide ? 2 : width) * _cellWidth;
                DrawRun(drawingContext, run.ToString(), cell, x, y, spanWidth, pixelsPerDip);
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
            drawingContext.DrawRectangle(Brush(Color.FromRgb(0x7D, 0xBA, 0xF2)), null, cursorRect);
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
        double pixelsPerDip)
    {
        var foreground = ResolveColor(style.Foreground, isForeground: true);
        var background = ResolveColor(style.Background, isForeground: false);
        if (style.Attributes.HasFlag(TerminalAttributes.Inverse))
        {
            (foreground, background) = (background, foreground);
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
        var typeface = style.Attributes.HasFlag(TerminalAttributes.Bold) ? _boldTypeface : _regularTypeface;
        drawingContext.DrawText(
            new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                TerminalFontSize,
                brush,
                pixelsPerDip),
            new Point(x, y));

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

        var prefix = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? "\u001b" : string.Empty;
        Send(prefix + e.Text);
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var modifiers = Keyboard.Modifiers;

        if ((modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V) ||
            (modifiers == ModifierKeys.Shift && e.Key == Key.Insert))
        {
            PasteClipboard();
            e.Handled = true;
            return;
        }

        var sequence = MapKey(e.Key, modifiers, Session?.Emulator.ApplicationCursorKeys == true);
        if (sequence is null)
        {
            return;
        }

        Send(sequence);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _ = Focus();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
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
        if (e.NewValue is TerminalTabViewModel newSession)
        {
            newSession.Emulator.ScreenChanged += control.OnScreenChanged;
            AutomationProperties.SetName(control, $"Terminal {newSession.Title}");
            AutomationProperties.SetHelpText(
                control,
                "Interactive terminal. Ctrl+Shift+V pastes; mouse wheel reviews scrollback.");
            control.ResizeTerminal();
        }

        control.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        _cellWidth = Math.Ceiling(sample.WidthIncludingTrailingWhitespace);
        _cellHeight = Math.Ceiling(sample.Height * 1.12);
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
        if (_scrollOffset != 0)
        {
            // Typing returns the viewport to the live screen even when the shell echoes nothing.
            _scrollOffset = 0;
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

        return key switch
        {
            Key.Enter => "\r",
            Key.Back => "\u007f",
            Key.Tab when modifiers.HasFlag(ModifierKeys.Shift) => "\u001b[Z",
            Key.Tab => "\t",
            Key.Escape => "\u001b",
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
            _ when isForeground => Color.FromRgb(0xD8, 0xDF, 0xE7),
            _ => Colors.Transparent,
        };
    }

    private static Color IndexedColor(byte index)
    {
        if (index < AnsiColors.Length)
        {
            return AnsiColors[index];
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
