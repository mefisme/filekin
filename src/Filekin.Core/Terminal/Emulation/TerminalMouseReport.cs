using System.Globalization;

namespace Filekin.Core.Terminal.Emulation;

/// <summary>How much mouse activity the hosted program has asked to receive.</summary>
public enum TerminalMouseTracking
{
    /// <summary>Nothing is reported; the mouse belongs to the terminal surface.</summary>
    None,

    /// <summary>Button presses and releases only (DECSET 1000).</summary>
    Click,

    /// <summary>Presses, releases, and motion while a button is held (DECSET 1002).</summary>
    ButtonEvent,

    /// <summary>Presses, releases, and all motion (DECSET 1003).</summary>
    AnyEvent,
}

/// <summary>The mouse button a report describes. Values are the wire codes.</summary>
public enum TerminalMouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2,

    /// <summary>No button held, used for motion reports.</summary>
    None = 3,
    WheelUp = 64,
    WheelDown = 65,
}

/// <summary>
/// Encodes a mouse event the way a hosted terminal program expects to read it. Full-screen tools
/// such as Claude Code and vim scroll and click through these reports rather than through the
/// terminal's own scrollback, so a terminal that never sends them appears to have no mouse at all.
/// </summary>
public static class TerminalMouseReport
{
    private const int LegacyCoordinateLimit = 222;

    /// <summary>
    /// The escape sequence for one mouse event, or null when it cannot be represented.
    /// Coordinates are zero-based cells.
    /// </summary>
    /// <param name="sgrEncoding">
    /// Whether the program enabled SGR reporting (DECSET 1006). The legacy encoding cannot express a
    /// position past column or row 223, which is why modern programs ask for SGR.
    /// </param>
    public static string? Encode(
        TerminalMouseButton button,
        bool pressed,
        bool motion,
        int column,
        int row,
        bool shift,
        bool alt,
        bool control,
        bool sgrEncoding)
    {
        if (column < 0 || row < 0)
        {
            return null;
        }

        var code = (int)button;
        if (motion)
        {
            code += 32;
        }

        if (shift)
        {
            code += 4;
        }

        if (alt)
        {
            code += 8;
        }

        if (control)
        {
            code += 16;
        }

        if (sgrEncoding)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"\u001b[<{code};{column + 1};{row + 1}{(pressed ? 'M' : 'm')}");
        }

        if (column > LegacyCoordinateLimit || row > LegacyCoordinateLimit)
        {
            return null;
        }

        // The legacy encoding has no room for which button was released, so every release reports
        // button 3 while keeping the modifier and motion bits.
        var legacy = pressed ? code : (code & ~3) | 3;
        return string.Concat(
            "\u001b[M",
            ((char)(32 + legacy)).ToString(CultureInfo.InvariantCulture),
            ((char)(33 + column)).ToString(CultureInfo.InvariantCulture),
            ((char)(33 + row)).ToString(CultureInfo.InvariantCulture));
    }
}
