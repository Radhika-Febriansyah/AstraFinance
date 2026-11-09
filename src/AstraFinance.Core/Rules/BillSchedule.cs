using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Rules;

/// <summary>
/// Occurrence arithmetic for recurring bills. Occurrence <c>n</c> is always computed from the first due date
/// (never from the previous occurrence), so a bill due on the 31st falls on the last day of shorter months and
/// returns to the 31st afterwards; a bill first due on 29 February falls on 28 February in other years.
/// </summary>
public static class BillSchedule
{
    /// <summary>Upper bound for occurrence counting, so a long-neglected daily bill can't cause a long loop.</summary>
    public const int MaxCountedOccurrences = 999;

    public static DateOnly Occurrence(DateOnly first, RecurrenceFrequency frequency, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return frequency switch
        {
            RecurrenceFrequency.Daily => first.AddDays(index),
            RecurrenceFrequency.Weekly => first.AddDays(checked(index * 7)),
            RecurrenceFrequency.Monthly => first.AddMonths(index),
            RecurrenceFrequency.Yearly => first.AddYears(index),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
        };
    }

    /// <summary>The first occurrence strictly after <paramref name="after"/> (the first due date when <paramref name="after"/> precedes it).</summary>
    public static DateOnly NextAfter(DateOnly first, RecurrenceFrequency frequency, DateOnly after)
    {
        if (after < first)
        {
            return first;
        }

        var index = EstimateIndex(first, frequency, after);
        while (index > 0 && Occurrence(first, frequency, index - 1) > after)
        {
            index--;
        }

        while (Occurrence(first, frequency, index) <= after)
        {
            index++;
        }

        return Occurrence(first, frequency, index);
    }

    /// <summary>True when <paramref name="date"/> is one of the bill's occurrences.</summary>
    public static bool IsOccurrence(DateOnly first, RecurrenceFrequency frequency, DateOnly date) =>
        date >= first && NextAfter(first, frequency, date.AddDays(-1)) == date;

    /// <summary>Number of occurrences from <paramref name="from"/> (inclusive, must be an occurrence) up to <paramref name="before"/> (exclusive), capped.</summary>
    public static int CountBetween(DateOnly first, RecurrenceFrequency frequency, DateOnly from, DateOnly before)
    {
        var count = 0;
        var date = from;
        while (date < before && count < MaxCountedOccurrences)
        {
            count++;
            date = NextAfter(first, frequency, date);
        }

        return count;
    }

    /// <summary>The latest occurrence on or before <paramref name="date"/>, or null when the bill starts later.</summary>
    public static DateOnly? LatestOnOrBefore(DateOnly first, RecurrenceFrequency frequency, DateOnly date)
    {
        if (date < first)
        {
            return null;
        }

        var index = EstimateIndex(first, frequency, date);
        while (index > 0 && Occurrence(first, frequency, index) > date)
        {
            index--;
        }

        while (Occurrence(first, frequency, index + 1) <= date)
        {
            index++;
        }

        return Occurrence(first, frequency, index);
    }

    private static int EstimateIndex(DateOnly first, RecurrenceFrequency frequency, DateOnly date)
    {
        var days = date.DayNumber - first.DayNumber;
        if (days <= 0)
        {
            return 0;
        }

        return frequency switch
        {
            RecurrenceFrequency.Daily => days,
            RecurrenceFrequency.Weekly => days / 7,
            RecurrenceFrequency.Monthly => ((date.Year - first.Year) * 12) + date.Month - first.Month,
            RecurrenceFrequency.Yearly => date.Year - first.Year,
            _ => 0,
        };
    }
}
