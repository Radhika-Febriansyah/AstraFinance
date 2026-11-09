using AstraFinance.Core.Enums;
using AstraFinance.Core.Money;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Backup;
using AstraFinance.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Tests.Infrastructure;

public class DatabaseTests
{
    [Fact]
    public async Task First_start_creates_migrated_database_with_default_categories()
    {
        await using var ctx = await FinanceTestContext.CreateAsync(initialize: false);
        var result = await ctx.Initializer.InitializeAsync();

        Assert.True(result.CreatedNew);
        Assert.NotEmpty(result.AppliedMigrations);
        Assert.Null(result.PreMigrationBackupPath);
        Assert.True(File.Exists(ctx.Paths.DatabaseFile));

        var categories = await ctx.Categories.ListAsync(includeInactive: true);
        Assert.Equal(DefaultCategories.All.Count, categories.Count);

        await using var db = await ctx.Factory.CreateDbContextAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Initialization_is_idempotent()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var second = await ctx.Initializer.InitializeAsync();

        Assert.False(second.CreatedNew);
        Assert.Empty(second.AppliedMigrations);
        Assert.Equal(DefaultCategories.All.Count, (await ctx.Categories.ListAsync(includeInactive: true)).Count);
    }

    [Fact]
    public async Task Existing_database_gets_a_verified_safety_copy_before_migration()
    {
        await using var ctx = await FinanceTestContext.CreateAsync(initialize: false);
        ctx.Paths.EnsureCreated();
        await using (var conn = new SqliteConnection($"Data Source={ctx.Paths.DatabaseFile};Pooling=False"))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Legacy (Id INTEGER); INSERT INTO Legacy VALUES (42);";
            await cmd.ExecuteNonQueryAsync();
        }

        var result = await ctx.Initializer.InitializeAsync();

        Assert.False(result.CreatedNew);
        Assert.NotNull(result.PreMigrationBackupPath);
        Assert.True(SqliteBackup.IntegrityCheck(result.PreMigrationBackupPath!));
        await using var copy = new SqliteConnection($"Data Source={result.PreMigrationBackupPath};Mode=ReadOnly;Pooling=False");
        await copy.OpenAsync();
        var check = copy.CreateCommand();
        check.CommandText = "SELECT Id FROM Legacy";
        Assert.Equal(42L, await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Database_from_a_newer_app_version_is_refused_and_untouched()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await using (var db = await ctx.Factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231000000_FromTheFuture', '99.0')");
        }

        await Assert.ThrowsAsync<DatabaseNewerThanApplicationException>(() => ctx.Initializer.InitializeAsync());
    }

    [Fact]
    public async Task Database_constraints_reject_invalid_financial_rows()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        var food = await ctx.CategoryAsync("Food");
        await using var db = await ctx.Factory.CreateDbContextAsync();
        var accountId = account.Id.ToString().ToUpperInvariant();

        // Sanity: a valid row is accepted, so the failures below are caused by the constraints under test.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Transactions (Id, Type, Amount, Description, CategoryId, AccountId, DestinationAccountId, Date, IsDeleted, CreatedAtUtc, UpdatedAtUtc) " +
            $"VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', 1, 5, '', '{food.Id.ToString().ToUpperInvariant()}', '{accountId}', NULL, '2026-11-09', 0, '2026-11-09', '2026-11-09')");

        async Task Insert(string type, long amount, string? category, string? destination)
        {
            var cat = category is null ? "NULL" : $"'{category}'";
            var dest = destination is null ? "NULL" : $"'{destination}'";
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Transactions (Id, Type, Amount, Description, CategoryId, AccountId, DestinationAccountId, Date, IsDeleted, CreatedAtUtc, UpdatedAtUtc) " +
                $"VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', {type}, {amount}, '', {cat}, '{accountId}', {dest}, '2026-11-09', 0, '2026-11-09', '2026-11-09')");
        }

        // Negative amount
        await Assert.ThrowsAsync<SqliteException>(() => Insert("1", -5, food.Id.ToString().ToUpperInvariant(), null));

        // Transfer to the same account
        await Assert.ThrowsAsync<SqliteException>(() => Insert("2", 5, null, account.Id.ToString().ToUpperInvariant()));

        // Expense without category
        await Assert.ThrowsAsync<SqliteException>(() => Insert("1", 5, null, null));

        // Unknown account (foreign key)
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO BalanceAdjustments (Id, AccountId, Amount, BalanceBefore, Reason, Date, IsDeleted, CreatedAtUtc, UpdatedAtUtc) " +
            $"VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 5, 0, 'x', '2026-11-09', 0, '2026-11-09', '2026-11-09')"));
    }

    [Fact]
    public async Task Accounts_with_history_cannot_be_hard_deleted()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync();
        await ctx.IncomeAsync(account, 1_000);

        await using var db = await ctx.Factory.CreateDbContextAsync();
        await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlRawAsync($"DELETE FROM Accounts WHERE Id = '{account.Id.ToString().ToUpperInvariant()}'"));
    }

    [Fact]
    public async Task Online_backup_includes_latest_committed_writes()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var account = await ctx.NewAccountAsync(opening: 100);
        await ctx.IncomeAsync(account, 900);

        var target = Path.Combine(ctx.Paths.BackupsDirectory, "manual.db");
        await SqliteBackup.BackupAsync(ctx.Paths.DatabaseFile, target);

        Assert.True(SqliteBackup.IntegrityCheck(target));
        await using var copy = new SqliteConnection($"Data Source={target};Mode=ReadOnly;Pooling=False");
        await copy.OpenAsync();
        var cmd = copy.CreateCommand();
        cmd.CommandText = "SELECT SUM(Amount) FROM Transactions WHERE IsDeleted = 0";
        Assert.Equal(900L, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Regional_settings_default_to_rupiah_and_persist()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        Assert.Equal(RegionalFormat.Default, await ctx.Settings.GetRegionalFormatAsync());

        var custom = new RegionalFormat(CurrencyInfo.Usd, NumberFormatStyle.CommaGroupDotDecimal, DateFormatStyle.YearMonthDay);
        await ctx.Settings.SetRegionalFormatAsync(custom);

        Assert.Equal(custom, await ctx.Settings.GetRegionalFormatAsync());
    }
}
