using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Tests.Infrastructure;

public class BillsTests
{
    private static readonly DateOnly Today = FinanceTestContext.Today; // 9 Nov 2026

    private static async Task<(FinanceTestContext Ctx, Account Bank, Category Utilities)> SetupAsync()
    {
        var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 5_000_000);
        var utilities = await ctx.CategoryAsync("BillsUtilities");
        return (ctx, bank, utilities);
    }

    private static BillDraft Draft(Account account, Category category, DateOnly firstDue, RecurrenceFrequency recurrence = RecurrenceFrequency.Monthly, long amount = 350_000, string name = "Electricity") =>
        new(name, amount, category.Id, account.Id, firstDue, recurrence, null);

    private static BillPaymentDraft Payment(Bill bill, DateOnly due, Account account, long? amount = null, Guid? requestId = null) =>
        new(requestId ?? Guid.NewGuid(), bill.Id, due, amount ?? bill.Amount, account.Id, Today, null);

    [Theory]
    [InlineData(1, BillStatus.Overdue)]
    [InlineData(9, BillStatus.DueToday)]
    [InlineData(12, BillStatus.DueSoon)]   // within the default 3-day window
    [InlineData(13, BillStatus.Upcoming)]
    public async Task Status_is_derived_from_the_next_due_date(int day, BillStatus expected)
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, day)));

        var summary = (await ctx.Bills.GetAsync(bill.Id, Today))!;
        Assert.Equal(new DateOnly(2026, 11, day), summary.NextDue);
        Assert.Equal(expected, summary.Status);
        Assert.Equal(expected == BillStatus.Overdue ? 1 : 0, summary.MissedCount);
    }

    [Fact]
    public async Task Reaching_a_due_date_never_changes_a_balance()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 7, 1)));

        var summary = Assert.Single(await ctx.Bills.ListAsync(Today));
        Assert.Equal(BillStatus.Overdue, summary.Status);
        Assert.Equal(5, summary.MissedCount); // Jul, Aug, Sep, Oct, Nov 1
        Assert.Equal(5_000_000, await ctx.Accounts.GetBalanceAsync(bank.Id));
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
    }

    [Fact]
    public async Task Confirmed_payment_creates_one_expense_and_moves_to_the_next_occurrence()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 10)));
        var request = Guid.NewGuid();

        var expense = await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 10), bank, 362_500, request));

        Assert.Equal(TransactionType.Expense, expense.Type);
        Assert.Equal((362_500L, utilities.Id, "Electricity", Today), (expense.Amount, expense.CategoryId!.Value, expense.Description, expense.Date));
        Assert.Equal(5_000_000 - 362_500, await ctx.Accounts.GetBalanceAsync(bank.Id));

        var summary = (await ctx.Bills.GetAsync(bill.Id, Today))!;
        Assert.Equal(new DateOnly(2026, 12, 10), summary.NextDue);
        Assert.Equal(BillStatus.Upcoming, summary.Status);
        Assert.Equal(expense.Id, summary.LastHandled!.Transaction!.Id);
        Assert.True(summary.HasHistory);

        // The same confirmation again (double click / retry) writes nothing twice.
        var again = await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 10), bank, 362_500, request));
        Assert.Equal(expense.Id, again.Id);
        Assert.Equal(1, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);

        // A second, different confirmation of the same occurrence is refused.
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 10), bank)));
        Assert.Contains(ex.Errors, e => e.Code == ValidationCodes.BillAlreadyHandled);
        Assert.Equal(5_000_000 - 362_500, await ctx.Accounts.GetBalanceAsync(bank.Id));
    }

    [Fact]
    public async Task Only_the_next_open_occurrence_can_be_paid()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 10, 20)));

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 20), bank)));
        Assert.Contains(ex.Errors, e => e.Code == ValidationCodes.BillOccurrenceNotOpen);
        await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 10, 20), bank));
        await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 20), bank)); // paying early is allowed
        Assert.Equal(new DateOnly(2026, 12, 20), (await ctx.Bills.GetAsync(bill.Id, Today))!.NextDue);
    }

    [Fact]
    public async Task Payment_uses_normal_transaction_validation_and_writes_nothing_on_failure()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 10)));
        var other = await ctx.NewAccountAsync("Old wallet");
        await ctx.Accounts.SetActiveAsync(other.Id, false);

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 10), other)));
        Assert.Contains(ex.Errors, e => e.Code == ValidationCodes.AccountInactive);
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 10), bank, amount: 0)));

        await using var db = await ctx.Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.BillPayments.CountAsync());
        Assert.Equal(0, await db.Transactions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Deleting_the_payment_expense_reopens_the_occurrence_and_paying_again_reuses_the_record()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 5)));
        var first = await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 5), bank));

        await ctx.Transactions.DeleteAsync(first.Id);
        var summary = (await ctx.Bills.GetAsync(bill.Id, Today))!;
        Assert.Equal(new DateOnly(2026, 11, 5), summary.NextDue);
        Assert.Equal(BillStatus.Overdue, summary.Status);
        Assert.Equal(5_000_000, await ctx.Accounts.GetBalanceAsync(bank.Id));
        Assert.True(Assert.Single(await ctx.Bills.GetHistoryAsync(bill.Id)).IsVoided);

        var second = await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 5), bank));
        await using var db = await ctx.Factory.CreateDbContextAsync();
        var record = Assert.Single(await db.BillPayments.ToListAsync());
        Assert.Equal(second.Id, record.TransactionId);
        Assert.Equal(5_000_000 - 350_000, await ctx.Accounts.GetBalanceAsync(bank.Id));
    }

    [Fact]
    public async Task Skip_records_no_expense_and_can_be_undone()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 1)));

        await ctx.Bills.SkipAsync(bill.Id, new DateOnly(2026, 11, 1));
        var summary = (await ctx.Bills.GetAsync(bill.Id, Today))!;
        Assert.Equal(new DateOnly(2026, 12, 1), summary.NextDue);
        Assert.True(summary.LastHandled!.IsSkipped);
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
        Assert.Equal(5_000_000, await ctx.Accounts.GetBalanceAsync(bank.Id));

        await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 12, 1), bank));
        // Only the latest handled occurrence can be un-skipped.
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.UndoSkipAsync(bill.Id, new DateOnly(2026, 11, 1)));

        await ctx.Bills.SkipAsync(bill.Id, new DateOnly(2027, 1, 1));
        await ctx.Bills.UndoSkipAsync(bill.Id, new DateOnly(2027, 1, 1));
        Assert.Equal(new DateOnly(2027, 1, 1), (await ctx.Bills.GetAsync(bill.Id, Today))!.NextDue);
    }

    [Fact]
    public async Task Skip_missed_moves_an_overdue_bill_to_its_next_occurrence_without_expenses()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 7, 15)));
        Assert.Equal(4, (await ctx.Bills.GetAsync(bill.Id, Today))!.MissedCount);

        await ctx.Bills.SkipMissedAsync(bill.Id, Today);

        var summary = (await ctx.Bills.GetAsync(bill.Id, Today))!;
        Assert.Equal(new DateOnly(2026, 11, 15), summary.NextDue);
        Assert.Equal(BillStatus.Upcoming, summary.Status);
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);
        // Nothing missed any more.
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.SkipMissedAsync(bill.Id, Today));
    }

    [Fact]
    public async Task Paused_bills_are_hidden_and_cannot_be_paid_and_bills_with_history_cannot_be_deleted()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var internet = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 10), name: "Internet"));
        var streaming = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 20), name: "Streaming"));

        await ctx.Bills.SetActiveAsync(internet.Id, false);
        Assert.Equal(streaming.Id, Assert.Single(await ctx.Bills.ListAsync(Today)).Bill.Id);
        var all = await ctx.Bills.ListAsync(Today, includePaused: true);
        Assert.Equal(BillStatus.Paused, all[^1].Status);
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.PayAsync(Payment(internet, new DateOnly(2026, 11, 10), bank)));
        Assert.Contains(ex.Errors, e => e.Code == ValidationCodes.BillPaused);

        await ctx.Bills.SetActiveAsync(internet.Id, true);
        await ctx.Bills.PayAsync(Payment(internet, new DateOnly(2026, 11, 10), bank));
        ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Bills.DeleteAsync(internet.Id));
        Assert.Contains(ex.Errors, e => e.Code == ValidationCodes.BillHasHistory);

        await ctx.Bills.DeleteAsync(streaming.Id);
        Assert.Null(await ctx.Bills.GetAsync(streaming.Id, Today));
    }

    [Fact]
    public async Task Bill_validation()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var salary = await ctx.CategoryAsync("Salary");

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Bills.CreateAsync(new BillDraft(" ", 0, salary.Id, Guid.NewGuid(), default, RecurrenceFrequency.Monthly, null)));
        var codes = ex.Errors.Select(e => (e.Field, e.Code)).ToHashSet();
        Assert.Contains(("Name", ValidationCodes.Required), codes);
        Assert.Contains(("Amount", ValidationCodes.AmountMustBePositive), codes);
        Assert.Contains(("FirstDueDate", ValidationCodes.Required), codes);
        Assert.Contains(("CategoryId", ValidationCodes.CategoryKindMismatch), codes);
        Assert.Contains(("PaymentAccountId", ValidationCodes.AccountNotFound), codes);
    }

    [Fact]
    public async Task Bill_payments_count_toward_budgets_like_any_expense()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        await ctx.Budgets.CreateAsync(new BudgetDraft(utilities.Id, 1_000_000, Today, null, null));
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 3)));
        Assert.Equal(0, Assert.Single((await ctx.Budgets.GetOverviewAsync(Today)).Items).Evaluation.Spent); // unpaid: nothing spent

        await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 3), bank));
        Assert.Equal(350_000, Assert.Single((await ctx.Budgets.GetOverviewAsync(Today)).Items).Evaluation.Spent);
    }

    // ======================= Reminders =======================

    [Fact]
    public async Task Bill_reminders_appear_once_per_occurrence_and_stage()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        var bill = await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 11)));
        await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 30), name: "Far away"));

        var first = Assert.Single(await ctx.Reminders.CollectDueAsync(Today));
        Assert.Equal((ReminderKind.BillDueSoon, bill.Id, new DateOnly(2026, 11, 11)), (first.Kind, first.EntityId, first.Date));
        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today)); // not repeated

        Assert.Equal(ReminderKind.BillDueToday, Assert.Single(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 11, 11))).Kind);
        Assert.Equal(ReminderKind.BillOverdue, Assert.Single(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 11, 12))).Kind);
        Assert.Empty(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 11, 13)));

        // Paid: the next occurrence (11 Dec) reminds again when it comes close.
        await ctx.Bills.PayAsync(Payment(bill, new DateOnly(2026, 11, 11), bank));
        Assert.Empty(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 11, 13)));
        Assert.Contains(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 12, 9)), r => r.EntityId == bill.Id && r.Date == new DateOnly(2026, 12, 11));
    }

    [Fact]
    public async Task Reminders_respect_notification_settings()
    {
        var (ctx, bank, utilities) = await SetupAsync();
        await using var _ = ctx;
        await ctx.Bills.CreateAsync(Draft(bank, utilities, new DateOnly(2026, 11, 15)));

        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today)); // 6 days away, default window 3
        await ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BillReminderDays = 7 });
        Assert.Equal(BillStatus.DueSoon, Assert.Single(await ctx.Bills.ListAsync(Today)).Status);
        await ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BillReminderDays = 7, Enabled = false });
        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today));
        await ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BillReminderDays = 7 });
        Assert.Single(await ctx.Reminders.CollectDueAsync(Today));

        var saved = await ctx.NotificationSettings.GetAsync();
        Assert.Equal(7, saved.BillReminderDays);
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.NotificationSettings.SaveAsync(saved with { BillReminderDays = 15 }));
    }

    [Fact]
    public async Task Budget_warnings_appear_once_per_level_and_month()
    {
        var (ctx, bank, _) = await SetupAsync();
        await using var _ = ctx;
        var food = await ctx.CategoryAsync("Food");
        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 100_000, Today, null, null));

        await ctx.ExpenseAsync(bank, 50_000, "Food");
        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today));

        var big = await ctx.ExpenseAsync(bank, 35_000, "Food"); // 85 %
        var warning = Assert.Single(await ctx.Reminders.CollectDueAsync(Today));
        Assert.Equal((ReminderKind.BudgetWarning, 85L, food.Id), (warning.Kind, warning.Percent, warning.Category!.Id));
        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today));

        await ctx.ExpenseAsync(bank, 30_000, "Food"); // 115 %
        Assert.Equal(ReminderKind.BudgetExceeded, Assert.Single(await ctx.Reminders.CollectDueAsync(Today)).Kind);

        await ctx.Transactions.DeleteAsync(big.Id);   // back to 80 %: no new warning this month
        Assert.Empty(await ctx.Reminders.CollectDueAsync(Today));

        await ctx.ExpenseAsync(bank, 90_000, "Food", new DateOnly(2026, 12, 2)); // a new month warns again
        Assert.Equal(ReminderKind.BudgetWarning, Assert.Single(await ctx.Reminders.CollectDueAsync(new DateOnly(2026, 12, 3))).Kind);
    }
}
