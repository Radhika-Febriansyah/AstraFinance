using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Rules;
using AstraFinance.Infrastructure.Export;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>Phase 10: upgrades from the first schema, offline guarantee, and end-to-end reconciliation.</summary>
public class ReleaseQualityTests
{
    private const string FirstMigration = "20261109073602_InitialCreate";

    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public async Task Upgrading_a_database_from_the_first_release_schema_keeps_every_record()
    {
        await using var ctx = await FinanceTestContext.CreateAsync(initialize: false);
        var now = DateTime.UtcNow;
        var category = new Category { Id = Guid.NewGuid(), Name = "Groceries", Kind = CategoryKind.Expense, IconKey = "Icon.Utensils", ColorHex = "#F4806B", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var salary = new Category { Id = Guid.NewGuid(), Name = "Pay", Kind = CategoryKind.Income, IconKey = "Icon.Briefcase", ColorHex = "#3DBE8B", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var bank = new Account { Id = Guid.NewGuid(), Name = "Bank", Type = AccountType.BankAccount, OpeningBalance = 1_000_000, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var cash = new Account { Id = Guid.NewGuid(), Name = "Cash", Type = AccountType.Cash, OpeningBalance = 50_000, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        Transaction Tx(TransactionType type, long amount, Account from, Category? c, DateOnly date, Account? to = null, Guid? parent = null) => new()
        {
            Id = Guid.NewGuid(), Type = type, Amount = amount, AccountId = from.Id, DestinationAccountId = to?.Id, CategoryId = c?.Id,
            Description = type.ToString(), Date = date, ParentTransactionId = parent, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        var income = Tx(TransactionType.Income, 5_000_000, bank, salary, D(10, 25));
        var groceries = Tx(TransactionType.Expense, 300_000, bank, category, D(11, 2));
        var transfer = Tx(TransactionType.Transfer, 200_000, bank, null, D(11, 3), cash);
        var fee = Tx(TransactionType.Expense, 2_500, bank, category, D(11, 3), parent: transfer.Id);
        var billExpense = Tx(TransactionType.Expense, 400_000, bank, category, D(11, 5));
        var bill = new Bill { Id = Guid.NewGuid(), Name = "Electricity", Amount = 400_000, CategoryId = category.Id, PaymentAccountId = bank.Id, FirstDueDate = D(11, 5), Recurrence = RecurrenceFrequency.Monthly, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var goal = new SavingsGoal { Id = Guid.NewGuid(), Name = "Laptop", TargetAmount = 8_000_000, CreatedAtUtc = now, UpdatedAtUtc = now };

        // Build the database exactly as the first release did, then add data with that schema.
        await using (var db = await ctx.Factory.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync(FirstMigration);
            db.AddRange(category, salary, bank, cash, income, groceries, transfer, fee, billExpense, bill, goal);
            db.Add(new BillPayment { Id = Guid.NewGuid(), BillId = bill.Id, DueDate = D(11, 5), TransactionId = billExpense.Id, PaidAtUtc = now });
            db.Add(new Budget { Id = Guid.NewGuid(), CategoryId = category.Id, Amount = 1_000_000, Period = BudgetPeriod.Monthly, StartDate = D(11, 1), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Add(new GoalAllocation { Id = Guid.NewGuid(), GoalId = goal.Id, AccountId = bank.Id, Amount = 1_500_000, Date = D(11, 6), CreatedAtUtc = now });
            await db.SaveChangesAsync();
        }

        var result = await ctx.Initializer.InitializeAsync();

        Assert.Equal(2, result.AppliedMigrations.Count);
        Assert.True(File.Exists(result.PreMigrationBackupPath));
        Assert.Equal(1_000_000 + 5_000_000 - 300_000 - 200_000 - 2_500 - 400_000, await ctx.Accounts.GetBalanceAsync(bank.Id));
        Assert.Equal(250_000, await ctx.Accounts.GetBalanceAsync(cash.Id));
        Assert.Equal(D(12, 5), (await ctx.Bills.GetAsync(bill.Id, D(11, 9)))!.NextDue);            // the paid occurrence survived
        Assert.Equal(702_500, Assert.Single((await ctx.Budgets.GetOverviewAsync(D(11, 9))).Items).Evaluation.Spent);
        Assert.Equal(1_500_000, (await ctx.Goals.GetAsync(goal.Id))!.Allocated);
        Assert.Contains(await ctx.Categories.ListAsync(), c => c.SystemKey == "Fees");               // built-ins added on upgrade
    }

    [Theory]
    [InlineData("AstraFinance")]
    [InlineData("AstraFinance.Core")]
    [InlineData("AstraFinance.Infrastructure")]
    public void The_app_never_uses_the_network(string assemblyName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName)
                       ?? System.Reflection.Assembly.Load(assemblyName);
        string[] network = ["System.Net.Http", "System.Net.Sockets", "System.Net.Requests", "System.Net.WebClient", "System.Net.WebSockets", "System.Net.WebSockets.Client"];
        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(referenced, n => network.Contains(n));
    }

    [Fact]
    public async Task Every_figure_reconciles_across_balances_analytics_reports_and_budgets()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 3_000_000);
        var cash = await ctx.NewAccountAsync("Cash", 200_000);
        var food = await ctx.CategoryAsync("Food");
        await ctx.Budgets.CreateAsync(new BudgetDraft(food.Id, 500_000, D(11, 1), null, null));

        await ctx.IncomeAsync(bank, 8_000_000, D(11, 1));
        await ctx.ExpenseAsync(cash, 45_000, "Food", D(11, 2));
        await ctx.TransferAsync(bank, cash, 300_000, fee: 6_500, date: D(11, 3));
        await ctx.Accounts.AdjustBalanceAsync(cash.Id, 460_000, "Counted", D(11, 4));
        var deleted = await ctx.ExpenseAsync(bank, 999_000, "Food", D(11, 5));
        await ctx.Transactions.DeleteAsync(deleted.Id);
        var restored = await ctx.ExpenseAsync(bank, 120_000, "Food", D(11, 6));
        await ctx.Transactions.DeleteAsync(restored.Id);
        await ctx.Transactions.RestoreAsync(restored.Id);
        var utilities = await ctx.CategoryAsync("BillsUtilities");
        var bill = await ctx.Bills.CreateAsync(new BillDraft("Internet", 350_000, utilities.Id, bank.Id, D(11, 7), RecurrenceFrequency.Monthly, null));
        await ctx.Bills.PayAsync(new BillPaymentDraft(Guid.NewGuid(), bill.Id, D(11, 7), 350_000, bank.Id, D(11, 7), null));
        var goal = await ctx.Goals.CreateAsync(new SavingsGoalDraft("Trip", 2_000_000, null, "Icon.SavingsGoals", "#F4806B"));
        await ctx.Goals.AllocateAsync(goal.Id, bank.Id, 1_000_000, D(11, 8), null);
        var csv = TransactionImportService.Parse("Date;Description;Amount\n09/11/2026;Freelance;750000\n09/11/2026;Lunch;-30000\n");
        var preview = await ctx.Import.PreviewAsync(csv, new ImportOptions(cash.Id, ctx.Import.GuessMapping(csv.Headers), DateFormatStyle.DayMonthYear, ImportNumberStyle.Plain));
        await ctx.Import.ImportAsync(cash.Id, preview.Rows);

        var november = DateRange.Month(2026, 11);
        var income = 8_000_000L + 750_000;
        var expenses = 45_000L + 6_500 + 120_000 + 350_000 + 30_000;
        var adjustments = 460_000L - (200_000 - 45_000 + 300_000); // the documented correction on Cash

        // Balances: opening + income - expenses + adjustments; transfers and goal allocations change nothing in total.
        var total = await ctx.Accounts.GetTotalBalanceAsync();
        Assert.Equal(3_200_000 + income - expenses + adjustments, total);
        Assert.Equal(total, await ctx.BalanceAsync(bank) + await ctx.BalanceAsync(cash));

        // Analytics and reports agree with each other and with the balances.
        var snapshot = await ctx.Analytics.GetSnapshotAsync(november);
        Assert.Equal((income, expenses), (snapshot.Income, snapshot.Expense));
        var incomeReport = await ctx.Reports.BuildAsync(ReportKind.Income, november);
        var expenseReport = await ctx.Reports.BuildAsync(ReportKind.Expense, november);
        Assert.Equal(income, incomeReport.Summary[0].Value.Value);
        Assert.Equal(expenses, expenseReport.Summary[0].Value.Value);
        Assert.Equal(5, expenseReport.Tables[1].Rows.Count); // lunch, fee, restored, bill, imported lunch; the deleted one is excluded
        var cashFlow = await ctx.Reports.BuildAsync(ReportKind.CashFlow, november);
        Assert.Equal(total, cashFlow.Summary[5].Value.Value);
        var accounts = await ctx.Reports.BuildAsync(ReportKind.AccountBalance, november);
        Assert.Equal(total, accounts.Tables[0].Total![8].Value);

        // Budgets count only Food expenses; the goal holds its allocation without moving money.
        // (the imported lunch has no category column, so it went to "Other expenses", not Food)
        var budget = Assert.Single((await ctx.Budgets.GetOverviewAsync(D(11, 9))).Items).Evaluation.Spent;
        Assert.Equal(45_000 + 120_000, budget);
        Assert.Equal(1_000_000, (await ctx.Goals.GetAsync(goal.Id))!.Allocated);
        Assert.Equal(expenseReport.Tables[0].Rows.Single(r => r[0].Text == "Food & Drinks")[2].Value, budget);
    }
}
