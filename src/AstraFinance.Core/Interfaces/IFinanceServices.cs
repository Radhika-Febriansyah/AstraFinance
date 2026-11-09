using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;

namespace AstraFinance.Core.Interfaces;

/// <summary>Accounts, balances and documented balance adjustments.</summary>
public interface IAccountService
{
    Task<Account> CreateAsync(AccountDraft draft, CancellationToken cancellationToken = default);

    Task<Account> UpdateAsync(Guid id, AccountUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Deactivates or reactivates an account. History is always preserved.</summary>
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<AccountSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountSummary>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<long> GetBalanceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sum of the balances of all accounts, including inactive ones (money never disappears from the total).</summary>
    Task<long> GetTotalBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a documented correction so the account's calculated balance becomes <paramref name="targetBalance"/>.
    /// A reason is mandatory; the opening balance and existing records are never modified.
    /// </summary>
    Task<BalanceAdjustment> AdjustBalanceAsync(Guid accountId, long targetBalance, string reason, DateOnly effectiveDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BalanceAdjustment>> ListAdjustmentsAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<Category>> ListAsync(CategoryKind? kind = null, bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<Category?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Category> CreateAsync(CategoryDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames/restyles a category (the kind never changes). Pass a null <paramref name="name"/> to keep the
    /// current name; renaming a built-in category turns it into a custom name that is no longer translated.
    /// </summary>
    Task<Category> UpdateAsync(Guid id, string? name, string iconKey, string colorHex, CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Inserts any missing built-in categories. Idempotent.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}

/// <summary>The only path through which income, expenses and transfers are written.</summary>
public interface ITransactionService
{
    Task<TransactionSaveResult> CreateAsync(TransactionDraft draft, CancellationToken cancellationToken = default);

    Task<TransactionSaveResult> UpdateAsync(TransactionDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a transaction (and a transfer's fee). Deleted records leave balances, budgets and reports.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Restores a soft-deleted transaction (and a transfer's fee).</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active fee expense linked to a transfer, if any.</summary>
    Task<Transaction?> GetFeeAsync(Guid transferId, CancellationToken cancellationToken = default);

    /// <summary>Gets a transaction, including deleted ones.</summary>
    Task<Transaction?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<Transaction>> QueryAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    /// <summary>Count, income and expense totals for all transactions matching the filters (paging ignored).</summary>
    Task<TransactionSummary> SummarizeAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    Task<IncomeExpenseTotals> GetIncomeExpenseTotalsAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>Expense totals per category for budgets (Expense transactions only).</summary>
    Task<IReadOnlyDictionary<Guid, long>> GetExpenseTotalsByCategoryAsync(DateRange range, CancellationToken cancellationToken = default);
}

/// <summary>Key/value settings stored in the database.</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<RegionalFormat> GetRegionalFormatAsync(CancellationToken cancellationToken = default);

    Task SetRegionalFormatAsync(RegionalFormat format, CancellationToken cancellationToken = default);
}

/// <summary>Well-known setting keys.</summary>
public static class SettingKeys
{
    public const string BaseCurrency = "Regional.BaseCurrency";
    public const string NumberFormat = "Regional.NumberFormat";
    public const string DateFormat = "Regional.DateFormat";
    public const string BudgetWarningThreshold = "Budgets.WarningThresholdPercent";
}

/// <summary>Outcome of opening the database at startup.</summary>
/// <param name="CreatedNew">True when the database file did not exist.</param>
/// <param name="AppliedMigrations">Migrations applied during this startup.</param>
/// <param name="PreMigrationBackupPath">Safety copy taken before migrating an existing database.</param>
public sealed record DatabaseInitResult(bool CreatedNew, IReadOnlyList<string> AppliedMigrations, string? PreMigrationBackupPath);

public interface IDatabaseInitializer
{
    Task<DatabaseInitResult> InitializeAsync(CancellationToken cancellationToken = default);
}
