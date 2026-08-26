using System.Diagnostics;

namespace Filekin.Core.Terminal.Emulation;

public enum TerminalColorKind
{
    Default,
    Indexed,
    Rgb,
}

/// <summary>A terminal color expressed as the default, an ANSI palette index, or RGB.</summary>
public readonly record struct TerminalColor
{
    private TerminalColor(TerminalColorKind kind, byte first, byte second, byte third)
    {
        Kind = kind;
        First = first;
        Second = second;
        Third = third;
    }

    public TerminalColorKind Kind { get; }

    public byte First { get; }

    public byte Second { get; }

    public byte Third { get; }

    public static TerminalColor Default { get; } = new(TerminalColorKind.Default, 0, 0, 0);

    public static TerminalColor Indexed(byte index) => new(TerminalColorKind.Indexed, index, 0, 0);

    public static TerminalColor Rgb(byte red, byte green, byte blue) =>
        new(TerminalColorKind.Rgb, red, green, blue);
}

[Flags]
public enum TerminalAttributes
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Hidden = 32,
    StrikeThrough = 64,
}

/// <summary>One rendered terminal cell. A wide glyph owns two cells; the second is a continuation.</summary>
[DebuggerDisplay("{Text,nq}")]
public readonly record struct TerminalCell(
    string Text,
    TerminalColor Foreground,
    TerminalColor Background,
    TerminalAttributes Attributes,
    bool IsContinuation)
{
    public static TerminalCell Empty { get; } = new(
        " ",
        TerminalColor.Default,
        TerminalColor.Default,
        TerminalAttributes.None,
        IsContinuation: false);
}

