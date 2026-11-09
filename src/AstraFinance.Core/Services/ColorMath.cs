using System.Globalization;

namespace AstraFinance.Core.Services;

/// <summary>An sRGB color with 8-bit channels.</summary>
public readonly record struct RgbColor(byte A, byte R, byte G, byte B)
{
    public static RgbColor Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var s = hex.TrimStart('#');
        if (s.Length == 6)
        {
            s = "FF" + s;
        }

        if (s.Length != 8)
        {
            throw new FormatException($"Invalid color '{hex}'.");
        }

        var value = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new RgbColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");

    public RgbColor WithAlpha(byte alpha) => this with { A = alpha };
}

/// <summary>WCAG 2.x color helpers used to keep the theme palette accessible.</summary>
public static class ColorMath
{
    /// <summary>Relative luminance per WCAG 2.x.</summary>
    public static double RelativeLuminance(RgbColor c)
    {
        static double Channel(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>Contrast ratio between two opaque colors (1–21).</summary>
    public static double ContrastRatio(RgbColor a, RgbColor b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (light, dark) = la > lb ? (la, lb) : (lb, la);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Linear mix of two colors; <paramref name="amount"/> = 0 returns <paramref name="from"/>.</summary>
    public static RgbColor Mix(RgbColor from, RgbColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        static byte Lerp(byte x, byte y, double t) => (byte)Math.Round(x + ((y - x) * t));
        return new RgbColor(Lerp(from.A, to.A, amount), Lerp(from.R, to.R, amount), Lerp(from.G, to.G, amount), Lerp(from.B, to.B, amount));
    }

    /// <summary>Composites a (possibly translucent) foreground over an opaque background.</summary>
    public static RgbColor Composite(RgbColor foreground, RgbColor background)
    {
        var alpha = foreground.A / 255d;
        var opaque = foreground with { A = 255 };
        return Mix(background with { A = 255 }, opaque, alpha);
    }
}
