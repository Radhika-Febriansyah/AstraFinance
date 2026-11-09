namespace AstraFinance.Core.Money;

/// <summary>Overflow-checked integer money arithmetic. Overflow throws instead of wrapping silently.</summary>
public static class MoneyMath
{
    /// <summary>Largest absolute amount accepted for a single record (one quadrillion minor units).</summary>
    public const long MaxAmount = 1_000_000_000_000_000;

    public static long Add(long a, long b) => checked(a + b);

    public static long Subtract(long a, long b) => checked(a - b);

    public static long Sum(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        long total = 0;
        foreach (var v in values)
        {
            total = checked(total + v);
        }

        return total;
    }

    /// <summary>
    /// Integer percentage (0–∞) of <paramref name="part"/> relative to <paramref name="whole"/>, rounded down,
    /// computed without floating point.
    /// </summary>
    public static long PercentOf(long part, long whole)
    {
        if (whole <= 0)
        {
            return part > 0 ? 100 : 0;
        }

        return (long)(Int128.Max(0, part) * 100 / whole);
    }
}
