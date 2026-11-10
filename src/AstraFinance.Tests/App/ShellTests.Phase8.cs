using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 8: Analytics page (periods, figures, empty data, drill-down) and Reports page (preview, export).</summary>
public sealed partial class ShellTests
{
    [Fact]
    public async Task Analytics_page_shows_real_figures_and_moves_between_periods()
    {
        await StartUnlockedAsync();
        var (food, salary, cash, bank) = await SeedAsync();
        await AddAsync(TransactionType.Income, 8_000_000, bank, salary, "November salary", new DateOnly(2026, 11, 2));
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch", new DateOnly(2026, 11, 3));
        await AddAsync(TransactionType.Transfer, 500_000, bank, null, destination: cash, fee: 2_500, date: new DateOnly(2026, 11, 4));
        await AddAsync(TransactionType.Expense, 120_000, bank, food, "Groceries", new DateOnly(2026, 10, 20));

        await Shell.NavigateAsync(AppPage.Analytics);
        var page = Assert.IsType<AnalyticsViewModel>(Shell.CurrentPage);
        Assert.Equal("November 2026", page.PeriodText);
        Assert.True(page.HasData);
        Assert.Equal("Rp8.000.000", page.IncomeText);
        Assert.Equal("Rp47.500", page.ExpenseText);          // lunch + transfer fee; the transfer itself never counts
        Assert.Equal("+Rp7.952.500", page.NetText);
        Assert.Equal("99% of income kept", page.SavingsRateText);
        Assert.Equal(30, page.Series.Count);
        Assert.Equal(12, page.Trend.Count);
        Assert.Equal(["Food & Drinks", "Fees & Charges"], page.ExpenseBreakdown.Select(s => s.Name));
        Assert.Equal("Salary", Assert.Single(page.IncomeSources).Name);
        Assert.StartsWith("Rp45.000: Lunch", page.LargestExpenseText, StringComparison.Ordinal);
        Assert.False(page.NextCommand.CanExecute(null)); // no future periods

        await page.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("October 2026", page.PeriodText);
        Assert.Equal("Rp120.000", page.ExpenseText);
        Assert.False(page.HasIncome);
        Assert.True(page.NextCommand.CanExecute(null));

        page.PeriodOptions.Single(o => o.Value == AnalyticsPeriod.Week).IsSelected = true;
        await page.LoadAsync();
        Assert.Equal(AnalyticsPeriod.Week, page.Period);
        Assert.Equal("05/10/2026 – 11/10/2026", page.PeriodText); // the week of the anchor (9 Oct)

        await page.PreviousCommand.ExecuteAsync(null);
        Assert.True(page.IsEmpty); // nothing that week

        // Custom range validation.
        page.PeriodOptions.Single(o => o.Value == AnalyticsPeriod.Custom).IsSelected = true;
        await page.LoadAsync();
        page.CustomTo = page.CustomFrom!.Value.AddDays(-1);
        await page.LoadAsync();
        Assert.Equal("The end date must not be before the start date.", page.CustomRangeError);
        page.CustomFrom = new DateOnly(2026, 10, 1);
        page.CustomTo = new DateOnly(2026, 11, 30);
        await page.LoadAsync();
        Assert.Null(page.CustomRangeError);
        Assert.Equal("Rp167.500", page.ExpenseText);

        // Clicking a category shows its transactions.
        await page.OpenCategoryCommand.ExecuteAsync(page.ExpenseBreakdown.First(s => s.Name == "Food & Drinks"));
        var transactions = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        Assert.Equal(food, transactions.SelectedCategoryFilter!.Value);
    }

    [Fact]
    public async Task Reports_page_previews_and_exports_the_selected_report()
    {
        await StartUnlockedAsync();
        var (food, _, cash, bank) = await SeedAsync();
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch", new DateOnly(2026, 11, 3));
        await AddAsync(TransactionType.Expense, 30_000, bank, food, "Coffee", new DateOnly(2026, 11, 5));

        await Shell.NavigateAsync(AppPage.Reports);
        var page = Assert.IsType<ReportsViewModel>(Shell.CurrentPage);
        Assert.Equal(ReportKind.Income, page.Kind);
        Assert.Equal(ReportPeriodPreset.ThisMonth, page.Preset);

        page.ReportOptions.Single(o => o.Value == ReportKind.Expense).IsSelected = true;
        await page.RefreshPreviewAsync();
        Assert.Equal("Expense Report", page.PreviewTitle);
        Assert.Equal("01/11/2026 – 30/11/2026", page.PreviewPeriod);
        Assert.Equal(("Total expenses", "Rp75.000"), (page.PreviewSummary[0].Label, page.PreviewSummary[0].Value));
        Assert.Equal(["1 record", "2 records"], page.PreviewTables.Select(t => t.Value)); // 1 category, 2 transactions
        Assert.Equal("The export contains all 3 records shown above; nothing is left out.", page.PreviewCountText);

        // Last month has nothing.
        page.PeriodOptions.Single(o => o.Value == ReportPeriodPreset.LastMonth).IsSelected = true;
        await page.RefreshPreviewAsync();
        Assert.Equal("Rp0", page.PreviewSummary[0].Value);

        // Export as Excel to the picked path.
        page.PeriodOptions.Single(o => o.Value == ReportPeriodPreset.ThisMonth).IsSelected = true;
        page.FormatOptions.Single(o => o.Value == ExportFormat.Excel).IsSelected = true;
        await page.RefreshPreviewAsync();
        var path = Path.Combine(_dir.Path, "expenses.xlsx");
        SavePicker.NextResult = path;
        await page.ExportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(path));
        Assert.Equal(path, page.LastExportPath);
        Assert.Equal("Expense Report exported", Get<INotificationService>().History[0].Title);

        // Cancelling the file dialog writes nothing.
        SavePicker.NextResult = null;
        var before = Get<INotificationService>().History.Count;
        await page.ExportCommand.ExecuteAsync(null);
        Assert.Equal(before, Get<INotificationService>().History.Count);

        // An incomplete custom range blocks the export with a message.
        page.PeriodOptions.Single(o => o.Value == ReportPeriodPreset.Custom).IsSelected = true;
        await page.RefreshPreviewAsync();
        page.CustomFrom = null;
        await page.RefreshPreviewAsync();
        Assert.Equal("Choose a start and an end date.", page.PeriodError);
    }
}
