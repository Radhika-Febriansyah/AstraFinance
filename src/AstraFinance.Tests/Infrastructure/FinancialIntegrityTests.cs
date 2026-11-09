using AstraFinance.Core.Enums;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>Financial rules verified end-to-end against a real SQLite database.</summary>
public class FinancialIntegrityTests
{
    // ---------- Opening balances ----------

    [Theory]
    [InlineData(0)]
    [InlineData(2_500_000)]
    [InlineData(-150_000)]
    public async Task Opening_balance_is_the_initial_balance(long opening)
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: opening);
        Assert.Equal(opening, await ctx.BalanceAsync(account));
    }

    [Fact]
    public async Task Account_name_is_required()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.NewAccountAsync(name: "  "));
        Assert.True(ex.Has(ValidationCodes.Required));
    }

    // ---------- Income & expenses ----------

    [Fact]
    public async Task Income_increases_and_expense_decreases_balance()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 100_000);
        await ctx.IncomeAsync(account, 5_000_000);
        await ctx.ExpenseAsync(account, 75_000);

        Assert.Equal(5_025_000, await ctx.BalanceAsync(account));
        var totals = await ctx.Transactions.GetIncomeExpenseTotalsAsync(DateRange.MonthOf(FinanceTestContext.Today));
        Assert.Equal(new IncomeExpenseTotals(5_000_000, 75_000), totals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_amounts_are_rejected(long amount)
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.ExpenseAsync(account, amount));
        Assert.True(ex.Has(ValidationCodes.AmountMustBePositive));
    }

    [Fact]
    public async Task Category_kind_must_match_transaction_type()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var food = await ctx.CategoryAsync("Food");

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Income, Amount = 10, AccountId = account.Id, CategoryId = food.Id, Date = FinanceTestContext.Today,
        }));
        Assert.True(ex.Has(ValidationCodes.CategoryKindMismatch));
    }

    [Fact]
    public async Task Missing_references_are_rejected_with_all_errors()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Expense, Amount = 10, AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Date = FinanceTestContext.Today,
        }));
        Assert.True(ex.Has(ValidationCodes.AccountNotFound));
        Assert.True(ex.Has(ValidationCodes.CategoryNotFound));
    }

    [Fact]
    public async Task Duplicate_submission_is_saved_once()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var food = await ctx.CategoryAsync("Food");
        var draft = new TransactionDraft
        {
            Type = TransactionType.Expense, Amount = 20_000, AccountId = account.Id, CategoryId = food.Id, Date = FinanceTestContext.Today,
        };

        var first = await ctx.Transactions.CreateAsync(draft);
        var second = await ctx.Transactions.CreateAsync(draft);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
        Assert.Equal(-20_000, await ctx.BalanceAsync(account));
        Assert.Equal(1, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
    }

    // ---------- Transfers ----------

    [Fact]
    public async Task Transfer_moves_money_without_changing_total_or_income_expense()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000, AccountType.BankAccount);
        var wallet = await ctx.NewAccountAsync("Wallet", 0, AccountType.EWallet);

        await ctx.TransferAsync(bank, wallet, 250_000);

        Assert.Equal(750_000, await ctx.BalanceAsync(bank));
        Assert.Equal(250_000, await ctx.BalanceAsync(wallet));
        Assert.Equal(1_000_000, await ctx.Accounts.GetTotalBalanceAsync());

        var totals = await ctx.Transactions.GetIncomeExpenseTotalsAsync(DateRange.MonthOf(FinanceTestContext.Today));
        Assert.Equal(new IncomeExpenseTotals(0, 0), totals);
        Assert.Empty(await ctx.Transactions.GetExpenseTotalsByCategoryAsync(DateRange.MonthOf(FinanceTestContext.Today)));
    }

    [Fact]
    public async Task Transfer_fee_is_a_separate_linked_expense_from_the_source()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var wallet = await ctx.NewAccountAsync("Wallet");

        var result = await ctx.TransferAsync(bank, wallet, 250_000, fee: 6_500);

        Assert.NotNull(result.Fee);
        Assert.Equal(TransactionType.Expense, result.Fee!.Type);
        Assert.Equal(result.Transaction.Id, result.Fee.ParentTransactionId);
        Assert.Equal((await ctx.CategoryAsync(DefaultCategories.FeesKey)).Id, result.Fee.CategoryId);

        Assert.Equal(743_500, await ctx.BalanceAsync(bank));
        Assert.Equal(250_000, await ctx.BalanceAsync(wallet));
        Assert.Equal(993_500, await ctx.Accounts.GetTotalBalanceAsync());

        var totals = await ctx.Transactions.GetIncomeExpenseTotalsAsync(DateRange.MonthOf(FinanceTestContext.Today));
        Assert.Equal(new IncomeExpenseTotals(0, 6_500), totals);
    }

    [Theory]
    [InlineData(true, false, ValidationCodes.TransferSameAccount)]
    [InlineData(false, true, ValidationCodes.CategoryNotAllowedForTransfer)]
    public async Task Invalid_transfers_are_rejected(bool sameAccount, bool withCategory, string code)
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A");
        var b = await ctx.NewAccountAsync("B");
        var food = await ctx.CategoryAsync("Food");

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Transfer, Amount = 10, AccountId = a.Id, DestinationAccountId = sameAccount ? a.Id : b.Id,
            CategoryId = withCategory ? food.Id : null, Date = FinanceTestContext.Today,
        }));
        Assert.True(ex.Has(code));
    }

    [Fact]
    public async Task Fee_is_only_allowed_on_transfers()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var food = await ctx.CategoryAsync("Food");
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Expense, Amount = 10, TransferFee = 1, AccountId = account.Id, CategoryId = food.Id, Date = FinanceTestContext.Today,
        }));
        Assert.True(ex.Has(ValidationCodes.FeeOnlyForTransfer));
    }

    [Fact]
    public async Task Rejected_transfer_with_fee_writes_nothing()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A", 1_000);
        var b = await ctx.NewAccountAsync("B");
        var salary = await ctx.CategoryAsync("Salary"); // income category: invalid for a fee

        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Transfer, Amount = 500, TransferFee = 10, FeeCategoryId = salary.Id,
            AccountId = a.Id, DestinationAccountId = b.Id, Date = FinanceTestContext.Today,
        }));

        Assert.Equal(1_000, await ctx.BalanceAsync(a));
        Assert.Equal(0, await ctx.BalanceAsync(b));
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
    }

    // ---------- Balance adjustments ----------

    [Fact]
    public async Task Balance_adjustment_records_documented_delta_and_preserves_history()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 100_000);
        await ctx.ExpenseAsync(account, 30_000);

        var adjustment = await ctx.Accounts.AdjustBalanceAsync(account.Id, 65_000, "Cash count at month end", FinanceTestContext.Today);

        Assert.Equal(-5_000, adjustment.Amount);
        Assert.Equal(70_000, adjustment.BalanceBefore);
        Assert.Equal(65_000, await ctx.BalanceAsync(account));

        var summary = await ctx.Accounts.GetAsync(account.Id);
        Assert.Equal(100_000, summary!.Account.OpeningBalance); // never overwritten
        Assert.Equal(-5_000, summary.Components.Adjustments);
        Assert.Single(await ctx.Accounts.ListAdjustmentsAsync(account.Id));

        // Adjustments are not income or expense.
        var totals = await ctx.Transactions.GetIncomeExpenseTotalsAsync(DateRange.MonthOf(FinanceTestContext.Today));
        Assert.Equal(30_000, totals.Expense);
        Assert.Equal(0, totals.Income);
    }

    [Theory]
    [InlineData("", ValidationCodes.Required)]
    [InlineData("   ", ValidationCodes.Required)]
    public async Task Balance_adjustment_requires_a_reason(string reason, string code)
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Accounts.AdjustBalanceAsync(account.Id, 10, reason, FinanceTestContext.Today));
        Assert.True(ex.Has(code));
    }

    [Fact]
    public async Task Balance_adjustment_to_the_same_balance_is_rejected()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 500);
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Accounts.AdjustBalanceAsync(account.Id, 500, "No change", FinanceTestContext.Today));
        Assert.True(ex.Has(ValidationCodes.BalanceUnchanged));
    }

    // ---------- Editing ----------

    [Fact]
    public async Task Editing_amount_and_type_recalculates_balance()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 1_000);
        var expense = await ctx.ExpenseAsync(account, 300);
        var salary = await ctx.CategoryAsync("Salary");

        await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = expense.Id, Type = TransactionType.Income, Amount = 450, AccountId = account.Id, CategoryId = salary.Id,
            Date = FinanceTestContext.Today, Description = "Refund",
        });

        Assert.Equal(1_450, await ctx.BalanceAsync(account));
        var saved = await ctx.Transactions.GetAsync(expense.Id);
        Assert.Equal("Refund", saved!.Description);
        Assert.Equal(expense.CreatedAtUtc, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task Editing_a_transfer_to_move_accounts_updates_all_balances()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A", 1_000);
        var b = await ctx.NewAccountAsync("B");
        var c = await ctx.NewAccountAsync("C");
        var transfer = await ctx.TransferAsync(a, b, 400, fee: 10);

        await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = transfer.Transaction.Id, Type = TransactionType.Transfer, Amount = 300, TransferFee = 20,
            AccountId = a.Id, DestinationAccountId = c.Id, Date = FinanceTestContext.Today,
        });

        Assert.Equal(680, await ctx.BalanceAsync(a));
        Assert.Equal(0, await ctx.BalanceAsync(b));
        Assert.Equal(300, await ctx.BalanceAsync(c));
    }

    [Fact]
    public async Task Removing_a_fee_on_edit_soft_deletes_it_and_fee_cannot_be_edited_directly()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A", 1_000);
        var b = await ctx.NewAccountAsync("B");
        var transfer = await ctx.TransferAsync(a, b, 400, fee: 10);

        var direct = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Transactions.DeleteAsync(transfer.Fee!.Id));
        Assert.True(direct.Has(ValidationCodes.FeeEditedThroughTransfer));

        var updated = await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = transfer.Transaction.Id, Type = TransactionType.Transfer, Amount = 400,
            AccountId = a.Id, DestinationAccountId = b.Id, Date = FinanceTestContext.Today,
        });

        Assert.Null(updated.Fee);
        Assert.Equal(600, await ctx.BalanceAsync(a));
        Assert.True((await ctx.Transactions.GetAsync(transfer.Fee!.Id))!.IsDeleted);
    }

    // ---------- Deleting & restoring ----------

    [Fact]
    public async Task Deleted_transactions_leave_balances_totals_and_budgets_and_can_be_restored()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 1_000);
        var expense = await ctx.ExpenseAsync(account, 200);
        var month = DateRange.MonthOf(FinanceTestContext.Today);

        await ctx.Transactions.DeleteAsync(expense.Id);

        Assert.Equal(1_000, await ctx.BalanceAsync(account));
        Assert.Equal(0, (await ctx.Transactions.GetIncomeExpenseTotalsAsync(month)).Expense);
        Assert.Empty(await ctx.Transactions.GetExpenseTotalsByCategoryAsync(month));
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
        var bin = await ctx.Transactions.QueryAsync(new TransactionQuery { DeletedOnly = true });
        Assert.Equal(expense.Id, Assert.Single(bin.Items).Id);

        await ctx.Transactions.RestoreAsync(expense.Id);

        Assert.Equal(800, await ctx.BalanceAsync(account));
        Assert.Equal(200, (await ctx.Transactions.GetIncomeExpenseTotalsAsync(month)).Expense);
        Assert.False((await ctx.Transactions.GetAsync(expense.Id))!.IsDeleted);
    }

    [Fact]
    public async Task Deleting_and_restoring_a_transfer_includes_its_fee_only()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A", 1_000);
        var b = await ctx.NewAccountAsync("B");

        // An earlier fee removed by editing must not come back on restore.
        var transfer = await ctx.TransferAsync(a, b, 100, fee: 5);
        var oldFeeId = transfer.Fee!.Id;
        ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddMinutes(1);
        var edited = await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = transfer.Transaction.Id, Type = TransactionType.Transfer, Amount = 100, TransferFee = 7,
            AccountId = a.Id, DestinationAccountId = b.Id, Date = FinanceTestContext.Today,
        });
        Assert.Equal(oldFeeId, edited.Fee!.Id); // fee updated in place

        ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddMinutes(1);
        await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = transfer.Transaction.Id, Type = TransactionType.Transfer, Amount = 100,
            AccountId = a.Id, DestinationAccountId = b.Id, Date = FinanceTestContext.Today,
        });
        var withNewFee = await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = transfer.Transaction.Id, Type = TransactionType.Transfer, Amount = 100, TransferFee = 3,
            AccountId = a.Id, DestinationAccountId = b.Id, Date = FinanceTestContext.Today,
        });
        Assert.NotEqual(oldFeeId, withNewFee.Fee!.Id);
        Assert.Equal(897, await ctx.BalanceAsync(a));

        ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddMinutes(1);
        await ctx.Transactions.DeleteAsync(transfer.Transaction.Id);
        Assert.Equal(1_000, await ctx.BalanceAsync(a));
        Assert.Equal(0, await ctx.BalanceAsync(b));

        await ctx.Transactions.RestoreAsync(transfer.Transaction.Id);
        Assert.Equal(897, await ctx.BalanceAsync(a));
        Assert.Equal(100, await ctx.BalanceAsync(b));
        Assert.True((await ctx.Transactions.GetAsync(oldFeeId))!.IsDeleted);
    }

    // ---------- Inactive accounts & categories ----------

    [Fact]
    public async Task Deactivated_account_keeps_history_and_balance_but_accepts_no_new_activity()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 500);
        var expense = await ctx.ExpenseAsync(account, 100);
        await ctx.Accounts.SetActiveAsync(account.Id, false);

        Assert.Equal(400, await ctx.BalanceAsync(account));
        Assert.Equal(400, await ctx.Accounts.GetTotalBalanceAsync());
        Assert.Empty(await ctx.Accounts.ListAsync());
        Assert.Single(await ctx.Accounts.ListAsync(includeInactive: true));

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.ExpenseAsync(account, 1));
        Assert.True(ex.Has(ValidationCodes.AccountInactive));

        // Correcting existing history on the inactive account is still possible.
        var food = await ctx.CategoryAsync("Food");
        await ctx.Transactions.UpdateAsync(new TransactionDraft
        {
            Id = expense.Id, Type = TransactionType.Expense, Amount = 120, AccountId = account.Id, CategoryId = food.Id, Date = FinanceTestContext.Today,
        });
        Assert.Equal(380, await ctx.BalanceAsync(account));
    }

    [Fact]
    public async Task Categories_have_localized_defaults_and_unique_custom_names()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var food = await ctx.CategoryAsync("Food");
        Assert.Equal("Makanan & Minuman", DefaultCategories.DisplayName(food, new LocalizationService(AppLanguage.Indonesian)));
        Assert.Equal("Food & Drinks", DefaultCategories.DisplayName(food, new LocalizationService(AppLanguage.English)));

        await ctx.Categories.CreateAsync(new CategoryDraft("Kopi Pagi", CategoryKind.Expense, "Icon.Utensils", "#F4806B"));
        var dup = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Categories.CreateAsync(new CategoryDraft("KOPI PAGI", CategoryKind.Expense, "Icon.Utensils", "#F4806B")));
        Assert.True(dup.Has(ValidationCodes.DuplicateName));

        // Same name under the other kind is allowed.
        await ctx.Categories.CreateAsync(new CategoryDraft("Kopi Pagi", CategoryKind.Income, "Icon.Store", "#F4806B"));

        // Restyling a built-in category keeps it localized.
        var restyled = await ctx.Categories.UpdateAsync(food.Id, null, "Icon.Store", "#000000");
        Assert.Equal("Food", restyled.SystemKey);

        await ctx.Categories.SetActiveAsync(food.Id, false);
        Assert.DoesNotContain(await ctx.Categories.ListAsync(CategoryKind.Expense), c => c.Id == food.Id);
        var account = await ctx.NewAccountAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.ExpenseAsync(account, 10));
        Assert.True(ex.Has(ValidationCodes.CategoryInactive));
    }

    // ---------- Budgets ----------

    [Fact]
    public async Task Budget_spending_counts_only_expenses_in_the_period()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 10_000_000);
        var wallet = await ctx.NewAccountAsync("Wallet");
        var food = await ctx.CategoryAsync("Food");
        var month = DateRange.MonthOf(FinanceTestContext.Today);

        await ctx.ExpenseAsync(bank, 300_000, "Food");
        await ctx.ExpenseAsync(wallet, 550_000, "Food", new DateOnly(2026, 11, 30));
        await ctx.ExpenseAsync(bank, 999_000, "Food", new DateOnly(2026, 10, 31));        // previous month
        await ctx.ExpenseAsync(bank, 50_000, "Transportation");                            // other category
        await ctx.IncomeAsync(bank, 5_000_000);                                            // income
        await ctx.TransferAsync(bank, wallet, 1_000_000);                                  // transfer
        var deleted = await ctx.ExpenseAsync(bank, 77_000, "Food");
        await ctx.Transactions.DeleteAsync(deleted.Id);                                    // deleted

        var totals = await ctx.Transactions.GetExpenseTotalsByCategoryAsync(month);
        Assert.Equal(850_000, totals[food.Id]);

        var evaluation = BudgetEvaluator.Evaluate(1_000_000, totals[food.Id]);
        Assert.Equal(BudgetStatus.Warning, evaluation.Status);
        Assert.Equal(150_000, evaluation.Remaining);
        Assert.Equal(85, evaluation.UsagePercent);
    }

    // ---------- Dates & querying ----------

    [Fact]
    public async Task Dates_are_stored_date_only_and_timestamps_as_utc()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var newYearsEve = new DateOnly(2026, 12, 31);
        var saved = await ctx.ExpenseAsync(account, 10, date: newYearsEve);

        var loaded = await ctx.Transactions.GetAsync(saved.Id);
        Assert.Equal(newYearsEve, loaded!.Date);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(ctx.Clock.UtcNow.UtcDateTime, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Query_filters_sorts_and_pages()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var wallet = await ctx.NewAccountAsync("Wallet");
        await ctx.ExpenseAsync(bank, 100, description: "Coffee 50% off", date: new DateOnly(2026, 11, 1));
        await ctx.ExpenseAsync(bank, 300, description: "Lunch", date: new DateOnly(2026, 11, 2));
        await ctx.IncomeAsync(bank, 900, description: "Salary", date: new DateOnly(2026, 11, 3));
        await ctx.TransferAsync(bank, wallet, 200, date: new DateOnly(2026, 11, 4));

        var all = await ctx.Transactions.QueryAsync(new TransactionQuery { PageSize = 2 });
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(2, all.PageCount);
        Assert.Equal(new DateOnly(2026, 11, 4), all.Items[0].Date);

        var walletOnly = await ctx.Transactions.QueryAsync(new TransactionQuery { AccountIds = [wallet.Id] });
        Assert.Equal(TransactionType.Transfer, Assert.Single(walletOnly.Items).Type);

        var search = await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "50%" });
        Assert.Equal("Coffee 50% off", Assert.Single(search.Items).Description);
        Assert.Single((await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "LUNCH" })).Items);

        var expenses = await ctx.Transactions.QueryAsync(new TransactionQuery
        {
            Types = [TransactionType.Expense], Sort = TransactionSort.AmountDescending,
        });
        Assert.Equal([300L, 100L], expenses.Items.Select(t => t.Amount));

        var range = await ctx.Transactions.QueryAsync(new TransactionQuery
        {
            From = new DateOnly(2026, 11, 2), To = new DateOnly(2026, 11, 3), Sort = TransactionSort.DateAscending,
        });
        Assert.Equal(["Lunch", "Salary"], range.Items.Select(t => t.Description));

        var food = await ctx.CategoryAsync("Food");
        Assert.Equal(2, (await ctx.Transactions.QueryAsync(new TransactionQuery { CategoryIds = [food.Id] })).TotalCount);
    }
}
