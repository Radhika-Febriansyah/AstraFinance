using AstraFinance.Core.Enums;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;

namespace AstraFinance.Tests.Core;

public class MoneyAndRulesTests
{
    private static readonly RegionalFormat Idr = RegionalFormat.Default;
    private static readonly RegionalFormat UsdEnglish = new(CurrencyInfo.Usd, NumberFormatStyle.CommaGroupDotDecimal, DateFormatStyle.MonthDayYear);

    [Theory]
    [InlineData(0, "Rp0")]
    [InlineData(500, "Rp500")]
    [InlineData(8_000_000, "Rp8.000.000")]
    [InlineData(1_234_567, "Rp1.234.567")]
    [InlineData(-25_000, "-Rp25.000")]
    public void Formats_rupiah_as_whole_units(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.Format(amount, Idr));

    [Theory]
    [InlineData(123_450, "$1,234.50")]
    [InlineData(5, "$0.05")]
    [InlineData(-1_050, "-$10.50")]
    public void Formats_fractional_currencies_exactly(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.Format(amount, UsdEnglish));

    [Fact]
    public void Formats_extreme_values_without_overflow()
    {
        Assert.Equal("-9.223.372.036.854.775.808", MoneyFormatter.Format(long.MinValue, Idr, includeSymbol: false));
        Assert.Equal("9.223.372.036.854.775.807", MoneyFormatter.Format(long.MaxValue, Idr, includeSymbol: false));
    }

    [Theory]
    [InlineData("1.234.567", 1_234_567)]
    [InlineData("1234567", 1_234_567)]
    [InlineData("Rp 50.000", 50_000)]
    [InlineData("  0 ", 0)]
    public void Parses_rupiah(string text, long expected)
    {
        Assert.True(MoneyParser.TryParse(text, Idr, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1,5")] // IDR has no minor unit
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.2a")]
    [InlineData("99999999999999999999999")]
    public void Rejects_invalid_rupiah(string text) => Assert.False(MoneyParser.TryParse(text, Idr, out _));

    [Theory]
    [InlineData("12.34", 1_234)]
    [InlineData("1,234.5", 123_450)]
    [InlineData("$7", 700)]
    public void Parses_fractional_currency(string text, long expected)
    {
        Assert.True(MoneyParser.TryParse(text, UsdEnglish, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Rejects_more_decimals_than_currency_allows() =>
        Assert.False(MoneyParser.TryParse("12.345", UsdEnglish, out _));

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(999L)]
    [InlineData(1_000L)]
    [InlineData(123_456_789_012L)]
    public void Format_then_parse_round_trips(long amount)
    {
        Assert.True(MoneyParser.TryParse(MoneyFormatter.Format(amount, Idr), Idr, out var idr));
        Assert.Equal(amount, idr);
        Assert.True(MoneyParser.TryParse(MoneyFormatter.Format(amount, UsdEnglish), UsdEnglish, out var usd));
        Assert.Equal(amount, usd);
    }

    [Fact]
    public void Money_math_throws_on_overflow_instead_of_wrapping()
    {
        Assert.Throws<OverflowException>(() => MoneyMath.Add(long.MaxValue, 1));
        Assert.Throws<OverflowException>(() => MoneyMath.Sum([long.MaxValue, 1]));
        Assert.Equal(50, MoneyMath.PercentOf(4_000_000, 8_000_000));
    }

    [Fact]
    public void Balance_formula_matches_specification()
    {
        // Opening + Income − Expense + Transfers In − Transfers Out + Adjustments
        var balance = BalanceCalculator.Compute(new BalanceComponents(1_000, 500, 200, 300, 100, -50));
        Assert.Equal(1_450, balance);
    }

    [Theory]
    [InlineData(790, BudgetStatus.Normal, 79)]
    [InlineData(800, BudgetStatus.Warning, 80)]
    [InlineData(1_000, BudgetStatus.Warning, 100)]
    [InlineData(1_001, BudgetStatus.Exceeded, 100)]
    [InlineData(2_500, BudgetStatus.Exceeded, 250)]
    [InlineData(0, BudgetStatus.Normal, 0)]
    public void Budget_status_uses_default_threshold(long spent, BudgetStatus status, long percent)
    {
        var result = BudgetEvaluator.Evaluate(1_000, spent);
        Assert.Equal(status, result.Status);
        Assert.Equal(percent, result.UsagePercent);
        Assert.Equal(1_000 - spent, result.Remaining);
    }

    [Fact]
    public void Budget_threshold_is_configurable_and_exact()
    {
        Assert.Equal(BudgetStatus.Normal, BudgetEvaluator.Evaluate(3, 1, 50).Status);   // 33%
        Assert.Equal(BudgetStatus.Warning, BudgetEvaluator.Evaluate(3, 2, 50).Status);  // 66%
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetEvaluator.Evaluate(0, 1));
    }

    [Fact]
    public void Allocations_cannot_exceed_unallocated_balance()
    {
        Assert.Equal(6_000, AllocationRules.Available(10_000, 4_000));
        Assert.True(AllocationRules.CanAllocate(6_000, 10_000, 4_000));
        Assert.False(AllocationRules.CanAllocate(6_001, 10_000, 4_000));
        Assert.False(AllocationRules.CanAllocate(0, 10_000, 0));
        Assert.Equal(0, AllocationRules.Available(1_000, 4_000)); // balance fell below allocations
        Assert.True(AllocationRules.CanRelease(4_000, 4_000));
        Assert.False(AllocationRules.CanRelease(4_001, 4_000));
    }

    [Fact]
    public void Month_range_handles_leap_years()
    {
        Assert.Equal(new DateOnly(2028, 2, 29), DateRange.Month(2028, 2).End);
        Assert.Equal(new DateOnly(2026, 2, 28), DateRange.Month(2026, 2).End);
        Assert.True(DateRange.MonthOf(new DateOnly(2026, 11, 30)).Contains(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Regional_date_formats()
    {
        var date = new DateOnly(2026, 12, 31);
        Assert.Equal("31/12/2026", Idr.FormatDate(date));
        Assert.Equal("12/31/2026", UsdEnglish.FormatDate(date));
    }
}
