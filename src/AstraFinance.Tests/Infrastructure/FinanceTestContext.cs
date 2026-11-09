using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Backup;
using AstraFinance.Infrastructure.Database;
using AstraFinance.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>A fresh, migrated SQLite database file per test, with real services.</summary>
internal sealed class FinanceTestContext : IAsyncDisposable
{
    private readonly TempDirectory _dir = new();

    private FinanceTestContext()
    {
        Paths = new AppPaths(_dir.Path);
        Factory = new FileContextFactory(Paths.DatabaseFile);
        Accounts = new AccountService(Factory, Clock);
        Categories = new CategoryService(Factory, Clock);
        Transactions = new TransactionService(Factory, Clock);
        Settings = new SettingsStore(Factory, Clock);
        Pin = new PinService(Settings, Clock, new PinHasher(1_000), PinAttemptPolicy.Default);
        Onboarding = new OnboardingService(Settings, Accounts, Pin);
        Regional = new RegionalFormatService(Settings, Accounts);
        Backup = new BackupService(Settings, Paths, Clock, NullLogger<BackupService>.Instance);
        Initializer = new DatabaseInitializer(Factory, Paths, Categories, Clock, NullLogger<DatabaseInitializer>.Instance);
    }

    public FakeClock Clock { get; } = new();

    public AppPaths Paths { get; }

    public IDbContextFactory<AstraDbContext> Factory { get; }

    public AccountService Accounts { get; }

    public CategoryService Categories { get; }

    public TransactionService Transactions { get; }

    public SettingsStore Settings { get; }

    public PinService Pin { get; }

    public OnboardingService Onboarding { get; }

    public RegionalFormatService Regional { get; }

    public BackupService Backup { get; }

    public DatabaseInitializer Initializer { get; }

    public static DateOnly Today => new(2026, 11, 9);

    public static async Task<FinanceTestContext> CreateAsync(bool initialize = true)
    {
        var ctx = new FinanceTestContext();
        if (initialize)
        {
            await ctx.Initializer.InitializeAsync();
        }

        return ctx;
    }

    public async Task<Account> NewAccountAsync(string name = "Cash", long opening = 0, AccountType type = AccountType.Cash) =>
        await Accounts.CreateAsync(new AccountDraft(name, type, opening, "Icon.Accounts", "#F4806B"));

    public async Task<Category> CategoryAsync(string systemKey)
    {
        var all = await Categories.ListAsync(includeInactive: true);
        return all.Single(c => c.SystemKey == systemKey);
    }

    public async Task<Transaction> IncomeAsync(Account account, long amount, DateOnly? date = null, string description = "")
    {
        var salary = await CategoryAsync("Salary");
        return (await Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Income, Amount = amount, AccountId = account.Id, CategoryId = salary.Id,
            Date = date ?? Today, Description = description,
        })).Transaction;
    }

    public async Task<Transaction> ExpenseAsync(Account account, long amount, string categoryKey = "Food", DateOnly? date = null, string description = "")
    {
        var category = await CategoryAsync(categoryKey);
        return (await Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Expense, Amount = amount, AccountId = account.Id, CategoryId = category.Id,
            Date = date ?? Today, Description = description,
        })).Transaction;
    }

    public async Task<TransactionSaveResult> TransferAsync(Account from, Account to, long amount, long fee = 0, DateOnly? date = null) =>
        await Transactions.CreateAsync(new TransactionDraft
        {
            Type = TransactionType.Transfer, Amount = amount, AccountId = from.Id, DestinationAccountId = to.Id,
            TransferFee = fee, Date = date ?? Today,
        });

    public async Task<long> BalanceAsync(Account account) => await Accounts.GetBalanceAsync(account.Id);

    public async ValueTask DisposeAsync()
    {
        Pin.Dispose();
        Backup.Dispose();
        SqliteConnection.ClearAllPools();
        await Task.Yield();
        _dir.Dispose();
    }

    private sealed class FileContextFactory(string file) : IDbContextFactory<AstraDbContext>
    {
        public AstraDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AstraDbContext>();
            DatabaseSetup.Configure(options, file);
            return new AstraDbContext(options.Options);
        }
    }
}
