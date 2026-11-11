using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Services;

/// <summary>Concrete accent colors for one accent preset in one theme.</summary>
/// <param name="Accent">Fill color for primary buttons, selection indicators, switches.</param>
/// <param name="Hover">Fill color on hover.</param>
/// <param name="Pressed">Fill color when pressed.</param>
/// <param name="Subtle">Translucent tint for selected rows and badges.</param>
/// <param name="Text">Accent-colored text/icons on the page background (WCAG AA ≥ 4.5:1).</param>
/// <param name="Foreground">Text/icons placed on an <paramref name="Accent"/> fill or gradient (WCAG AA ≥ 4.5:1).</param>
/// <param name="GradientStart">Start (bottom-left) of the primary-action gradient; it ends at <paramref name="Accent"/>.</param>
public sealed record AccentSwatch(
    RgbColor Accent,
    RgbColor Hover,
    RgbColor Pressed,
    RgbColor Subtle,
    RgbColor Text,
    RgbColor Foreground,
    RgbColor GradientStart);

/// <summary>
/// The curated accent presets. Blue (Royal Blue, the Astra brand color) is the default. All
/// combinations are checked for WCAG AA contrast in unit tests against the theme backgrounds below.
/// </summary>
public static class AccentPalette
{
    /// <summary>Brand Deep Navy.</summary>
    public static readonly RgbColor BrandNavy = RgbColor.Parse("#172554");

    /// <summary>Brand Royal Blue.</summary>
    public static readonly RgbColor BrandRoyal = RgbColor.Parse("#2563EB");

    public static readonly RgbColor DarkBackground = RgbColor.Parse("#0B1229");
    public static readonly RgbColor DarkSurface = RgbColor.Parse("#111C3A");
    public static readonly RgbColor LightBackground = RgbColor.Parse("#FFFFFF");
    public static readonly RgbColor LightSurface = RgbColor.Parse("#F5F7FB");

    private static readonly RgbColor DarkInk = RgbColor.Parse("#0B1229");
    private static readonly RgbColor LightInk = RgbColor.Parse("#0F172A");
    private static readonly RgbColor White = RgbColor.Parse("#FFFFFF");
    private static readonly RgbColor Black = RgbColor.Parse("#000000");

    /// <summary>Brand gradient start on dark backgrounds (Deep Navy would disappear there).</summary>
    private static readonly RgbColor DarkBrandGradientStart = RgbColor.Parse("#1E40AF");

    // (accent fill, accent text) per theme.
    private static readonly Dictionary<(AccentColor, ResolvedTheme), (string Fill, string Text)> Definitions = new()
    {
        [(AccentColor.Coral, ResolvedTheme.Dark)] = ("#F4806B", "#F79583"),
        [(AccentColor.Coral, ResolvedTheme.Light)] = ("#EF6C55", "#B9432E"),
        [(AccentColor.Amber, ResolvedTheme.Dark)] = ("#F2B04A", "#F5BE63"),
        [(AccentColor.Amber, ResolvedTheme.Light)] = ("#E9A23B", "#8F5A0A"),
        [(AccentColor.Teal, ResolvedTheme.Dark)] = ("#2EC4B0", "#4FD1BF"),
        [(AccentColor.Teal, ResolvedTheme.Light)] = ("#1FB3A0", "#0E7A6C"),
        [(AccentColor.Blue, ResolvedTheme.Dark)] = ("#2563EB", "#60A5FA"),
        [(AccentColor.Blue, ResolvedTheme.Light)] = ("#2563EB", "#1D4ED8"),
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
        var isDark = theme == ResolvedTheme.Dark;

        // Text on the fill: white where readable, otherwise the theme's ink.
        var whiteOnFill = ColorMath.ContrastRatio(White, fill) >= 4.5;

        // Blue is the brand: its gradient runs Deep Navy → Royal Blue. Other presets get a gentle shade of
        // themselves, shaded away from the text color so the text stays readable across the whole gradient.
        var gradientStart = accent == AccentColor.Blue
            ? (isDark ? DarkBrandGradientStart : BrandNavy)
            : ColorMath.Mix(fill, whiteOnFill ? Black : White, 0.12);

        var foreground = whiteOnFill && ColorMath.ContrastRatio(White, gradientStart) >= 4.5
            ? White
            : isDark ? DarkInk : LightInk;

        return isDark
            ? new AccentSwatch(
                fill,
                ColorMath.Mix(fill, White, 0.12),
                ColorMath.Mix(fill, Black, 0.10),
                fill.WithAlpha(0x2E),
                text,
                foreground,
                gradientStart)
            : new AccentSwatch(
                fill,
                ColorMath.Mix(fill, Black, 0.08),
                ColorMath.Mix(fill, Black, 0.16),
                fill.WithAlpha(0x24),
                text,
                foreground,
                gradientStart);
    }
}
