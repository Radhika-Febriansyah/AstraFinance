using System.Globalization;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Builds exportable reports from the database. Every report lists all matching records (no paging or
/// truncation) and states what is included. Deleted records are excluded by the global query filters.
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IBudgetService _budgets;
    private readonly ILocalizationService _loc;
    private readonly IRegionalFormatService _format;
    private readonly IClock _clock;

    public ReportService(IDbContextFactory<AstraDbContext> factory, IBudgetService budgets, ILocalizationService localization, IRegionalFormatService format, IClock clock)
    {
        _factory = factory;
        _budgets = budgets;
        _loc = localization;
        _format = format;
        _clock = clock;
    }

    public async Task<ReportDocument> BuildAsync(ReportKind kind, DateRange period, CancellationToken cancellationToken = default)
    {
        if (period.End < period.Start)
        {
            throw new ArgumentException("The period ends before it starts.", nameof(period));
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var (summary, tables, notes) = kind switch
            {
                ReportKind.Income => await TransactionsReportAsync(db, TransactionType.Income, period, cancellationToken).ConfigureAwait(false),
                ReportKind.Expense => await TransactionsReportAsync(db, TransactionType.Expense, period, cancellationToken).ConfigureAwait(false),
                ReportKind.CashFlow => await CashFlowAsync(db, period, cancellationToken).ConfigureAwait(false),
                ReportKind.AccountBalance => await AccountBalancesAsync(db, period, cancellationToken).ConfigureAwait(false),
                ReportKind.Budget => await BudgetsAsync(period, cancellationToken).ConfigureAwait(false),
                ReportKind.SavingsGoals => await GoalsAsync(db, period, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

            var format = _format.Current;
            var generated = _clock.LocalNow;
            return new ReportDocument(
                kind,
                _loc.GetString($"Report.{kind}.Title"),
                _loc.GetString("App.Name"),
                period,
                _loc.Format("Report.Period", format.FormatDate(period.Start), format.FormatDate(period.End)),
                generated,
                _loc.Format("Report.Generated", format.FormatDate(DateOnly.FromDateTime(generated)), generated.ToString("HH:mm", CultureInfo.InvariantCulture)),
                format,
                summary,
                tables,
                notes);
        }
    }

    private string T(string key) => _loc.GetString(key);

    // ---------------------------------------------------------------- Income / Expense

    private async Task<(List<ReportSummaryItem>, List<ReportTable>, List<string>)> TransactionsReportAsync(
        AstraDbContext db, TransactionType type, DateRange period, CancellationToken ct)
    {
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Type == type && t.Date >= period.Start && t.Date <= period.End)
            .OrderBy(t => t.Date).ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct).ConfigureAwait(false);

        var total = MoneyMath.Sum(rows.Select(r => r.Amount));
        var byCategory = AnalyticsService.ByCategory(rows.Select(r => (r.CategoryId, r.Amount)), categories);
        var prefix = type == TransactionType.Income ? "Report.Income" : "Report.Expense";

        var summary = new List<ReportSummaryItem>
        {
            new(T($"{prefix}.Total"), ReportCell.Money(total)),
            new(T("Report.TransactionCount"), ReportCell.Number(rows.Count)),
            new(T("Report.AveragePerTransaction"), ReportCell.Money(rows.Count == 0 ? 0 : total / rows.Count)),
        };
        if (type == TransactionType.Expense)
        {
            summary.Add(new(T("Report.AveragePerDay"), ReportCell.Money(total / AnalyticsPeriods.DayCount(period))));
        }

        var categoryTable = new ReportTable(
            T("Report.ByCategory"),
            [new(T("Report.Column.Category"), ReportCellKind.Text, 3), new(T("Report.Column.Count"), ReportCellKind.Number), new(T("Report.Column.Amount"), ReportCellKind.Money, 2), new(T("Report.Column.Share"), ReportCellKind.Percent)],
            byCategory.Select(c => (IReadOnlyList<ReportCell>)
            [
                ReportCell.Of(CategoryName(c.Category)),
                ReportCell.Number(c.Count),
                ReportCell.Money(c.Amount),
                ReportCell.Percent(MoneyMath.PercentOf(c.Amount, total)),
            ]).ToList(),
            [ReportCell.Of(T("Report.Total")), ReportCell.Number(rows.Count), ReportCell.Money(total), ReportCell.Empty],
            T($"{prefix}.Empty"));

        var detailTable = new ReportTable(
            T("Report.Transactions"),
            [new(T("Report.Column.Date"), ReportCellKind.Date, 1.2), new(T("Report.Column.Description"), ReportCellKind.Text, 3), new(T("Report.Column.Category"), ReportCellKind.Text, 2), new(T("Report.Column.Account"), ReportCellKind.Text, 2), new(T("Report.Column.Amount"), ReportCellKind.Money, 1.8)],
            rows.Select(r => (IReadOnlyList<ReportCell>)
            [
                ReportCell.Day(r.Date),
                ReportCell.Of(Describe(r)),
                ReportCell.Of(CategoryName(r.CategoryId is { } id ? categories.GetValueOrDefault(id) : null)),
                ReportCell.Of(accounts.GetValueOrDefault(r.AccountId, "?")),
                ReportCell.Money(r.Amount),
            ]).ToList(),
            [ReportCell.Of(T("Report.Total")), ReportCell.Empty, ReportCell.Empty, ReportCell.Empty, ReportCell.Money(total)],
            T($"{prefix}.Empty"));

        return (summary, [categoryTable, detailTable], [T($"{prefix}.Note")]);
    }

    // ---------------------------------------------------------------- Cash flow

    private async Task<(List<ReportSummaryItem>, List<ReportTable>, List<string>)> CashFlowAsync(AstraDbContext db, DateRange period, CancellationToken ct)
    {
        var openingBalances = MoneyMath.Sum(await db.Accounts.AsNoTracking().Select(a => a.OpeningBalance).ToListAsync(ct).ConfigureAwait(false));
        var tx = await db.Transactions.AsNoTracking()
            .Where(t => t.Date <= period.End && (t.Type == TransactionType.Income || t.Type == TransactionType.Expense))
            .Select(t => new { t.Date, t.Type, t.Amount })
            .ToListAsync(ct).ConfigureAwait(false);
        var adjustments = await db.BalanceAdjustments.AsNoTracking()
            .Where(a => a.Date <= period.End)
            .Select(a => new { a.Date, a.Amount })
            .ToListAsync(ct).ConfigureAwait(false);

        // Transfers move money between the user's own accounts, so they cancel out in the total balance.
        long Signed(TransactionType type, long amount) => type == TransactionType.Income ? amount : -amount;
        var opening = MoneyMath.Sum(
            new[] { openingBalances }
                .Concat(tx.Where(t => t.Date < period.Start).Select(t => Signed(t.Type, t.Amount)))
                .Concat(adjustments.Where(a => a.Date < period.Start).Select(a => a.Amount)));
        var inPeriod = tx.Where(t => t.Date >= period.Start).ToList();
        var income = MoneyMath.Sum(inPeriod.Where(t => t.Type == TransactionType.Income).Select(t => t.Amount));
        var expense = MoneyMath.Sum(inPeriod.Where(t => t.Type == TransactionType.Expense).Select(t => t.Amount));
        var adjusted = MoneyMath.Sum(adjustments.Where(a => a.Date >= period.Start).Select(a => a.Amount));
        var closing = MoneyMath.Sum([opening, income, -expense, adjusted]);

        var bucket = AnalyticsPeriods.BucketFor(period);
        DateOnly BucketOf(DateOnly d) => bucket == AnalyticsBucket.Day ? d : new DateOnly(d.Year, d.Month, 1);
        var rows = new List<IReadOnlyList<ReportCell>>();
        var running = opening;
        for (var start = BucketOf(period.Start); start <= period.End; start = bucket == AnalyticsBucket.Day ? start.AddDays(1) : start.AddMonths(1))
        {
            var bucketIncome = MoneyMath.Sum(inPeriod.Where(t => t.Type == TransactionType.Income && BucketOf(t.Date) == start).Select(t => t.Amount));
            var bucketExpense = MoneyMath.Sum(inPeriod.Where(t => t.Type == TransactionType.Expense && BucketOf(t.Date) == start).Select(t => t.Amount));
            var bucketAdjusted = MoneyMath.Sum(adjustments.Where(a => a.Date >= period.Start && BucketOf(a.Date) == start).Select(a => a.Amount));
            running = MoneyMath.Sum([running, bucketIncome, -bucketExpense, bucketAdjusted]);
            rows.Add(
            [
                ReportCell.Of(bucket == AnalyticsBucket.Day ? _format.Current.FormatDate(start) : MonthName(start)),
                ReportCell.Money(bucketIncome),
                ReportCell.Money(bucketExpense),
                ReportCell.Money(bucketIncome - bucketExpense),
                ReportCell.Money(bucketAdjusted),
                ReportCell.Money(running),
            ]);
        }

        var summary = new List<ReportSummaryItem>
        {
            new(T("Report.CashFlow.Opening"), ReportCell.Money(opening)),
            new(T("Report.CashFlow.Income"), ReportCell.Money(income)),
            new(T("Report.CashFlow.Expense"), ReportCell.Money(expense)),
            new(T("Report.CashFlow.Net"), ReportCell.Money(income - expense)),
            new(T("Report.CashFlow.Adjustments"), ReportCell.Money(adjusted)),
            new(T("Report.CashFlow.Closing"), ReportCell.Money(closing)),
        };
        var table = new ReportTable(
            T(bucket == AnalyticsBucket.Day ? "Report.CashFlow.ByDay" : "Report.CashFlow.ByMonth"),
            [new(T("Report.Column.Period"), ReportCellKind.Text, 1.6), new(T("Report.Column.Income"), ReportCellKind.Money), new(T("Report.Column.Expenses"), ReportCellKind.Money), new(T("Report.Column.Net"), ReportCellKind.Money), new(T("Report.Column.Adjustments"), ReportCellKind.Money), new(T("Report.Column.BalanceAfter"), ReportCellKind.Money)],
            rows,
            [ReportCell.Of(T("Report.Total")), ReportCell.Money(income), ReportCell.Money(expense), ReportCell.Money(income - expense), ReportCell.Money(adjusted), ReportCell.Money(closing)]);
        return (summary, [table], [T("Report.CashFlow.Note")]);
    }

    // ---------------------------------------------------------------- Account balances

    private async Task<(List<ReportSummaryItem>, List<ReportTable>, List<string>)> AccountBalancesAsync(AstraDbContext db, DateRange period, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        var tx = await db.Transactions.AsNoTracking()
            .Where(t => t.Date <= period.End)
            .Select(t => new { t.Date, t.Type, t.Amount, t.AccountId, t.DestinationAccountId })
            .ToListAsync(ct).ConfigureAwait(false);
        var adjustments = await db.BalanceAdjustments.AsNoTracking()
            .Where(a => a.Date <= period.End)
            .Select(a => new { a.Date, a.Amount, a.AccountId })
            .ToListAsync(ct).ConfigureAwait(false);

        var rows = new List<IReadOnlyList<ReportCell>>();
        long tOpening = 0, tIncome = 0, tExpense = 0, tIn = 0, tOut = 0, tAdjusted = 0, tClosing = 0;
        var before = tx.Where(t => t.Date < period.Start).ToList();
        var during = tx.Where(t => t.Date >= period.Start).ToList();
        foreach (var a in accounts)
        {
            // Balance before the period: opening balance plus all earlier activity.
            var opening = BalanceCalculator.Compute(new BalanceComponents(
                a.OpeningBalance,
                MoneyMath.Sum(before.Where(t => t.Type == TransactionType.Income && t.AccountId == a.Id).Select(t => t.Amount)),
                MoneyMath.Sum(before.Where(t => t.Type == TransactionType.Expense && t.AccountId == a.Id).Select(t => t.Amount)),
                MoneyMath.Sum(before.Where(t => t.Type == TransactionType.Transfer && t.DestinationAccountId == a.Id).Select(t => t.Amount)),
                MoneyMath.Sum(before.Where(t => t.Type == TransactionType.Transfer && t.AccountId == a.Id).Select(t => t.Amount)),
                MoneyMath.Sum(adjustments.Where(x => x.Date < period.Start && x.AccountId == a.Id).Select(x => x.Amount))));
            var income = MoneyMath.Sum(during.Where(t => t.Type == TransactionType.Income && t.AccountId == a.Id).Select(t => t.Amount));
            var expense = MoneyMath.Sum(during.Where(t => t.Type == TransactionType.Expense && t.AccountId == a.Id).Select(t => t.Amount));
            var transferIn = MoneyMath.Sum(during.Where(t => t.Type == TransactionType.Transfer && t.DestinationAccountId == a.Id).Select(t => t.Amount));
            var transferOut = MoneyMath.Sum(during.Where(t => t.Type == TransactionType.Transfer && t.AccountId == a.Id).Select(t => t.Amount));
            var adjusted = MoneyMath.Sum(adjustments.Where(x => x.Date >= period.Start && x.AccountId == a.Id).Select(x => x.Amount));
            var closing = MoneyMath.Sum([opening, income, -expense, transferIn, -transferOut, adjusted]);

            rows.Add(
            [
                ReportCell.Of(a.Name),
                ReportCell.Of(T($"AccountType.{a.Type}") + (a.IsActive ? string.Empty : $" ({T("Accounts.Inactive")})")),
                ReportCell.Money(opening),
                ReportCell.Money(income),
                ReportCell.Money(expense),
                ReportCell.Money(transferIn),
                ReportCell.Money(transferOut),
                ReportCell.Money(adjusted),
                ReportCell.Money(closing),
            ]);
            tOpening = checked(tOpening + opening);
            tIncome = checked(tIncome + income);
            tExpense = checked(tExpense + expense);
            tIn = checked(tIn + transferIn);
            tOut = checked(tOut + transferOut);
            tAdjusted = checked(tAdjusted + adjusted);
            tClosing = checked(tClosing + closing);
        }

        var summary = new List<ReportSummaryItem>
        {
            new(T("Report.Accounts.OpeningTotal"), ReportCell.Money(tOpening)),
            new(T("Report.Accounts.ClosingTotal"), ReportCell.Money(tClosing)),
            new(T("Report.Accounts.Count"), ReportCell.Number(accounts.Count)),
        };
        var table = new ReportTable(
            T("Report.Accounts.Table"),
            [
                new(T("Report.Column.Account"), ReportCellKind.Text, 2.2), new(T("Report.Column.Type"), ReportCellKind.Text, 1.8),
                new(T("Report.Column.Opening"), ReportCellKind.Money, 1.5), new(T("Report.Column.Income"), ReportCellKind.Money, 1.5),
                new(T("Report.Column.Expenses"), ReportCellKind.Money, 1.5), new(T("Report.Column.TransfersIn"), ReportCellKind.Money, 1.5),
                new(T("Report.Column.TransfersOut"), ReportCellKind.Money, 1.5), new(T("Report.Column.Adjustments"), ReportCellKind.Money, 1.5),
                new(T("Report.Column.Closing"), ReportCellKind.Money, 1.5),
            ],
            rows,
            [ReportCell.Of(T("Report.Total")), ReportCell.Empty, ReportCell.Money(tOpening), ReportCell.Money(tIncome), ReportCell.Money(tExpense), ReportCell.Money(tIn), ReportCell.Money(tOut), ReportCell.Money(tAdjusted), ReportCell.Money(tClosing)],
            T("Report.Accounts.Empty"));
        return (summary, [table], [T("Report.Accounts.Note")]);
    }

    // ---------------------------------------------------------------- Budgets

    private async Task<(List<ReportSummaryItem>, List<ReportTable>, List<string>)> BudgetsAsync(DateRange period, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<ReportCell>>();
        long limit = 0, spent = 0;
        var exceeded = 0;
        for (var month = DateRange.MonthOf(period.Start).Start; month <= period.End; month = month.AddMonths(1))
        {
            var overview = await _budgets.GetOverviewAsync(month, ct).ConfigureAwait(false);
            foreach (var item in overview.Items.OrderBy(i => i.Category.SortOrder).ThenBy(i => i.Category.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var e = item.Evaluation;
                rows.Add(
                [
                    ReportCell.Of(MonthName(month)),
                    ReportCell.Of(CategoryName(item.Category)),
                    ReportCell.Money(e.Limit),
                    ReportCell.Money(e.Spent),
                    ReportCell.Money(e.Remaining),
                    ReportCell.Percent(e.UsagePercent),
                    ReportCell.Of(T($"BudgetStatus.{e.Status}")),
                ]);
                limit = checked(limit + e.Limit);
                spent = checked(spent + e.Spent);
                exceeded += e.Status == BudgetStatus.Exceeded ? 1 : 0;
            }
        }

        var summary = new List<ReportSummaryItem>
        {
            new(T("Report.Budget.TotalLimit"), ReportCell.Money(limit)),
            new(T("Report.Budget.TotalSpent"), ReportCell.Money(spent)),
            new(T("Report.Budget.Remaining"), ReportCell.Money(limit - spent)),
            new(T("Report.Budget.Exceeded"), ReportCell.Number(exceeded)),
        };
        var table = new ReportTable(
            T("Report.Budget.Table"),
            [new(T("Report.Column.Month"), ReportCellKind.Text, 1.6), new(T("Report.Column.Category"), ReportCellKind.Text, 2), new(T("Report.Column.Limit"), ReportCellKind.Money, 1.5), new(T("Report.Column.Spent"), ReportCellKind.Money, 1.5), new(T("Report.Column.Remaining"), ReportCellKind.Money, 1.5), new(T("Report.Column.Used"), ReportCellKind.Percent), new(T("Report.Column.Status"), ReportCellKind.Text, 1.4)],
            rows,
            [ReportCell.Of(T("Report.Total")), ReportCell.Empty, ReportCell.Money(limit), ReportCell.Money(spent), ReportCell.Money(limit - spent), ReportCell.Percent(MoneyMath.PercentOf(spent, limit)), ReportCell.Empty],
            T("Report.Budget.Empty"));
        return (summary, [table], [T("Report.Budget.Note")]);
    }

    // ---------------------------------------------------------------- Savings goals

    private async Task<(List<ReportSummaryItem>, List<ReportTable>, List<string>)> GoalsAsync(AstraDbContext db, DateRange period, CancellationToken ct)
    {
        var goals = await db.SavingsGoals.AsNoTracking().OrderBy(g => g.Status).ThenBy(g => g.Name).ToListAsync(ct).ConfigureAwait(false);
        var allocations = await db.GoalAllocations.AsNoTracking().Where(a => a.Date <= period.End)
            .OrderBy(a => a.Date).ThenBy(a => a.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct).ConfigureAwait(false);
        var goalNames = goals.ToDictionary(g => g.Id, g => g.Name);

        var goalRows = new List<IReadOnlyList<ReportCell>>();
        long totalTarget = 0, totalAllocated = 0;
        foreach (var g in goals)
        {
            var allocated = MoneyMath.Sum(allocations.Where(a => a.GoalId == g.Id).Select(a => a.Amount));
            goalRows.Add(
            [
                ReportCell.Of(g.Name),
                ReportCell.Of(T($"GoalStatus.{g.Status}")),
                ReportCell.Money(g.TargetAmount),
                ReportCell.Money(allocated),
                ReportCell.Percent(MoneyMath.PercentOf(allocated, g.TargetAmount)),
                g.TargetDate is { } d ? ReportCell.Day(d) : ReportCell.Of("—"),
            ]);
            totalTarget = checked(totalTarget + g.TargetAmount);
            totalAllocated = checked(totalAllocated + allocated);
        }

        var inPeriod = allocations.Where(a => a.Date >= period.Start).ToList();
        var netContributions = MoneyMath.Sum(inPeriod.Select(a => a.Amount));
        var summary = new List<ReportSummaryItem>
        {
            new(T("Report.Goals.TotalAllocated"), ReportCell.Money(totalAllocated)),
            new(T("Report.Goals.NetContributions"), ReportCell.Money(netContributions)),
            new(T("Report.Goals.Count"), ReportCell.Number(goals.Count)),
        };
        var goalsTable = new ReportTable(
            T("Report.Goals.Table"),
            [new(T("Report.Column.Goal"), ReportCellKind.Text, 2.4), new(T("Report.Column.Status"), ReportCellKind.Text, 1.3), new(T("Report.Column.Target"), ReportCellKind.Money, 1.6), new(T("Report.Column.Allocated"), ReportCellKind.Money, 1.6), new(T("Report.Column.Progress"), ReportCellKind.Percent), new(T("Report.Column.TargetDate"), ReportCellKind.Date, 1.3)],
            goalRows,
            [ReportCell.Of(T("Report.Total")), ReportCell.Empty, ReportCell.Money(totalTarget), ReportCell.Money(totalAllocated), ReportCell.Empty, ReportCell.Empty],
            T("Report.Goals.Empty"));
        var historyTable = new ReportTable(
            T("Report.Goals.History"),
            [new(T("Report.Column.Date"), ReportCellKind.Date, 1.2), new(T("Report.Column.Goal"), ReportCellKind.Text, 2.2), new(T("Report.Column.Account"), ReportCellKind.Text, 2), new(T("Report.Column.Amount"), ReportCellKind.Money, 1.6), new(T("Report.Column.Note"), ReportCellKind.Text, 2.4)],
            inPeriod.Select(a => (IReadOnlyList<ReportCell>)
            [
                ReportCell.Day(a.Date),
                ReportCell.Of(goalNames.GetValueOrDefault(a.GoalId, "?")),
                ReportCell.Of(accounts.GetValueOrDefault(a.AccountId, "?")),
                ReportCell.Money(a.Amount),
                ReportCell.Of(a.Note),
            ]).ToList(),
            [ReportCell.Of(T("Report.Total")), ReportCell.Empty, ReportCell.Empty, ReportCell.Money(netContributions), ReportCell.Empty],
            T("Report.Goals.HistoryEmpty"));
        return (summary, [goalsTable, historyTable], [T("Report.Goals.Note")]);
    }

    /// <summary>A transfer fee is labelled as such, so it is never mistaken for a separate purchase.</summary>
    private string Describe(Transaction t) => t.ParentTransactionId is null
        ? t.Description
        : string.IsNullOrWhiteSpace(t.Description) ? T("Report.FeeOnly") : _loc.Format("Report.FeeDescription", t.Description);

    private string CategoryName(Category? category) =>
        category is null ? T("Report.Uncategorized") : DefaultCategories.DisplayName(category, _loc);

    private string MonthName(DateOnly month) =>
        month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", _loc.Culture);
}
