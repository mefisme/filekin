namespace Filekin.Core.Terminal.Emulation;

/// <summary>An immutable screen image for a renderer.</summary>
public sealed class TerminalSnapshot
{
    internal TerminalSnapshot(
        int columns,
        int rows,
        TerminalCell[] cells,
        int cursorColumn,
        int cursorRow,
        bool cursorVisible,
        int scrollbackCount,
        long firstVisibleLine)
    {
        FirstVisibleLine = firstVisibleLine;
        Columns = columns;
        Rows = rows;
        Cells = cells;
        CursorColumn = cursorColumn;
        CursorRow = cursorRow;
        CursorVisible = cursorVisible;
        ScrollbackCount = scrollbackCount;
    }

    public int Columns { get; }

    public int Rows { get; }

    public IReadOnlyList<TerminalCell> Cells { get; }

    public int CursorColumn { get; }

    public int CursorRow { get; }

    public bool CursorVisible { get; }

    public int ScrollbackCount { get; }

    /// <summary>
    /// Absolute line index of viewport row 0. Absolute indices stay stable as new output scrolls the
    /// screen, so a selection anchored to them survives both new output and scrollback movement.
    /// Pass them to <see cref="TerminalEmulator.GetLines"/>.
    /// </summary>
    public long FirstVisibleLine { get; }

    public TerminalCell this[int row, int column] => Cells[(row * Columns) + column];
}

