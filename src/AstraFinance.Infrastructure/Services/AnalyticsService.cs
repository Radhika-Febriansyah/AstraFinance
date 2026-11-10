using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Aggregates for the Analytics page. Reads only non-deleted Income and Expense transactions in the range
/// (transfer fees are expenses); transfers and balance adjustments never count. Sums are exact <c>long</c>
/// arithmetic done in memory over a narrow projection, so no SQL date functions are involved.
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IDbContextFactory<AstraDbContext> _factory;

    public AnalyticsService(IDbContextFactory<AstraDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<AnalyticsSnapshot> GetSnapshotAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        if (range.End < range.Start)
        {
            throw new ArgumentException("The range ends before it starts.", nameof(range));
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = await db.Transactions.AsNoTracking()
                .Where(t => t.Date >= range.Start && t.Date <= range.End
                            && (t.Type == TransactionType.Income || t.Type == TransactionType.Expense))
                .Select(t => new { t.Id, t.Date, t.Type, t.Amount, t.CategoryId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var categoryIds = rows.Where(r => r.CategoryId is not null).Select(r => r.CategoryId!.Value).Distinct().ToList();
            var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);

            var income = rows.Where(r => r.Type == TransactionType.Income).ToList();
            var expense = rows.Where(r => r.Type == TransactionType.Expense).ToList();

            var bucket = AnalyticsPeriods.BucketFor(range);
            var series = new List<BucketTotals>();
            var bucketOf = new Func<DateOnly, DateOnly>(d => bucket == AnalyticsBucket.Day ? d : new DateOnly(d.Year, d.Month, 1));
            var incomeByBucket = income.GroupBy(r => bucketOf(r.Date)).ToDictionary(g => g.Key, g => MoneyMath.Sum(g.Select(r => r.Amount)));
            var expenseByBucket = expense.GroupBy(r => bucketOf(r.Date)).ToDictionary(g => g.Key, g => MoneyMath.Sum(g.Select(r => r.Amount)));
            for (var start = bucketOf(range.Start); start <= range.End; start = bucket == AnalyticsBucket.Day ? start.AddDays(1) : start.AddMonths(1))
            {
                series.Add(new BucketTotals(start, incomeByBucket.GetValueOrDefault(start), expenseByBucket.GetValueOrDefault(start)));
            }

            var weekdays = new long[7];
            foreach (var r in expense)
            {
                var index = ((int)r.Date.DayOfWeek + 6) % 7;
                weekdays[index] = checked(weekdays[index] + r.Amount);
            }

            Transaction? largest = null;
            var largestRow = expense.OrderByDescending(r => r.Amount).ThenByDescending(r => r.Date).FirstOrDefault();
            if (largestRow is not null)
            {
                largest = await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == largestRow.Id, cancellationToken).ConfigureAwait(false);
            }

            return new AnalyticsSnapshot(
                range,
                bucket,
                MoneyMath.Sum(income.Select(r => r.Amount)),
                MoneyMath.Sum(expense.Select(r => r.Amount)),
                income.Count,
                expense.Count,
                series,
                ByCategory(expense.Select(r => (r.CategoryId, r.Amount)), categories),
                ByCategory(income.Select(r => (r.CategoryId, r.Amount)), categories),
                weekdays,
                largest);
        }
    }

    /// <summary>Totals per category, largest first.</summary>
    internal static IReadOnlyList<CategoryAmount> ByCategory(IEnumerable<(Guid? CategoryId, long Amount)> rows, IReadOnlyDictionary<Guid, Category> categories) =>
        rows.GroupBy(r => r.CategoryId)
            .Select(g => new CategoryAmount(
                g.Key is { } id ? categories.GetValueOrDefault(id) : null,
                MoneyMath.Sum(g.Select(r => r.Amount)),
                g.Count()))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.Category?.SortOrder ?? int.MaxValue)
            .ToList();
}
