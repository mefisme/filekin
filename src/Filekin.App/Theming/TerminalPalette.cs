using System;
using System.Windows.Media;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.App.Theming;

/// <summary>
/// The colors a hosted terminal draws with. A terminal cannot take its colors from the WPF resource
/// dictionary — it renders raw cells, not styled controls — so the palette lives here and
/// <see cref="ThemeManager"/> points it at the dark or light set alongside the rest of the theme. A
/// dark terminal pane inside a light window would be a half-applied theme.
///
/// The sixteen ANSI entries are what programs explicitly ask for, so both sets keep the standard
/// meanings; the light set darkens them instead of remapping them, because the standard bright
/// colors are chosen for a dark ground and vanish on a light one.
/// </summary>
internal static class TerminalPalette
{
    private static readonly Color[] DarkAnsi =
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

    private static readonly Color[] LightAnsi =
    [
        Color.FromRgb(0x24, 0x2A, 0x2C), Color.FromRgb(0xC0, 0x43, 0x3F),
        Color.FromRgb(0x2E, 0x9B, 0x5B), Color.FromRgb(0x9A, 0x6B, 0x1E),
        Color.FromRgb(0x1F, 0x6F, 0xB8), Color.FromRgb(0x8A, 0x2A, 0x8F),
        Color.FromRgb(0x17, 0x70, 0x8A), Color.FromRgb(0x59, 0x63, 0x5F),
        Color.FromRgb(0x8A, 0x92, 0x8D), Color.FromRgb(0xA8, 0x33, 0x30),
        Color.FromRgb(0x24, 0x82, 0x4B), Color.FromRgb(0x7F, 0x57, 0x14),
        Color.FromRgb(0x17, 0x57, 0x8F), Color.FromRgb(0x71, 0x21, 0x76),
        Color.FromRgb(0x12, 0x5B, 0x70), Color.FromRgb(0x14, 0x18, 0x1A),
    ];

    /// <summary>Raised after the palette has been pointed at a different set.</summary>
    public static event EventHandler? Changed;

    /// <summary>The ground the terminal clears to — the window ground, so the pane is not a hole.</summary>
    public static Color Background { get; private set; } = Color.FromRgb(0x12, 0x16, 0x1B);

    /// <summary>Text with no explicit color, matching the app's body text.</summary>
    public static Color Foreground { get; private set; } = Color.FromRgb(0xD8, 0xDF, 0xE7);

    /// <summary>The caret, drawn in the accent ink.</summary>
    public static Color Cursor { get; private set; } = Color.FromRgb(0x7D, 0xBA, 0xF2);

    /// <summary>The block behind selected cells.</summary>
    public static Color Selection { get; private set; } = Color.FromRgb(0x2C, 0x4C, 0x70);

    /// <summary>The sixteen ANSI colors, indexed as the escape sequences number them.</summary>
    public static Color[] Ansi { get; private set; } = DarkAnsi;

    /// <summary>
    /// Points the palette at the set for <paramref name="theme"/> (a concrete dark or light), with
    /// the caret and selection taken from <paramref name="accent"/>. The sixteen ANSI entries are
    /// never accent-tinted: a program that asks for red means red.
    /// </summary>
    public static void Use(string theme, AccentColors accent)
    {
        ArgumentNullException.ThrowIfNull(accent);

        if (theme == ThemePreference.Light)
        {
            // MainBrush and TextBrush from Tokens.Light.xaml, so the pane is part of the window.
            Background = Color.FromRgb(0xF1, 0xF3, 0xF1);
            Foreground = Color.FromRgb(0x24, 0x2A, 0x2C);
            Ansi = LightAnsi;
        }
        else
        {
            Background = Color.FromRgb(0x12, 0x16, 0x1B);
            Foreground = Color.FromRgb(0xD8, 0xDF, 0xE7);
            Ansi = DarkAnsi;
        }

        Cursor = accent.InkFor(theme);

        // Opaque, not a translucent wash: a selection block is drawn straight over the cell, with no
        // layer beneath it for the renderer to composite against.
        Selection = AccentPalette.Blend(
            Background,
            accent.For(theme),
            theme == ThemePreference.Light ? 0.22 : 0.38);

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
