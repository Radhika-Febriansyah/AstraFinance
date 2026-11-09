using System.Globalization;
using System.Text;
using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Money;

/// <summary>Regional formatting preferences used for money and dates.</summary>
public sealed record RegionalFormat(CurrencyInfo Currency, NumberFormatStyle NumberStyle, DateFormatStyle DateStyle)
{
    public static RegionalFormat Default { get; } =
        new(CurrencyInfo.Idr, NumberFormatStyle.DotGroupCommaDecimal, DateFormatStyle.DayMonthYear);

    public char GroupSeparator => NumberStyle switch
    {
        NumberFormatStyle.CommaGroupDotDecimal => ',',
        NumberFormatStyle.SpaceGroupCommaDecimal => ' ',
        _ => '.',
    };

    public char DecimalSeparator => NumberStyle == NumberFormatStyle.CommaGroupDotDecimal ? '.' : ',';

    public string ShortDatePattern => DateStyle switch
    {
        DateFormatStyle.MonthDayYear => "MM/dd/yyyy",
        DateFormatStyle.YearMonthDay => "yyyy-MM-dd",
        _ => "dd/MM/yyyy",
    };

    public string FormatDate(DateOnly date) => date.ToString(ShortDatePattern, CultureInfo.InvariantCulture);
}

/// <summary>Formats integer minor-unit amounts exactly (no floating point involved).</summary>
public static class MoneyFormatter
{
    /// <summary>
    /// Machine-readable amount for exports: no symbol, no grouping, '.' as decimal separator
    /// (e.g. 1500000, -12.34). Exact; no floating point.
    /// </summary>
    public static string FormatPlain(long minorUnits, CurrencyInfo currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var negative = minorUnits < 0;
        var magnitude = negative ? (ulong)(-(minorUnits + 1)) + 1 : (ulong)minorUnits;
        var scale = (ulong)currency.Scale;
        var text = (magnitude / scale).ToString(CultureInfo.InvariantCulture);
        if (currency.MinorDigits > 0)
        {
            text += "." + (magnitude % scale).ToString(CultureInfo.InvariantCulture).PadLeft(currency.MinorDigits, '0');
        }

        return negative ? "-" + text : text;
    }

    /// <summary>
    /// Short label for chart axes, e.g. "Rp8jt", "Rp1,5jt", "$2.3M". <paramref name="suffixes"/> are the
    /// localized thousand/million/billion/trillion suffixes. Display only — never used for stored values.
    /// </summary>
    public static string FormatCompact(long minorUnits, RegionalFormat format, IReadOnlyList<string> suffixes)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(suffixes);
        if (suffixes.Count != 4)
        {
            throw new ArgumentException("Expected four suffixes (thousand, million, billion, trillion).", nameof(suffixes));
        }

        var negative = minorUnits < 0;
        var major = Math.Abs((decimal)minorUnits) / format.Currency.Scale;
        string number;
        var tier = major >= 1_000_000_000_000m ? 3 : major >= 1_000_000_000m ? 2 : major >= 1_000_000m ? 1 : major >= 1_000m ? 0 : -1;
        if (tier < 0)
        {
            number = Math.Round(major, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }
        else
        {
            var scaled = major / (decimal)Math.Pow(1000, tier + 1);
            number = Math.Round(scaled, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture)
                .Replace('.', format.DecimalSeparator) + suffixes[tier];
        }

        var symbol = format.Currency.SymbolBeforeAmount ? format.Currency.Symbol : string.Empty;
        var after = format.Currency.SymbolBeforeAmount ? string.Empty : " " + format.Currency.Symbol;
        return (negative ? "-" : string.Empty) + symbol + number + after;
    }

    /// <summary>A representative amount for previews: 1,234,567 (plus .89 for currencies with minor units).</summary>
    public static long SampleAmount(CurrencyInfo currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return (1_234_567 * currency.Scale) + (currency.MinorDigits > 0 ? 89 % currency.Scale : 0);
    }

    /// <summary>Formats e.g. 1234567 IDR → "Rp1.234.567", -1050 USD → "-$10.50".</summary>
    public static string Format(long minorUnits, RegionalFormat format, bool includeSymbol = true)
    {
        ArgumentNullException.ThrowIfNull(format);
        var currency = format.Currency;
        var negative = minorUnits < 0;

        // Use unsigned magnitude so long.MinValue is handled.
        var magnitude = negative ? (ulong)(-(minorUnits + 1)) + 1 : (ulong)minorUnits;
        var scale = (ulong)currency.Scale;
        var whole = magnitude / scale;
        var fraction = magnitude % scale;

        var digits = whole.ToString(CultureInfo.InvariantCulture);
        var number = new StringBuilder(digits.Length + 8);
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                number.Append(format.GroupSeparator);
            }

            number.Append(digits[i]);
        }

        if (currency.MinorDigits > 0)
        {
            number.Append(format.DecimalSeparator)
                  .Append(fraction.ToString(CultureInfo.InvariantCulture).PadLeft(currency.MinorDigits, '0'));
        }

        var result = new StringBuilder();
        if (negative)
        {
            result.Append('-');
        }

        if (!includeSymbol)
        {
            return result.Append(number).ToString();
        }

        var space = currency.SpaceAfterSymbol ? " " : string.Empty;
        return currency.SymbolBeforeAmount
            ? result.Append(currency.Symbol).Append(space).Append(number).ToString()
            : result.Append(number).Append(' ').Append(currency.Symbol).ToString();
    }
}

/// <summary>Parses user-entered amounts into minor units without floating point.</summary>
public static class MoneyParser
{
    /// <summary>Like <see cref="TryParse"/> but accepts a leading minus sign (e.g. an overdrawn balance).</summary>
    public static bool TryParseSigned(string? text, RegionalFormat format, out long minorUnits)
    {
        minorUnits = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var negative = s.StartsWith('-') || s.StartsWith('−');
        if (!TryParse(negative ? s[1..] : s, format, out var magnitude))
        {
            return false;
        }

        minorUnits = negative ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Accepts digits with the configured group separator (optional) and decimal separator.
    /// Rejects more fractional digits than the currency allows, signs, and values above <see cref="MoneyMath.MaxAmount"/>.
    /// </summary>
    public static bool TryParse(string? text, RegionalFormat format, out long minorUnits)
    {
        ArgumentNullException.ThrowIfNull(format);
        minorUnits = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith(format.Currency.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            s = s[format.Currency.Symbol.Length..].Trim();
        }

        var group = format.GroupSeparator;
        var dec = format.DecimalSeparator;

        string wholePart;
        var fractionPart = string.Empty;
        var decIndex = s.IndexOf(dec, StringComparison.Ordinal);
        if (decIndex >= 0)
        {
            wholePart = s[..decIndex];
            fractionPart = s[(decIndex + 1)..];
            if (fractionPart.Length == 0 || fractionPart.Length > format.Currency.MinorDigits)
            {
                return false;
            }
        }
        else
        {
            wholePart = s;
        }

        var cleaned = new StringBuilder(wholePart.Length);
        foreach (var c in wholePart)
        {
            if (char.IsAsciiDigit(c))
            {
                cleaned.Append(c);
            }
            else if (c == group || (group == ' ' && c == ' '))
            {
                continue;
            }
            else
            {
                return false;
            }
        }

        if (cleaned.Length == 0 || !fractionPart.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!long.TryParse(cleaned.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            return false;
        }

        var fraction = fractionPart.Length == 0
            ? 0
            : long.Parse(fractionPart.PadRight(format.Currency.MinorDigits, '0'), NumberStyles.None, CultureInfo.InvariantCulture);

        try
        {
            var value = checked((whole * format.Currency.Scale) + fraction);
            if (value > MoneyMath.MaxAmount)
            {
                return false;
            }

            minorUnits = value;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
