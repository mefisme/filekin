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
        int scrollbackCount)
    {
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

    public TerminalCell this[int row, int column] => Cells[(row * Columns) + column];
}

