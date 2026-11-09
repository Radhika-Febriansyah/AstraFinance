using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Tests.Infrastructure;

public class BudgetsAndGoalsTests
{
    private static readonly DateOnly November = new(2026, 11, 9);

    // ======================= Budgets =======================

    [Fact]
    public async Task Budget_counts_only_expenses_of_its_category_in_the_month()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 10_000_000);
        var cash = await ctx.NewAccountAsync("Cash");
        var food = await ctx.CategoryAsync("Food");
        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 1_000_000, November, null, null));

        await ctx.ExpenseAsync(bank, 300_000, "Food", new DateOnly(2026, 11, 2));
        await ctx.ExpenseAsync(cash, 550_000, "Food", new DateOnly(2026, 11, 30));
        await ctx.ExpenseAsync(bank, 999_000, "Food", new DateOnly(2026, 10, 31));   // other month
        await ctx.ExpenseAsync(bank, 50_000, "Transportation");                      // other category
        await ctx.IncomeAsync(bank, 5_000_000);                                      // income never counts
        await ctx.TransferAsync(bank, cash, 1_000_000);                              // transfers never count
        var deleted = await ctx.ExpenseAsync(bank, 77_000, "Food");
        await ctx.Transactions.DeleteAsync(deleted.Id);                              // deleted never counts

        var overview = await ctx.Budgets.GetOverviewAsync(November);

        var item = Assert.Single(overview.Items);
        Assert.Equal(850_000, item.Evaluation.Spent);
        Assert.Equal(150_000, item.Evaluation.Remaining);
        Assert.Equal(BudgetStatus.Warning, item.Evaluation.Status); // 85 % ≥ default 80 %
        Assert.Equal((1_000_000L, 850_000L), (overview.TotalLimit, overview.TotalSpent));
    }

    [Fact]
    public async Task Budget_thresholds_custom_and_default_and_exceeded_ordering()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 10_000_000);
        var food = await ctx.CategoryAsync("Food");
        var transport = await ctx.CategoryAsync("Transportation");
        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 100_000, November, 50, null));   // custom 50 %
        await ctx.Budgets.CreateAsync(new BudgetDraft(transport.Id, 100_000, November, null, null));
        await ctx.ExpenseAsync(bank, 60_000, "Food");
        await ctx.ExpenseAsync(bank, 120_000, "Transportation");

        var items = (await ctx.Budgets.GetOverviewAsync(November)).Items;
        Assert.Equal(BudgetStatus.Exceeded, items[0].Evaluation.Status); // most at risk first
        Assert.Equal(transport.Id, items[0].Category.Id);
        Assert.Equal(BudgetStatus.Warning, items[1].Evaluation.Status);  // 60 % ≥ custom 50 %

        await ctx.Budgets.SetDefaultWarningThresholdAsync(95);
        Assert.Equal(95, await ctx.Budgets.GetDefaultWarningThresholdAsync());
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Budgets.SetDefaultWarningThresholdAsync(0));
    }

    [Fact]
    public async Task Budget_applies_from_its_start_month_onward()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var food = await ctx.CategoryAsync("Food");
        var budget = await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 100_000, new DateOnly(2026, 12, 15), null, null));

        Assert.Equal(new DateOnly(2026, 12, 1), budget.StartDate);
        Assert.Empty((await ctx.Budgets.GetOverviewAsync(November)).Items);
        Assert.Single((await ctx.Budgets.GetOverviewAsync(new DateOnly(2027, 3, 1))).Items);
    }

    [Fact]
    public async Task Budget_validation_and_one_budget_per_category()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var food = await ctx.CategoryAsync("Food");
        var salary = await ctx.CategoryAsync("Salary");

        var income = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Budgets.CreateAsync(new BudgetDraft(salary.Id, 100, November, null, null)));
        Assert.True(income.Has(ValidationCodes.CategoryKindMismatch));

        var invalid = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 0, November, 150, null)));
        Assert.True(invalid.Has(ValidationCodes.AmountMustBePositive));
        Assert.True(invalid.Has(ValidationCodes.ThresholdOutOfRange));

        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 100, November, null, null));
        var duplicate = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 200, November, null, null)));
        Assert.True(duplicate.Has(ValidationCodes.BudgetExistsForCategory));

        // The database enforces it too.
        await using var db = await ctx.Factory.CreateDbContextAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Budgets (Id, CategoryId, Amount, Period, StartDate, IsActive, CreatedAtUtc, UpdatedAtUtc) " +
            $"VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', '{food.Id.ToString().ToUpperInvariant()}', 5, 0, '2026-11-01', 1, '2026-11-09', '2026-11-09')"));
    }

    [Fact]
    public async Task Deleting_a_budget_never_touches_transactions()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000);
        var food = await ctx.CategoryAsync("Food");
        var budget = await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 500, November, null, null));
        await ctx.ExpenseAsync(bank, 200, "Food");

        await ctx.Budgets.DeleteAsync(budget.Id);

        Assert.Empty(await ctx.Budgets.ListAsync());
        Assert.Equal(800, await ctx.BalanceAsync(bank));
    }

    // ======================= Savings goals =======================

    [Fact]
    public async Task Allocations_earmark_money_without_changing_balances()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 10_000_000);
        var laptop = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Laptop Fund", 8_000_000, null, "Icon.SavingsGoals", "#F4806B"));

        await ctx.Goals.AllocateAsync(laptop.Id, bank.Id, 4_000_000, November, "First half");

        Assert.Equal(10_000_000, await ctx.BalanceAsync(bank));
        Assert.Equal(10_000_000, await ctx.Accounts.GetTotalBalanceAsync());
        var summary = (await ctx.Goals.GetAsync(laptop.Id))!;
        Assert.Equal(4_000_000, summary.Allocated);
        Assert.Equal(50, summary.ProgressPercent);
        Assert.Equal(4_000_000, summary.Remaining);
        var info = Assert.Single(await ctx.Goals.GetAccountAllocationsAsync(), i => i.AccountId == bank.Id);
        Assert.Equal(6_000_000, info.Available);
    }

    [Fact]
    public async Task Allocations_cannot_exceed_the_unallocated_balance_across_goals()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var a = await ctx.Goals.CreateAsync(new SavingsGoalDraft("A", 5_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        var b = await ctx.Goals.CreateAsync(new SavingsGoalDraft("B", 5_000_000, null, "Icon.SavingsGoals", "#F4806B"));

        await ctx.Goals.AllocateAsync(a.Id, bank.Id, 700_000, November, null);
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.AllocateAsync(b.Id, bank.Id, 300_001, November, null));
        Assert.True(ex.Has(ValidationCodes.AllocationExceedsAvailable));
        await ctx.Goals.AllocateAsync(b.Id, bank.Id, 300_000, November, null); // exactly the rest is fine
    }

    [Fact]
    public async Task Release_is_limited_to_what_the_goal_holds_from_that_account()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var cash = await ctx.NewAccountAsync("Cash", 1_000_000);
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Trip", 2_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 400_000, November, null);

        var tooMuch = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.ReleaseAsync(goal.Id, bank.Id, 400_001, November, null));
        Assert.True(tooMuch.Has(ValidationCodes.ReleaseExceedsAllocated));
        var wrongAccount = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.ReleaseAsync(goal.Id, cash.Id, 1, November, null));
        Assert.True(wrongAccount.Has(ValidationCodes.ReleaseExceedsAllocated));

        await ctx.Goals.ReleaseAsync(goal.Id, bank.Id, 150_000, November, "Changed plans");
        Assert.Equal(250_000, (await ctx.Goals.GetAsync(goal.Id))!.Allocated);
        var history = await ctx.Goals.GetHistoryAsync(goal.Id);
        Assert.Equal([-150_000L, 400_000L], history.Select(h => h.Amount).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Spending_below_the_allocation_marks_the_account_over_allocated_and_blocks_more()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Emergency", 5_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 900_000, November, null);
        await ctx.ExpenseAsync(bank, 300_000);

        var info = Assert.Single(await ctx.Goals.GetAccountAllocationsAsync(), i => i.AccountId == bank.Id);
        Assert.True(info.IsOverAllocated);
        Assert.Equal(0, info.Available);
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.AllocateAsync(goal.Id, bank.Id, 1, November, null));
    }

    [Fact]
    public async Task Inactive_accounts_and_archived_goals_accept_no_new_allocations()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var old = await ctx.NewAccountAsync("Old", 1_000_000);
        await ctx.Accounts.SetActiveAsync(old.Id, false);
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Car", 5_000_000, null, "Icon.Car", "#F4806B"));

        var inactive = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.AllocateAsync(goal.Id, old.Id, 10, November, null));
        Assert.True(inactive.Has(ValidationCodes.AccountInactive));

        await ctx.Goals.ArchiveAsync(goal.Id, November);
        var archived = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Goals.AllocateAsync(goal.Id, bank.Id, 10, November, null));
        Assert.True(archived.Has(ValidationCodes.GoalArchived));
    }

    [Fact]
    public async Task Archiving_releases_allocations_and_keeps_history()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var cash = await ctx.NewAccountAsync("Cash", 500_000);
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Bike", 3_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 600_000, November, null);
        await ctx.Goals.AllocateAsync(goal.Id, cash.Id, 200_000, November, null);

        await ctx.Goals.ArchiveAsync(goal.Id, November);

        var summary = (await ctx.Goals.GetAsync(goal.Id))!;
        Assert.Equal(SavingsGoalStatus.Archived, summary.Goal.Status);
        Assert.Equal(0, summary.Allocated);
        Assert.Equal(4, (await ctx.Goals.GetHistoryAsync(goal.Id)).Count);
        Assert.All(await ctx.Goals.GetAccountAllocationsAsync(), i => Assert.Equal(0, i.Allocated));
        Assert.Empty(await ctx.Goals.ListAsync());
        Assert.Single(await ctx.Goals.ListAsync(includeArchived: true));
    }

    [Fact]
    public async Task Goal_status_and_validation()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var invalid = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Goals.CreateAsync(new SavingsGoalDraft(" ", 0, null, "Icon.SavingsGoals", "#F4806B")));
        Assert.True(invalid.Has(ValidationCodes.Required));
        Assert.True(invalid.Has(ValidationCodes.AmountMustBePositive));

        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Phone", 1_000, new DateOnly(2027, 1, 31), "Icon.Smartphone", "#F4806B"));
        await ctx.Goals.SetStatusAsync(goal.Id, SavingsGoalStatus.Completed);
        var completed = (await ctx.Goals.GetAsync(goal.Id))!.Goal;
        Assert.Equal(SavingsGoalStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);

        await ctx.Goals.SetStatusAsync(goal.Id, SavingsGoalStatus.Active);
        Assert.Null((await ctx.Goals.GetAsync(goal.Id))!.Goal.CompletedAtUtc);
    }
}
