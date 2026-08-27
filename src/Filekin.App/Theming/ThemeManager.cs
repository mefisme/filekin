using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Filekin.Infrastructure.Windows.Settings;
using Filekin.Infrastructure.Windows.Theming;

namespace Filekin.App.Theming;

/// <summary>
/// Applies the user's appearance preferences: the theme, by swapping the whole palette dictionary in
/// the application's merged resources, and the accent, by overriding the four accent keys above it.
/// Every style references its colours with <c>DynamicResource</c>, so both re-resolve the running UI
/// without rebuilding a single control template.
///
/// A theme is a palette and nothing else — no metric, font, or layout differs between them.
/// </summary>
internal static class ThemeManager
{
    // The palette dictionary identifies itself with this key rather than being located by merge
    // order, so adding a future dictionary to App.xaml cannot silently swap the wrong one.
    private const string ThemeNameKey = "ThemeName";

    // Alpha levels that reproduce the shipped blue exactly: a dim wash behind a hovered or selected
    // row, and a slightly stronger hairline for a focused border.
    private const byte DarkDimAlpha = 0x26;
    private const byte DarkLineAlpha = 0x4D;
    private const byte LightDimAlpha = 0x1F;
    private const byte LightLineAlpha = 0x52;

    // Assembly-qualified pack URIs, not relative ones: a relative dictionary Source resolves against
    // the entry assembly, so it silently breaks wherever this assembly is not the process entry
    // point. The name is read from the assembly rather than written out, because it is "Filekin",
    // not the project name.
    private static readonly string ThisAssembly = typeof(ThemeManager).Assembly.GetName().Name!;

    private static readonly Uri DarkTokens = Tokens("Dark");
    private static readonly Uri LightTokens = Tokens("Light");

    /// <summary>The theme currently on screen: <c>dark</c> or <c>light</c>, never <c>system</c>.</summary>
    public static string ActiveTheme { get; private set; } = ThemePreference.Dark;

    /// <summary>The accent currently on screen.</summary>
    public static string ActiveAccent { get; private set; } = AccentPalette.Default;

    /// <summary>
    /// Applies <paramref name="themePreference"/> — one of <see cref="ThemePreference"/> — and
    /// <paramref name="accent"/>. <c>system</c> is resolved against the current Windows app mode each
    /// time this is called, which is what makes a live app-mode change re-apply correctly.
    /// </summary>
    public static void Apply(string themePreference, string accent)
    {
        ArgumentNullException.ThrowIfNull(themePreference);
        ArgumentNullException.ThrowIfNull(accent);

        var theme = Resolve(themePreference);
        var colors = AccentPalette.Get(accent);

        if (Application.Current is not { } application)
        {
            ActiveTheme = theme;
            ActiveAccent = colors.Value;
            TerminalPalette.Use(theme, colors);
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var index = IndexOfPalette(dictionaries);
        if (index < 0)
        {
            return;
        }

        // Re-applying what is already on screen would rebuild every brush and flicker the window for
        // no visible change; a Windows app-mode broadcast that did not actually flip the mode is the
        // common case.
        if (theme == ActiveTheme && colors.Value == ActiveAccent && dictionaries[index].Contains(ThemeNameKey))
        {
            return;
        }

        if (theme != ActiveTheme || !dictionaries[index].Contains(ThemeNameKey))
        {
            dictionaries[index] = new ResourceDictionary
            {
                Source = theme == ThemePreference.Light ? LightTokens : DarkTokens,
            };
        }

        ApplyAccent(application.Resources, theme, colors);

        ActiveTheme = theme;
        ActiveAccent = colors.Value;

        // A hosted terminal renders raw cells and never reads the resource dictionary, so it is
        // repainted explicitly rather than left as a dark pane inside a light window.
        TerminalPalette.Use(theme, colors);
    }

    /// <summary>Resolves a theme preference to the concrete theme it means right now.</summary>
    public static string Resolve(string preference) => preference switch
    {
        ThemePreference.Light => ThemePreference.Light,
        ThemePreference.System => WindowsAppTheme.PrefersLight() ? ThemePreference.Light : ThemePreference.Dark,
        _ => ThemePreference.Dark,
    };

    /// <summary>The accent brush pair for a swatch, resolved against the theme now on screen.</summary>
    public static Brush SwatchFor(AccentColors accent)
    {
        ArgumentNullException.ThrowIfNull(accent);
        var brush = new SolidColorBrush(accent.For(ActiveTheme));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Writes the accent keys directly into the application's own resources, above the palette
    /// dictionary. A top-level entry shadows the merged one, so a later theme swap keeps the accent
    /// and only the grounds change.
    /// </summary>
    private static void ApplyAccent(ResourceDictionary resources, string theme, AccentColors accent)
    {
        var isLight = theme == ThemePreference.Light;
        var spark = accent.For(theme);
        var ink = accent.InkFor(theme);

        resources["AccentBrush"] = Frozen(spark);
        resources["AccentInkBrush"] = Frozen(ink);
        resources["AccentDimBrush"] = Frozen(Color.FromArgb(
            isLight ? LightDimAlpha : DarkDimAlpha, spark.R, spark.G, spark.B));
        resources["AccentLineBrush"] = Frozen(Color.FromArgb(
            isLight ? LightLineAlpha : DarkLineAlpha, spark.R, spark.G, spark.B));

        // Directories are the listing's one accent splash, so they follow the accent rather than
        // staying blue while everything around them changes.
        resources["DirBrush"] = Frozen(isLight ? spark : ink);
    }

    private static Uri Tokens(string theme) => new(
        $"pack://application:,,,/{ThisAssembly};component/Themes/Tokens.{theme}.xaml",
        UriKind.Absolute);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static int IndexOfPalette(Collection<ResourceDictionary> dictionaries)
    {
        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (dictionaries[index].Contains(ThemeNameKey))
            {
                return index;
            }
        }

        return -1;
    }
}
