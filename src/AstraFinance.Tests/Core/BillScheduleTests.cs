using AstraFinance.Core.Enums;
using AstraFinance.Core.Rules;

namespace AstraFinance.Tests.Core;

public class BillScheduleTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Monthly_on_the_31st_uses_month_end_and_returns_to_the_31st()
    {
        var first = D(2027, 1, 31);
        Assert.Equal(D(2027, 2, 28), BillSchedule.Occurrence(first, RecurrenceFrequency.Monthly, 1));
        Assert.Equal(D(2027, 3, 31), BillSchedule.Occurrence(first, RecurrenceFrequency.Monthly, 2));
        Assert.Equal(D(2027, 4, 30), BillSchedule.Occurrence(first, RecurrenceFrequency.Monthly, 3));

        Assert.Equal(D(2027, 3, 31), BillSchedule.NextAfter(first, RecurrenceFrequency.Monthly, D(2027, 2, 28)));
        Assert.Equal(D(2027, 2, 28), BillSchedule.NextAfter(first, RecurrenceFrequency.Monthly, D(2027, 1, 31)));
    }

    [Fact]
    public void Yearly_on_29_February_falls_on_28_February_in_common_years()
    {
        var first = D(2028, 2, 29);
        Assert.Equal(D(2029, 2, 28), BillSchedule.NextAfter(first, RecurrenceFrequency.Yearly, first));
        Assert.Equal(D(2032, 2, 29), BillSchedule.Occurrence(first, RecurrenceFrequency.Yearly, 4));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, "2026-11-10")]
    [InlineData(RecurrenceFrequency.Weekly, "2026-11-12")]
    [InlineData(RecurrenceFrequency.Monthly, "2026-12-05")]
    [InlineData(RecurrenceFrequency.Yearly, "2027-11-05")]
    public void Next_occurrence_after_a_date(RecurrenceFrequency frequency, string expected)
    {
        // First due 5 Nov 2026 (a Thursday); next after 9 Nov.
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            BillSchedule.NextAfter(D(2026, 11, 5), frequency, D(2026, 11, 9)));
    }

    [Fact]
    public void Before_the_first_due_date_the_next_occurrence_is_the_first()
    {
        Assert.Equal(D(2026, 12, 1), BillSchedule.NextAfter(D(2026, 12, 1), RecurrenceFrequency.Monthly, D(2026, 11, 9)));
        Assert.Null(BillSchedule.LatestOnOrBefore(D(2026, 12, 1), RecurrenceFrequency.Monthly, D(2026, 11, 9)));
    }

    [Fact]
    public void Latest_occurrence_count_and_membership()
    {
        var first = D(2026, 7, 15);
        Assert.Equal(D(2026, 10, 15), BillSchedule.LatestOnOrBefore(first, RecurrenceFrequency.Monthly, D(2026, 11, 8)));
        Assert.Equal(D(2026, 11, 15), BillSchedule.LatestOnOrBefore(first, RecurrenceFrequency.Monthly, D(2026, 11, 15)));
        Assert.Equal(4, BillSchedule.CountBetween(first, RecurrenceFrequency.Monthly, first, D(2026, 11, 9)));
        Assert.True(BillSchedule.IsOccurrence(first, RecurrenceFrequency.Monthly, D(2026, 9, 15)));
        Assert.False(BillSchedule.IsOccurrence(first, RecurrenceFrequency.Monthly, D(2026, 9, 16)));

        // A long-neglected daily bill is counted with a cap, not an unbounded loop.
        Assert.Equal(BillSchedule.MaxCountedOccurrences,
            BillSchedule.CountBetween(D(2020, 1, 1), RecurrenceFrequency.Daily, D(2020, 1, 1), D(2026, 11, 9)));
    }
}
