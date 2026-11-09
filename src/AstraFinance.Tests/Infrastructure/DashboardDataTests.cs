using AstraFinance.Core.Enums;
using AstraFinance.Core.Money;

namespace AstraFinance.Tests.Infrastructure;

public class DashboardDataTests
{
    private static readonly string[] English = ["K", "M", "B", "T"];
    private static readonly string[] Indonesian = ["rb", "jt", "M", "T"];

    [Fact]
    public async Task Monthly_totals_cover_every_month_and_exclude_transfers_and_deleted()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 10_000_000);
        var cash = await ctx.NewAccountAsync("Cash");
        await ctx.IncomeAsync(bank, 8_000_000, new DateOnly(2026, 11, 1));
        await ctx.ExpenseAsync(bank, 300_000, date: new DateOnly(2026, 11, 30));
        await ctx.ExpenseAsync(bank, 120_000, date: new DateOnly(2026, 9, 15));
        await ctx.ExpenseAsync(bank, 999_000, date: new DateOnly(2026, 5, 31));   // outside the 6-month window
        await ctx.TransferAsync(bank, cash, 1_000_000, fee: 2_500);                // fee counts, transfer doesn't
        var deleted = await ctx.ExpenseAsync(bank, 77_000);
        await ctx.Transactions.DeleteAsync(deleted.Id);

        var months = await ctx.Transactions.GetMonthlyTotalsAsync(FinanceTestContext.Today, 6);

        Assert.Equal(6, months.Count);
        Assert.Equal((2026, 6), (months[0].Year, months[0].Month));
        Assert.Equal((2026, 11), (months[^1].Year, months[^1].Month));
        Assert.Equal(8_000_000, months[^1].Income);
        Assert.Equal(302_500, months[^1].Expense);
        Assert.Equal(120_000, months.Single(m => m.Month == 9).Expense);
        Assert.All(months.Where(m => m.Month is 6 or 7 or 8 or 10), m => Assert.Equal((0L, 0L), (m.Income, m.Expense)));
    }

    [Fact]
    public async Task Monthly_totals_cross_year_boundaries()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank");
        await ctx.IncomeAsync(bank, 1_000, new DateOnly(2025, 12, 31));
        await ctx.IncomeAsync(bank, 2_000, new DateOnly(2026, 1, 1));

        var months = await ctx.Transactions.GetMonthlyTotalsAsync(new DateOnly(2026, 2, 10), 3);

        Assert.Equal([(2025, 12, 1_000L), (2026, 1, 2_000L), (2026, 2, 0L)], months.Select(m => (m.Year, m.Month, m.Income)));
    }

    [Theory]
    [InlineData(0, "Rp0")]
    [InlineData(950, "Rp950")]
    [InlineData(1_500, "Rp1,5rb")]
    [InlineData(8_000_000, "Rp8jt")]
    [InlineData(1_250_000, "Rp1,3jt")]
    [InlineData(2_000_000_000, "Rp2M")]
    [InlineData(-300_000, "-Rp300rb")]
    public void Compact_rupiah_labels(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.FormatCompact(amount, RegionalFormat.Default, Indonesian));

    [Theory]
    [InlineData(123_456_789, "$1.2M")]
    [InlineData(99, "$1")]
    public void Compact_labels_for_fractional_currencies(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.FormatCompact(amount, new RegionalFormat(CurrencyInfo.Usd, NumberFormatStyle.CommaGroupDotDecimal, DateFormatStyle.DayMonthYear), English));
}
