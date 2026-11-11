using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Services;

namespace AstraFinance.Tests.Core;

public class CalendarSummaryTests
{
    private static Transaction T(TransactionType type, long amount, DateOnly date, bool deleted = false) =>
        new() { Type = type, Amount = amount, Date = date, IsDeleted = deleted };

    [Theory]
    [InlineData(DayOfWeek.Monday, 2026, 10, 26)] // November 2026 starts on a Sunday → Monday grid starts 26 Oct
    [InlineData(DayOfWeek.Sunday, 2026, 11, 1)]
    public void Grid_is_six_full_weeks_starting_on_the_culture_first_day(DayOfWeek first, int y, int m, int d)
    {
        var grid = CalendarSummary.GridDates(2026, 11, first);

        Assert.Equal(42, grid.Count);
        Assert.Equal(new DateOnly(y, m, d), grid[0]);
        Assert.Equal(first, grid[0].DayOfWeek);
        Assert.Contains(new DateOnly(2026, 11, 1), grid);
        Assert.Contains(new DateOnly(2026, 11, 30), grid);
        Assert.All(grid.Zip(grid.Skip(1)), p => Assert.Equal(p.First.AddDays(1), p.Second));
    }

    [Fact]
    public void Day_totals_follow_the_financial_rules()
    {
        var day = new DateOnly(2026, 11, 9);
        var totals = CalendarSummary.ByDay(
        [
            T(TransactionType.Income, 8_000_000, day),
            T(TransactionType.Expense, 45_000, day),
            T(TransactionType.Expense, 2_500, day),             // a transfer fee is an expense
            T(TransactionType.Transfer, 500_000, day),          // never income or expense
            T(TransactionType.Expense, 999_999, day, deleted: true),
            T(TransactionType.Expense, 10_000, day.AddDays(1)),
        ]);

        var nine = totals[day];
        Assert.Equal(8_000_000, nine.Income);
        Assert.Equal(47_500, nine.Expense);
        Assert.Equal(7_952_500, nine.Net);
        Assert.Equal(4, nine.Count);
        Assert.Equal(10_000, totals[day.AddDays(1)].Expense);
    }

    [Fact]
    public void A_transfer_only_day_has_activity_but_no_income_or_expense()
    {
        var day = new DateOnly(2026, 11, 3);
        var totals = CalendarSummary.ByDay([T(TransactionType.Transfer, 1_000_000, day)]);
        Assert.Equal(new DayTotals(day, 0, 0, 1), totals[day]);
    }
}
