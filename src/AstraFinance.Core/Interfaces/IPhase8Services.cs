using System.Globalization;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;

namespace AstraFinance.Core.Interfaces;

// ============================== Analytics ==============================

public enum AnalyticsPeriod
{
    Week = 0,
    Month = 1,
    Year = 2,
    Custom = 3,
}

/// <summary>Granularity of time series: daily for short ranges, monthly otherwise.</summary>
public enum AnalyticsBucket
{
    Day = 0,
    Month = 1,
}

/// <summary>Resolves analytics periods. Weeks run Monday–Sunday (ISO 8601, as used in Indonesia).</summary>
public static class AnalyticsPeriods
{
    /// <summary>Ranges up to this many days are shown per day; longer ranges per month.</summary>
    public const int MaxDailyBucketDays = 62;

    public static DateRange Resolve(AnalyticsPeriod period, DateOnly anchor) => period switch
    {
        AnalyticsPeriod.Week => WeekOf(anchor),
        AnalyticsPeriod.Month => DateRange.MonthOf(anchor),
        AnalyticsPeriod.Year => new DateRange(new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31)),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Custom periods are given as a range."),
    };

    /// <summary>The anchor moved by one period (negative = back).</summary>
    public static DateOnly Shift(AnalyticsPeriod period, DateOnly anchor, int steps) => period switch
    {
        AnalyticsPeriod.Week => anchor.AddDays(7 * steps),
        AnalyticsPeriod.Month => anchor.AddMonths(steps),
        AnalyticsPeriod.Year => anchor.AddYears(steps),
        _ => anchor,
    };

    public static DateRange WeekOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        var start = date.AddDays(-offset);
        return new DateRange(start, start.AddDays(6));
    }

    public static AnalyticsBucket BucketFor(DateRange range) =>
        range.End.DayNumber - range.Start.DayNumber + 1 <= MaxDailyBucketDays ? AnalyticsBucket.Day : AnalyticsBucket.Month;

    public static int DayCount(DateRange range) => range.End.DayNumber - range.Start.DayNumber + 1;
}

/// <summary>An amount for one category (null category = uncategorised).</summary>
public sealed record CategoryAmount(Category? Category, long Amount, int Count);

/// <summary>Income and expense in one time bucket starting at <paramref name="Start"/>.</summary>
public sealed record BucketTotals(DateOnly Start, long Income, long Expense)
{
    public long Net => checked(Income - Expense);
}

/// <summary>
/// Everything the Analytics page shows for one period. Only Income and Expense transactions count
/// (transfer fees are expenses); transfers, balance adjustments and deleted records never do.
/// </summary>
/// <param name="ExpenseByWeekday">Expense totals Monday (index 0) to Sunday (index 6).</param>
public sealed record AnalyticsSnapshot(
    DateRange Range,
    AnalyticsBucket Bucket,
    long Income,
    long Expense,
    int IncomeCount,
    int ExpenseCount,
    IReadOnlyList<BucketTotals> Series,
    IReadOnlyList<CategoryAmount> ExpenseByCategory,
    IReadOnlyList<CategoryAmount> IncomeByCategory,
    IReadOnlyList<long> ExpenseByWeekday,
    Transaction? LargestExpense)
{
    public long Net => checked(Income - Expense);

    public bool HasData => IncomeCount + ExpenseCount > 0;

    /// <summary>Expenses divided by the number of days in the period (rounded down to whole minor units).</summary>
    public long AverageDailyExpense => Expense / AnalyticsPeriods.DayCount(Range);
}

public interface IAnalyticsService
{
    Task<AnalyticsSnapshot> GetSnapshotAsync(DateRange range, CancellationToken cancellationToken = default);
}

// ============================== Reports ==============================

public enum ReportKind
{
    Income = 0,
    Expense = 1,
    CashFlow = 2,
    AccountBalance = 3,
    Budget = 4,
    SavingsGoals = 5,
}

public enum ExportFormat
{
    Pdf = 0,
    Excel = 1,
    Csv = 2,
}

public enum ReportCellKind
{
    Text = 0,
    Money = 1,
    Date = 2,
    /// <summary>Whole percent (e.g. 85 = 85 %).</summary>
    Percent = 3,
    Number = 4,
}

/// <summary>A typed report value; exporters format it (money and dates follow the regional settings).</summary>
public readonly record struct ReportCell(ReportCellKind Kind, string? Text = null, long Value = 0, DateOnly Date = default)
{
    public static ReportCell Of(string? text) => new(ReportCellKind.Text, text ?? string.Empty);

    public static ReportCell Money(long minorUnits) => new(ReportCellKind.Money, Value: minorUnits);

    public static ReportCell Day(DateOnly date) => new(ReportCellKind.Date, Date: date);

    public static ReportCell Percent(long percent) => new(ReportCellKind.Percent, Value: percent);

    public static ReportCell Number(long value) => new(ReportCellKind.Number, Value: value);

    public static ReportCell Empty => new(ReportCellKind.Text, string.Empty);

    /// <summary>Human-readable text (amounts with currency symbol and grouping).</summary>
    public string Display(RegionalFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Kind switch
        {
            ReportCellKind.Money => MoneyFormatter.Format(Value, format),
            ReportCellKind.Date => format.FormatDate(Date),
            ReportCellKind.Percent => Value.ToString(CultureInfo.InvariantCulture) + "%",
            ReportCellKind.Number => Value.ToString(CultureInfo.InvariantCulture),
            _ => Text ?? string.Empty,
        };
    }
}

public sealed record ReportColumn(string Header, ReportCellKind Kind, double RelativeWidth = 1);

/// <summary>A table in a report. <paramref name="Total"/> is an optional totals row.</summary>
public sealed record ReportTable(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ReportCell>> Rows,
    IReadOnlyList<ReportCell>? Total = null,
    string? EmptyText = null);

public sealed record ReportSummaryItem(string Label, ReportCell Value);

/// <summary>A complete report, independent of the export format.</summary>
/// <param name="Notes">Explanations printed with the report (e.g. what is and isn't included).</param>
public sealed record ReportDocument(
    ReportKind Kind,
    string Title,
    string AppName,
    DateRange Period,
    string PeriodText,
    DateTime GeneratedAtLocal,
    string GeneratedText,
    RegionalFormat Format,
    IReadOnlyList<ReportSummaryItem> Summary,
    IReadOnlyList<ReportTable> Tables,
    IReadOnlyList<string> Notes)
{
    /// <summary>Total data rows across all tables (shown to the user so nothing is silently omitted).</summary>
    public int RowCount => Tables.Sum(t => t.Rows.Count);
}

public interface IReportService
{
    /// <summary>Builds a report from the database for the inclusive period.</summary>
    Task<ReportDocument> BuildAsync(ReportKind kind, DateRange period, CancellationToken cancellationToken = default);
}

public interface IReportExporter
{
    /// <summary>Writes the report to <paramref name="path"/> (atomically: a temporary file is moved into place).</summary>
    Task ExportAsync(ReportDocument report, ExportFormat format, string path, CancellationToken cancellationToken = default);
}
