namespace Filekin.Core.Terminal;

/// <summary>
/// Terminal dimensions in character cells. Columns map to width and rows map to height.
/// </summary>
public readonly record struct TerminalSize
{
    public TerminalSize(short columns, short rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);

        Columns = columns;
        Rows = rows;
    }

    public short Columns { get; }

    public short Rows { get; }

    public static TerminalSize Default { get; } = new(80, 24);
}
