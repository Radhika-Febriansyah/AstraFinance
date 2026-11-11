using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 5: Dashboard figures, chart data, recent transactions and navigation.</summary>
public sealed partial class ShellTests
{
    [Fact]
    public async Task Dashboard_shows_real_totals_cash_flow_and_recent_transactions()
    {
        await StartUnlockedAsync();
        var (food, salary, cash, bank) = await SeedAsync();
        await AddAsync(TransactionType.Income, 8_000_000, bank, salary, "November salary", new DateOnly(2026, 11, 1));
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch", new DateOnly(2026, 11, 8));
        await AddAsync(TransactionType.Expense, 120_000, bank, food, "Groceries", new DateOnly(2026, 10, 20));
        await AddAsync(TransactionType.Transfer, 500_000, bank, null, destination: cash, fee: 2_500, date: new DateOnly(2026, 11, 9));

        await Shell.NavigateAsync(AppPage.Transactions);
        await Shell.NavigateAsync(AppPage.Dashboard);
        var dashboard = Assert.IsType<DashboardViewModel>(Shell.CurrentPage);

        // Cash 100.000 + BCA 5.000.000 + 8.000.000 − 45.000 − 120.000 − 2.500 fee
        Assert.Equal("Rp12.932.500", dashboard.TotalBalanceText);
        Assert.Equal("2 active accounts", dashboard.AccountsText);
        Assert.Equal("Rp8.000.000", dashboard.MonthIncomeText);
        Assert.Equal("Rp47.500", dashboard.MonthExpenseText);
        Assert.Equal("Net this month: +Rp7.952.500", dashboard.MonthNetText);
        Assert.Equal("November 2026", dashboard.MonthName);

        Assert.False(dashboard.HasNoCashFlow);
        Assert.Equal(6, dashboard.CashFlow.Count);
        Assert.Equal("Nov", dashboard.CashFlow[^1].Label);
        Assert.Equal((8_000_000L, 47_500L), (dashboard.CashFlow[^1].Income, dashboard.CashFlow[^1].Expense));
        Assert.Equal(120_000, dashboard.CashFlow[^2].Expense);
        Assert.Equal("Rp8M", dashboard.FormatCompact(8_000_000)); // English suffixes in this host

        Assert.Equal(5, dashboard.Recent.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), dashboard.Recent[0].Transaction.Date);
        Assert.Equal("Groceries", dashboard.Recent[^1].Title); // newest first, oldest last
    }

    [Fact]
    public async Task Calendar_shows_real_day_totals_and_opens_transactions()
    {
        await StartUnlockedAsync();
        var (food, salary, cash, bank) = await SeedAsync();
        await AddAsync(TransactionType.Income, 8_000_000, bank, salary, "November salary", new DateOnly(2026, 11, 1));
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch", new DateOnly(2026, 11, 9));
        await AddAsync(TransactionType.Transfer, 500_000, bank, null, destination: cash, fee: 2_500, date: new DateOnly(2026, 11, 9));
        await AddAsync(TransactionType.Expense, 120_000, bank, food, "Groceries", new DateOnly(2026, 10, 20));

        await Shell.NavigateAsync(AppPage.Calendar);
        var calendar = Assert.IsType<CalendarViewModel>(Shell.CurrentPage);

        // Month totals: income 8.000.000; expenses 45.000 + 2.500 fee; the transfer itself is neither.
        Assert.Equal("Rp8.000.000", calendar.MonthIncomeText);
        Assert.Equal("Rp47.500", calendar.MonthExpenseText);
        Assert.Equal(42, calendar.Days.Count);

        // Today (9 Nov, from the test clock) is selected and highlighted.
        var today = calendar.Days.Single(d => d.IsToday);
        Assert.Equal(new DateOnly(2026, 11, 9), today.Date);
        Assert.True(today.IsSelected);
        Assert.True(today.HasExpense);
        Assert.False(today.HasIncome);
        Assert.Equal("Rp47.500", calendar.SelectedExpenseText);
        Assert.Equal(3, calendar.SelectedRows.Count); // lunch, transfer, fee

        // Selecting another day shows that day only.
        await calendar.SelectDayCommand.ExecuteAsync(calendar.Days.Single(d => d.Date == new DateOnly(2026, 11, 1)));
        Assert.Equal("Rp8.000.000", calendar.SelectedIncomeText);
        Assert.Single(calendar.SelectedRows);

        // Previous month has October's expense.
        await calendar.PreviousMonthCommand.ExecuteAsync(null);
        Assert.Equal("Rp120.000", calendar.MonthExpenseText);
        Assert.False(calendar.IsCurrentMonth);

        // A row opens the transaction on the Transactions page.
        await calendar.TodayCommand.ExecuteAsync(null);
        await calendar.OpenTransactionCommand.ExecuteAsync(calendar.SelectedRows[0]);
        Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
    }

    [Fact]
    public async Task Dashboard_without_activity_shows_empty_states_not_fake_data()
    {
        await StartUnlockedAsync();
        var dashboard = Assert.IsType<DashboardViewModel>(Shell.CurrentPage);
        await dashboard.LoadAsync();

        Assert.True(dashboard.HasNoCashFlow);
        Assert.True(dashboard.HasNoTransactions);
        Assert.Empty(dashboard.Recent);
        Assert.Equal("Rp100.000", dashboard.TotalBalanceText); // the opening balance is real data
        Assert.Equal("Rp0", dashboard.MonthIncomeText);
    }

    [Fact]
    public async Task Dashboard_cards_navigate_and_refresh_after_quick_add()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        var lunch = await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch");
        await Shell.NavigateAsync(AppPage.Accounts);
        await Shell.NavigateAsync(AppPage.Dashboard);
        var dashboard = Assert.IsType<DashboardViewModel>(Shell.CurrentPage);

        // Quick Add from the dashboard refreshes it.
        var dialogs = Get<DialogService>();
        var pending = dashboard.QuickAddCommand.ExecuteAsync(null);
        var form = Assert.IsType<TransactionFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.True(form.IsQuick);
        form.AmountText = "15.000";
        form.SelectedCategory = form.Categories.Single(c => c.Id == food);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await pending;
        await Settle();
        Assert.Equal("Rp60.000", dashboard.MonthExpenseText);
        Assert.Equal(2, dashboard.Recent.Count);

        await dashboard.OpenTransactionCommand.ExecuteAsync(dashboard.Recent.Single(r => r.Id == lunch.Transaction.Id));
        Assert.Equal(lunch.Transaction.Id, Assert.IsType<TransactionsViewModel>(Shell.CurrentPage).Selected!.Id);

        await Shell.NavigateAsync(AppPage.Dashboard);
        await ((DashboardViewModel)Shell.CurrentPage!).OpenAccountsCommand.ExecuteAsync(null);
        Assert.IsType<AccountsViewModel>(Shell.CurrentPage);

        await Shell.NavigateAsync(AppPage.Dashboard);
        await ((DashboardViewModel)Shell.CurrentPage!).OpenAnalyticsCommand.ExecuteAsync(null);
        Assert.Equal(AppPage.Analytics, Shell.CurrentPage!.Page);
    }
}
