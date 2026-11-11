using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Services;

/// <summary>Income and expense recorded on one calendar day.</summary>
/// <param name="Date">The day.</param>
/// <param name="Income">Sum of Income transactions (minor units).</param>
/// <param name="Expense">Sum of Expense transactions, including transfer fees (minor units).</param>
/// <param name="Count">Number of transactions on the day, transfers included.</param>
public sealed record DayTotals(DateOnly Date, long Income, long Expense, int Count)
{
    public long Net => checked(Income - Expense);
}

/// <summary>
/// Read-only calendar view of transactions. Follows the app's financial rules: transfers move money
/// between the user's own accounts and never count as income or expense; deleted transactions are ignored.
/// </summary>
public static class CalendarSummary
{
    public const int GridDays = 42; // 6 weeks × 7 days

    /// <summary>
    /// The dates shown for a month: six full weeks starting on <paramref name="firstDayOfWeek"/>
    /// on or before the 1st, so every week of the month is complete.
    /// </summary>
    public static IReadOnlyList<DateOnly> GridDates(int year, int month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, GridDays).Select(start.AddDays).ToList();
    }

    /// <summary>Totals per day for the given transactions.</summary>
    public static IReadOnlyDictionary<DateOnly, DayTotals> ByDay(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var result = new Dictionary<DateOnly, DayTotals>();
        foreach (var t in transactions)
        {
            if (t.IsDeleted)
            {
                continue;
            }

            var day = result.TryGetValue(t.Date, out var existing) ? existing : new DayTotals(t.Date, 0, 0, 0);
            result[t.Date] = t.Type switch
            {
                TransactionType.Income => day with { Income = checked(day.Income + t.Amount), Count = day.Count + 1 },
                TransactionType.Expense => day with { Expense = checked(day.Expense + t.Amount), Count = day.Count + 1 },
                _ => day with { Count = day.Count + 1 }, // transfers: activity only, never income or expense
            };
        }

        return result;
    }
}
