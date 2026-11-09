using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Services;

/// <summary>Concrete accent colors for one accent preset in one theme.</summary>
/// <param name="Accent">Fill color for primary buttons, selection indicators, switches.</param>
/// <param name="Hover">Fill color on hover.</param>
/// <param name="Pressed">Fill color when pressed.</param>
/// <param name="Subtle">Translucent tint for selected rows and badges.</param>
/// <param name="Text">Accent-colored text/icons on the page background (WCAG AA ≥ 4.5:1).</param>
/// <param name="Foreground">Text/icons placed on an <paramref name="Accent"/> fill (WCAG AA ≥ 4.5:1).</param>
public sealed record AccentSwatch(
    RgbColor Accent,
    RgbColor Hover,
    RgbColor Pressed,
    RgbColor Subtle,
    RgbColor Text,
    RgbColor Foreground);

/// <summary>
/// The curated accent presets. Coral is the brand default. All combinations are checked for
/// WCAG AA contrast in unit tests against the theme backgrounds below.
/// </summary>
public static class AccentPalette
{
    public static readonly RgbColor DarkBackground = RgbColor.Parse("#15171C");
    public static readonly RgbColor DarkSurface = RgbColor.Parse("#1B2130");
    public static readonly RgbColor LightBackground = RgbColor.Parse("#FFFFFF");
    public static readonly RgbColor LightSurface = RgbColor.Parse("#F6F7F9");

    private static readonly RgbColor DarkInk = RgbColor.Parse("#15171C");
    private static readonly RgbColor LightInk = RgbColor.Parse("#1F2329");
    private static readonly RgbColor White = RgbColor.Parse("#FFFFFF");
    private static readonly RgbColor Black = RgbColor.Parse("#000000");

    // (accent fill, accent text) per theme.
    private static readonly Dictionary<(AccentColor, ResolvedTheme), (string Fill, string Text)> Definitions = new()
    {
        [(AccentColor.Coral, ResolvedTheme.Dark)] = ("#F4806B", "#F79583"),
        [(AccentColor.Coral, ResolvedTheme.Light)] = ("#EF6C55", "#B9432E"),
        [(AccentColor.Amber, ResolvedTheme.Dark)] = ("#F2B04A", "#F5BE63"),
        [(AccentColor.Amber, ResolvedTheme.Light)] = ("#E9A23B", "#8F5A0A"),
        [(AccentColor.Teal, ResolvedTheme.Dark)] = ("#2EC4B0", "#4FD1BF"),
        [(AccentColor.Teal, ResolvedTheme.Light)] = ("#1FB3A0", "#0E7A6C"),
        [(AccentColor.Blue, ResolvedTheme.Dark)] = ("#6AA6F2", "#86B7F5"),
        [(AccentColor.Blue, ResolvedTheme.Light)] = ("#4C8DF0", "#1F5FC0"),
        [(AccentColor.Violet, ResolvedTheme.Dark)] = ("#A78BFA", "#B8A1FB"),
        [(AccentColor.Violet, ResolvedTheme.Light)] = ("#9A7BF5", "#6544D6"),
    };

    public static AccentSwatch Get(AccentColor accent, ResolvedTheme theme)
    {
        if (!Definitions.TryGetValue((accent, theme), out var def))
        {
            throw new ArgumentOutOfRangeException(nameof(accent), accent, "Unknown accent preset.");
        }

        var fill = RgbColor.Parse(def.Fill);
        var text = RgbColor.Parse(def.Text);
        return theme == ResolvedTheme.Dark
            ? new AccentSwatch(
                fill,
                ColorMath.Mix(fill, White, 0.12),
                ColorMath.Mix(fill, Black, 0.10),
                fill.WithAlpha(0x2E),
                text,
                DarkInk)
            : new AccentSwatch(
                fill,
                ColorMath.Mix(fill, Black, 0.08),
                ColorMath.Mix(fill, Black, 0.16),
                fill.WithAlpha(0x24),
                text,
                LightInk);
    }
}
