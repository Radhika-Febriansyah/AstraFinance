using AstraFinance.Core.Entities;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Rules;
using ClosedXML.Excel;
using PdfSharp.Pdf.IO;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>Phase 8: analytics aggregates, report contents and the three export formats.</summary>
public class AnalyticsAndReportsTests
{
    private static readonly DateRange November = DateRange.Month(2026, 11);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>
    /// Bank opens with 5,000,000 and Cash with 100,000. October: salary 1,000,000. November: salary 8,000,000 (Mon 2),
    /// lunch 45,000 cash (Tue 3), +5,000 cash adjustment (Thu 5), shoes 300,000 bank (Sat 7), transfer 500,000
    /// bank→cash with a 6,500 fee (Sun 8), a deleted 20,000 expense (Mon 9). December: 99,000 expense.
    /// </summary>
    private static async Task<(FinanceTestContext Ctx, Account Bank, Account Cash)> SeedAsync()
    {
        var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("BCA", 5_000_000);
        var cash = await ctx.NewAccountAsync("Cash", 100_000);
        await ctx.IncomeAsync(bank, 1_000_000, D(10, 15), "October salary");
        await ctx.IncomeAsync(bank, 8_000_000, D(11, 2), "November salary");
        await ctx.ExpenseAsync(cash, 45_000, "Food", D(11, 3), "Lunch");
        await ctx.Accounts.AdjustBalanceAsync(cash.Id, 60_000, "Counted cash", D(11, 5));
        await ctx.ExpenseAsync(bank, 300_000, "Shopping", D(11, 7), "Shoes");
        await ctx.TransferAsync(bank, cash, 500_000, fee: 6_500, date: D(11, 8));
        var deleted = await ctx.ExpenseAsync(bank, 20_000, "Food", D(11, 9), "Mistake");
        await ctx.Transactions.DeleteAsync(deleted.Id);
        await ctx.ExpenseAsync(bank, 99_000, "Food", D(12, 1), "December dinner");
        return (ctx, bank, cash);
    }

    // ======================= Analytics =======================

    [Fact]
    public void Periods_resolve_weeks_from_monday_and_choose_buckets()
    {
        Assert.Equal(new DateRange(D(11, 9), D(11, 15)), AnalyticsPeriods.Resolve(AnalyticsPeriod.Week, D(11, 11)));
        Assert.Equal(new DateRange(D(11, 9), D(11, 15)), AnalyticsPeriods.WeekOf(D(11, 15))); // Sunday belongs to the same week
        Assert.Equal(November, AnalyticsPeriods.Resolve(AnalyticsPeriod.Month, D(11, 20)));
        Assert.Equal(new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), AnalyticsPeriods.Resolve(AnalyticsPeriod.Year, D(6, 1)));
        Assert.Equal(D(10, 30), AnalyticsPeriods.Shift(AnalyticsPeriod.Month, D(11, 30), -1));
        Assert.Equal(D(2, 28), AnalyticsPeriods.Shift(AnalyticsPeriod.Month, D(3, 31), -1));
        Assert.Equal(AnalyticsBucket.Day, AnalyticsPeriods.BucketFor(November));
        Assert.Equal(AnalyticsBucket.Month, AnalyticsPeriods.BucketFor(new DateRange(D(1, 1), D(12, 31))));
    }

    [Fact]
    public async Task Snapshot_counts_only_income_and_expenses_with_fees_and_without_deleted_or_transfers()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;

        var s = await ctx.Analytics.GetSnapshotAsync(November);

        Assert.Equal((8_000_000L, 351_500L, 1, 3), (s.Income, s.Expense, s.IncomeCount, s.ExpenseCount));
        Assert.Equal(8_000_000 - 351_500, s.Net);
        Assert.Equal(351_500 / 30, s.AverageDailyExpense);
        Assert.Equal(AnalyticsBucket.Day, s.Bucket);
        Assert.Equal(30, s.Series.Count);
        Assert.Equal(8_000_000, s.Series.Single(b => b.Start == D(11, 2)).Income);
        Assert.Equal(6_500, s.Series.Single(b => b.Start == D(11, 8)).Expense); // the fee; the transfer itself never counts
        Assert.Equal(0, s.Series.Single(b => b.Start == D(11, 9)).Expense);    // deleted

        Assert.Equal(["Shopping", "Food", "Fees"], s.ExpenseByCategory.Select(c => c.Category!.SystemKey));
        Assert.Equal("Salary", Assert.Single(s.IncomeByCategory).Category!.SystemKey);
        Assert.Equal([0L, 45_000, 0, 0, 0, 300_000, 6_500], s.ExpenseByWeekday);
        Assert.Equal("Shoes", s.LargestExpense!.Description);
    }

    [Fact]
    public async Task Yearly_snapshot_uses_monthly_buckets_and_an_empty_period_has_no_data()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;

        var year = await ctx.Analytics.GetSnapshotAsync(AnalyticsPeriods.Resolve(AnalyticsPeriod.Year, D(1, 1)));
        Assert.Equal(12, year.Series.Count);
        Assert.Equal(1_000_000, year.Series[9].Income);
        Assert.Equal(351_500 + 99_000, year.Expense);

        var empty = await ctx.Analytics.GetSnapshotAsync(DateRange.Month(2026, 3));
        Assert.False(empty.HasData);
        Assert.Null(empty.LargestExpense);
        Assert.All(empty.Series, b => Assert.Equal((0L, 0L), (b.Income, b.Expense)));
    }

    // ======================= Reports =======================

    [Fact]
    public async Task Income_and_expense_reports_list_every_record_with_totals()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;

        var income = await ctx.Reports.BuildAsync(ReportKind.Income, November);
        Assert.Equal("Income Report", income.Title);
        Assert.Equal("01/11/2026 – 30/11/2026", income.PeriodText);
        Assert.Equal(8_000_000, income.Summary[0].Value.Value);
        var incomeRow = Assert.Single(income.Tables[1].Rows);
        Assert.Equal("November salary", incomeRow[1].Text);
        Assert.Equal(100, Assert.Single(income.Tables[0].Rows)[3].Value); // share

        var expense = await ctx.Reports.BuildAsync(ReportKind.Expense, November);
        var rows = expense.Tables[1].Rows;
        Assert.Equal(["Lunch", "Shoes", "Transfer fee"], rows.Select(r => r[1].Text));
        Assert.Equal(351_500, expense.Tables[1].Total![4].Value);
        Assert.Equal(["Shopping", "Food & Drinks", "Fees & Charges"], expense.Tables[0].Rows.Select(r => r[0].Text));
        Assert.Equal(6, expense.RowCount); // 3 category rows + 3 transactions, nothing truncated
    }

    [Fact]
    public async Task Cash_flow_report_reconciles_opening_and_closing_balances()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;

        var report = await ctx.Reports.BuildAsync(ReportKind.CashFlow, November);
        var values = report.Summary.Select(s => s.Value.Value).ToArray();
        var (opening, income, expense, net, adjusted, closing) = (values[0], values[1], values[2], values[3], values[4], values[5]);

        Assert.Equal(6_100_000, opening);           // opening balances + October salary
        Assert.Equal((8_000_000L, 351_500L, 5_000L), (income, expense, adjusted));
        Assert.Equal(income - expense, net);
        Assert.Equal(opening + income - expense + adjusted, closing);
        Assert.Equal(13_753_500, closing);
        Assert.Equal(30, report.Tables[0].Rows.Count);
        Assert.Equal(closing, report.Tables[0].Rows[^1][5].Value); // running balance ends at the closing balance

        // Through the end of all activity the closing balance equals the total of all account balances.
        var all = await ctx.Reports.BuildAsync(ReportKind.CashFlow, new DateRange(D(1, 1), D(12, 31)));
        Assert.Equal(await ctx.Accounts.GetTotalBalanceAsync(), all.Summary[5].Value.Value);
        Assert.Equal(12, all.Tables[0].Rows.Count); // monthly rows for a year
    }

    [Fact]
    public async Task Account_balance_report_matches_account_balances()
    {
        var (ctx, bank, cash) = await SeedAsync();
        await using var _ = ctx;

        var report = await ctx.Reports.BuildAsync(ReportKind.AccountBalance, November);
        var table = report.Tables[0];
        var bankRow = table.Rows.Single(r => r[0].Text == "BCA");
        var cashRow = table.Rows.Single(r => r[0].Text == "Cash");

        Assert.Equal([6_000_000L, 8_000_000, 306_500, 0, 500_000, 0, 13_193_500], bankRow.Skip(2).Select(c => c.Value));
        Assert.Equal([100_000L, 0, 45_000, 500_000, 0, 5_000, 560_000], cashRow.Skip(2).Select(c => c.Value));
        Assert.Equal(table.Total![5].Value, table.Total[6].Value); // transfers in = transfers out across accounts
        Assert.Equal(13_753_500, table.Total[8].Value);

        // A period that includes everything closes at each account's current balance.
        var all = await ctx.Reports.BuildAsync(ReportKind.AccountBalance, new DateRange(D(1, 1), D(12, 31)));
        Assert.Equal(await ctx.BalanceAsync(bank), all.Tables[0].Rows.Single(r => r[0].Text == "BCA")[8].Value);
        Assert.Equal(await ctx.BalanceAsync(cash), all.Tables[0].Rows.Single(r => r[0].Text == "Cash")[8].Value);
    }

    [Fact]
    public async Task Budget_report_has_a_row_per_budget_and_month()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;
        var food = await ctx.CategoryAsync("Food");
        var shopping = await ctx.CategoryAsync("Shopping");
        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 100_000, D(10, 1), null, null));
        await ctx.Budgets.CreateAsync(new BudgetDraft(shopping.Id, 200_000, D(11, 1), null, null));

        var report = await ctx.Reports.BuildAsync(ReportKind.Budget, new DateRange(D(10, 1), D(11, 30)));
        var rows = report.Tables[0].Rows;
        Assert.Equal(3, rows.Count);
        Assert.Equal(("October 2026", "Food & Drinks", 0L), (rows[0][0].Text, rows[0][1].Text, rows[0][3].Value));
        Assert.Equal(("November 2026", "Food & Drinks", 45_000L), (rows[1][0].Text, rows[1][1].Text, rows[1][3].Value));
        Assert.Equal(("Shopping", 300_000L, "Exceeded"), (rows[2][1].Text, rows[2][3].Value, rows[2][6].Text));
        Assert.Equal((400_000L, 345_000L, 1L), (report.Summary[0].Value.Value, report.Summary[1].Value.Value, report.Summary[3].Value.Value));
    }

    [Fact]
    public async Task Savings_goals_report_uses_allocations_up_to_the_period_end()
    {
        var (ctx, bank, _) = await SeedAsync();
        await using var _ = ctx;
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Laptop Fund", 8_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 1_000_000, D(10, 20), null);
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 2_000_000, D(11, 4), "Bonus");
        await ctx.Goals.ReleaseAsync(goal.Id, bank.Id, 500_000, D(11, 6), null);
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 1_000_000, D(12, 2), null); // after the period

        var report = await ctx.Reports.BuildAsync(ReportKind.SavingsGoals, November);
        var goalRow = Assert.Single(report.Tables[0].Rows);
        Assert.Equal((2_500_000L, 31L), (goalRow[3].Value, goalRow[4].Value));
        Assert.Equal([2_000_000L, -500_000], report.Tables[1].Rows.Select(r => r[3].Value));
        Assert.Equal(1_500_000, report.Summary[1].Value.Value);
    }

    // ======================= Exports =======================

    [Fact]
    public async Task Csv_export_contains_every_row_with_plain_amounts_and_formula_protection()
    {
        var (ctx, bank, _) = await SeedAsync();
        await using var _ = ctx;
        await ctx.ExpenseAsync(bank, 1_234, "Food", D(11, 20), "=HYPERLINK(\"x\")");
        var report = await ctx.Reports.BuildAsync(ReportKind.Expense, November);
        var path = Path.Combine(ctx.Paths.DataDirectory, "expenses.csv");

        await ctx.Exporter.ExportAsync(report, ExportFormat.Csv, path);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // UTF-8 BOM so Excel reads the encoding
        var text = await File.ReadAllTextAsync(path);
        Assert.StartsWith("Expense Report,01/11/2026 – 30/11/2026", text, StringComparison.Ordinal);
        Assert.Contains("03/11/2026,Lunch,Food & Drinks,Cash,45000", text, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text, StringComparison.Ordinal);
        Assert.Contains("Total,,,,352734", text, StringComparison.Ordinal);
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task Excel_export_has_a_summary_sheet_and_typed_cells()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;
        var report = await ctx.Reports.BuildAsync(ReportKind.Expense, November);
        var path = Path.Combine(ctx.Paths.DataDirectory, "expenses.xlsx");

        await ctx.Exporter.ExportAsync(report, ExportFormat.Excel, path);

        using var workbook = new XLWorkbook(path);
        Assert.Equal(["Expense Report", "By category", "Transactions"], workbook.Worksheets.Select(w => w.Name));
        var summary = workbook.Worksheet(1);
        Assert.Equal(351_500m, summary.Cell(5, 2).GetValue<decimal>());
        var sheet = workbook.Worksheet("Transactions");
        Assert.Equal(new DateTime(2026, 11, 3), sheet.Cell(2, 1).GetDateTime());
        Assert.Equal("dd/MM/yyyy", sheet.Cell(2, 1).Style.DateFormat.Format);
        Assert.Equal(45_000m, sheet.Cell(2, 5).GetValue<decimal>());
        Assert.StartsWith("\"Rp\"#,##0", sheet.Cell(2, 5).Style.NumberFormat.Format, StringComparison.Ordinal);
        Assert.Equal(351_500m, sheet.Cell(5, 5).GetValue<decimal>()); // total row after 3 records
        Assert.True(sheet.Cell(5, 5).Style.Font.Bold);
    }

    [Fact]
    public async Task Pdf_export_renders_all_pages_and_uses_landscape_for_wide_tables()
    {
        var (ctx, bank, _) = await SeedAsync();
        await using var _ = ctx;
        for (var i = 0; i < 120; i++)
        {
            await ctx.ExpenseAsync(bank, 1_000 + i, "Food", D(11, 1 + (i % 28)), $"Snack {i}");
        }

        var expenses = await ctx.Reports.BuildAsync(ReportKind.Expense, November);
        var path = Path.Combine(ctx.Paths.DataDirectory, "expenses.pdf");
        await ctx.Exporter.ExportAsync(expenses, ExportFormat.Pdf, path);
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Assert.True(pdf.PageCount >= 3, $"expected several pages, got {pdf.PageCount}");
            Assert.False(IsLandscape(pdf.Pages[0])); // portrait
        }

        var accounts = await ctx.Reports.BuildAsync(ReportKind.AccountBalance, November);
        var wide = Path.Combine(ctx.Paths.DataDirectory, "accounts.pdf");
        await ctx.Exporter.ExportAsync(accounts, ExportFormat.Pdf, wide);
        using (var pdf = PdfReader.Open(wide, PdfDocumentOpenMode.Import))
        {
            Assert.True(IsLandscape(pdf.Pages[0]), $"box {pdf.Pages[0].MediaBox} rotate {pdf.Pages[0].Rotate} orientation {pdf.Pages[0].Orientation}"); // 9 columns → landscape
        }
    }

    /// <summary>As displayed: a portrait media box rotated by 90° is a landscape page.</summary>
    private static bool IsLandscape(PdfSharp.Pdf.PdfPage page) =>
        (page.MediaBox.Width > page.MediaBox.Height) != (page.Rotate % 180 != 0);

    [Fact]
    public async Task Failed_export_leaves_no_file_behind()
    {
        var (ctx, _, _) = await SeedAsync();
        await using var _ = ctx;
        var report = await ctx.Reports.BuildAsync(ReportKind.Income, November);
        var path = Path.Combine(ctx.Paths.DataDirectory, "missing-folder", "income.xlsx");

        await Assert.ThrowsAnyAsync<IOException>(() => ctx.Exporter.ExportAsync(report, ExportFormat.Excel, path));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".partial"));
    }
}
