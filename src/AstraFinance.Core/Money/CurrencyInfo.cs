namespace AstraFinance.Core.Money;

/// <summary>
/// Describes a currency. Amounts are stored as integer minor units: value = minor / 10^<see cref="MinorDigits"/>.
/// Version 1 uses a single application-wide base currency (default IDR, 0 minor digits).
/// </summary>
public sealed record CurrencyInfo(string Code, string Symbol, int MinorDigits, bool SymbolBeforeAmount = true, bool SpaceAfterSymbol = false)
{
    public static readonly CurrencyInfo Idr = new("IDR", "Rp", 0);
    public static readonly CurrencyInfo Usd = new("USD", "$", 2);
    public static readonly CurrencyInfo Eur = new("EUR", "€", 2);
    public static readonly CurrencyInfo Sgd = new("SGD", "S$", 2);
    public static readonly CurrencyInfo Myr = new("MYR", "RM", 2);
    public static readonly CurrencyInfo Jpy = new("JPY", "¥", 0);

    public static IReadOnlyList<CurrencyInfo> Supported { get; } = [Idr, Usd, Eur, Sgd, Myr, Jpy];

    public static CurrencyInfo Default => Idr;

    /// <summary>10^MinorDigits.</summary>
    public long Scale => MinorDigits switch
    {
        0 => 1,
        1 => 10,
        2 => 100,
        3 => 1000,
        _ => throw new InvalidOperationException($"Unsupported minor digits {MinorDigits}."),
    };

    public static CurrencyInfo FromCode(string? code) =>
        Supported.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
