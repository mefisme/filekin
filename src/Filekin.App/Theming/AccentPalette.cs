using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Filekin.Infrastructure.Windows.Settings;

namespace Filekin.App.Theming;

/// <summary>
/// One selectable accent. Each carries a dark and a light pair because an accent that reads well on
/// <c>#12161B</c> is washed out on <c>#F1F3F1</c>; the two are the same hue tuned per ground, the
/// way the shipped blue always was (DECISIONS.md, 2026-08-25 — "Visual Identity").
/// </summary>
/// <param name="Value">The durable value written to <c>settings.json</c>.</param>
/// <param name="Title">The name shown in Settings.</param>
internal sealed record AccentColors(
    string Value,
    string Title,
    Color Dark,
    Color DarkInk,
    Color Light,
    Color LightInk)
{
    /// <summary>The accent for <paramref name="theme"/>: the spark colour itself.</summary>
    public Color For(string theme) => theme == ThemePreference.Light ? Light : Dark;

    /// <summary>The quieter ink for <paramref name="theme"/>: headings, carets, directory names.</summary>
    public Color InkFor(string theme) => theme == ThemePreference.Light ? LightInk : DarkInk;
}

/// <summary>
/// The accent colours Filekin offers. Blue is the shipped default; orange is the hue the original
/// Filekin colour study used. The rest fill out the wheel without colliding with the semantic status
/// colours, which are a separate set an accent never replaces — nothing here is red (Bad), amber
/// (Warn), or the specific green used for Good.
/// </summary>
internal static class AccentPalette
{
    public const string Default = "blue";

    public static IReadOnlyList<AccentColors> All { get; } =
    [
        new("blue", "Blue", Rgb(0x4F9CE8), Rgb(0x7DBAF2), Rgb(0x1F6FB8), Rgb(0x17578F)),
        new("teal", "Teal", Rgb(0x3FB8AD), Rgb(0x72D2C9), Rgb(0x0F7D74), Rgb(0x0B615A)),
        new("green", "Green", Rgb(0x86C96B), Rgb(0xA8DC92), Rgb(0x428C2C), Rgb(0x336E22)),
        new("orange", "Orange", Rgb(0xE8863B), Rgb(0xF2A664), Rgb(0xC05F16), Rgb(0x9A4A0E)),
        new("pink", "Pink", Rgb(0xE87AA8), Rgb(0xF2A3C4), Rgb(0xB83A6E), Rgb(0x932D58)),
        new("purple", "Purple", Rgb(0xA98BE0), Rgb(0xC2ABEB), Rgb(0x6E4E96), Rgb(0x573C78)),
    ];

    /// <summary>The named accent, or blue when the value is unknown.</summary>
    public static AccentColors Get(string value) =>
        All.FirstOrDefault(accent => accent.Value == value) ?? All[0];

    /// <summary>Whether <paramref name="value"/> names an accent Filekin ships.</summary>
    public static bool IsKnown(string value) =>
        All.Any(accent => accent.Value == value);

    private static Color Rgb(int packed) => Color.FromRgb(
        (byte)((packed >> 16) & 0xFF),
        (byte)((packed >> 8) & 0xFF),
        (byte)(packed & 0xFF));

    /// <summary>
    /// Mixes <paramref name="over"/> into <paramref name="ground"/> opaquely. Used where a colour
    /// must be solid rather than composited by the renderer — the terminal draws raw cells and has
    /// no layer beneath a selection block to blend with.
    /// </summary>
    public static Color Blend(Color ground, Color over, double amount)
    {
        var mix = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)Math.Round(ground.R + ((over.R - ground.R) * mix)),
            (byte)Math.Round(ground.G + ((over.G - ground.G) * mix)),
            (byte)Math.Round(ground.B + ((over.B - ground.B) * mix)));
    }
}
