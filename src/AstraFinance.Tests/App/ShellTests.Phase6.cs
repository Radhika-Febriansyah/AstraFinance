using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 6: Budgets page, Savings Goals page, dashboard budget summary and search.</summary>
public sealed partial class ShellTests
{
    [Fact]
    public async Task Budgets_page_creates_and_evaluates_budgets()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        await AddAsync(TransactionType.Expense, 90_000, cash, food, "Groceries");

        await Shell.NavigateAsync(AppPage.Budgets);
        var page = Assert.IsType<BudgetsViewModel>(Shell.CurrentPage);
        Assert.True(page.IsEmpty);
        Assert.Equal("November 2026", page.MonthText);

        var dialogs = Get<DialogService>();
        var pending = page.AddCommand.ExecuteAsync(null);
        var form = Assert.IsType<BudgetFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.All(form.Categories, c => Assert.Equal(CategoryKind.Expense, c.Kind));
        form.SelectedCategory = form.Categories.Single(c => c.Id == food);
        form.AmountText = "100.000";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await pending;
        await Settle();

        var row = Assert.Single(page.Items);
        Assert.Equal("Food & Drinks", row.Name);
        Assert.Equal(BudgetStatus.Warning, row.Status);
        Assert.Equal("Rp10.000 left", row.RemainingText);
        Assert.Equal("90%", row.PercentText);
        Assert.Equal("Rp90.000", page.TotalSpentText);

        // Another expense pushes it over; the page refreshes from the change notification.
        await Get<ITransactionService>().CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Expense, Amount = 25_000, AccountId = cash, CategoryId = food, Date = new DateOnly(2026, 11, 9),
        });
        Get<DataChangeNotifier>().Notify(DataChangeKind.Transactions);
        await Settle();
        Assert.Equal(BudgetStatus.Exceeded, page.Items.Single().Status);
        Assert.Equal("Rp15.000 over budget", page.Items.Single().RemainingText);
        Assert.True(page.IsTotalOver);

        // Previous month has no spending.
        await page.PreviousMonthCommand.ExecuteAsync(null);
        Assert.Equal("Rp0", page.TotalSpentText);
        Assert.False(page.IsCurrentMonth);
    }

    [Fact]
    public async Task Budget_form_rejects_duplicates_and_bad_threshold_and_delete_confirms()
    {
        await StartUnlockedAsync();
        var (food, _, _, _) = await SeedAsync();
        await Get<IBudgetService>().CreateAsync(new BudgetDraft(food, 100_000, new DateOnly(2026, 11, 1), null, null));

        var form = Get<Func<BudgetFormViewModel>>()();
        await form.InitializeAsync(null, new DateOnly(2026, 11, 1));
        Assert.DoesNotContain(form.Categories, c => c.Id == food); // already budgeted
        form.SelectedCategory = form.Categories[0];
        form.AmountText = "50.000";
        form.UseCustomThreshold = true;
        form.ThresholdText = "150";
        Assert.False(await form.SubmitAsync());
        Assert.Equal("Enter a percentage from 1 to 100.", form.GetError(nameof(BudgetFormViewModel.ThresholdText)));

        await Shell.NavigateAsync(AppPage.Budgets);
        var page = Assert.IsType<BudgetsViewModel>(Shell.CurrentPage);
        var dialogs = Get<DialogService>();
        var deleting = page.DeleteCommand.ExecuteAsync(page.Items.Single());
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await deleting;
        await Settle();
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task Goals_page_creates_allocates_and_archives_without_touching_balances()
    {
        await StartUnlockedAsync();
        var (_, _, _, bank) = await SeedAsync();
        await Shell.NavigateAsync(AppPage.SavingsGoals);
        var page = Assert.IsType<SavingsGoalsViewModel>(Shell.CurrentPage);
        Assert.True(page.IsEmpty);
        var dialogs = Get<DialogService>();

        var creating = page.AddCommand.ExecuteAsync(null);
        var goalForm = Assert.IsType<GoalFormViewModel>(dialogs.ActiveDialog!.Form);
        goalForm.Name = "Laptop Fund";
        goalForm.TargetText = "8.000.000";
        goalForm.TargetDate = new DateOnly(2027, 3, 31);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await creating;
        Assert.Equal("Laptop Fund", page.Selected!.Name);
        Assert.Equal("Allocate about Rp2.000.000 per month to reach it on time", page.Selected.PaceText);

        var allocating = page.AllocateCommand.ExecuteAsync(null);
        var allocation = Assert.IsType<AllocationFormViewModel>(dialogs.ActiveDialog!.Form);
        allocation.SelectedAccount = allocation.Accounts.Single(a => a.Id == bank);
        Assert.Equal("Available to allocate: Rp5.000.000", allocation.LimitText);
        allocation.AmountText = "6.000.000";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        Assert.NotNull(dialogs.ActiveDialog); // exceeds available
        Assert.Equal("The allocation exceeds the account's unallocated balance.", allocation.GetError(nameof(AllocationFormViewModel.AmountText)));
        allocation.AmountText = "4.000.000";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await allocating;

        Assert.Equal("Rp4.000.000", page.Selected!.AllocatedText);
        Assert.Equal("50%", page.Selected.PercentText);
        var entry = Assert.Single(page.History);
        Assert.Equal(entry.DateText, entry.Caption); // no note → no dangling separator
        Assert.Equal(5_000_000, await Get<IAccountService>().GetBalanceAsync(bank)); // unchanged

        var archiving = page.ArchiveCommand.ExecuteAsync(null);
        Assert.Contains("Rp4.000.000", dialogs.ActiveDialog!.Message, StringComparison.Ordinal);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await archiving;
        Assert.True(page.IsEmpty);
        page.ShowArchived = true;
        await Settle();
        Assert.True(Assert.Single(page.Goals).IsArchived);
    }

    [Fact]
    public async Task Dashboard_budget_summary_and_search_for_budgets_and_goals()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        await Get<IBudgetService>().CreateAsync(new BudgetDraft(food, 50_000, new DateOnly(2026, 11, 1), null, null));
        await AddAsync(TransactionType.Expense, 60_000, cash, food);
        var goal = await Get<ISavingsGoalService>().CreateAsync(new SavingsGoalDraft("Holiday Bali", 10_000_000, null, "Icon.SavingsGoals", "#F4806B"));

        await Shell.NavigateAsync(AppPage.Transactions);
        await Shell.NavigateAsync(AppPage.Dashboard);
        var dashboard = Assert.IsType<DashboardViewModel>(Shell.CurrentPage);
        Assert.False(dashboard.HasNoBudgets);
        Assert.Equal(BudgetStatus.Exceeded, Assert.Single(dashboard.Budgets).Status);

        var search = Get<IGlobalSearchService>();
        var budgetHit = (await search.SearchAsync("food", CancellationToken.None)).Single(g => g.Category == SearchCategory.Budgets).Results[0];
        Assert.Equal(AppPage.Budgets, budgetHit.Target.Page);
        var goalHit = (await search.SearchAsync("bali", CancellationToken.None)).Single(g => g.Category == SearchCategory.SavingsGoals).Results[0];
        await Get<INavigationService>().NavigateAsync(goalHit.Target);
        Assert.Equal(goal.Id, Assert.IsType<SavingsGoalsViewModel>(Shell.CurrentPage).Selected!.Id);

        await Shell.NavigateAsync(AppPage.Dashboard);
        await ((DashboardViewModel)Shell.CurrentPage!).OpenBudgetsCommand.ExecuteAsync(null);
        Assert.IsType<BudgetsViewModel>(Shell.CurrentPage);
    }
}
