using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Single write path for income, expenses and transfers (Quick Add and the full form both use it).
/// Every operation validates first, then writes all affected rows in one SQLite transaction.
/// </summary>
public sealed class TransactionService : ITransactionService
{
    public const int MaxDescriptionLength = 200;
    public const int MaxNotesLength = 2000;
    public const int MaxPageSize = 500;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IClock _clock;

    public TransactionService(IDbContextFactory<AstraDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<TransactionSaveResult> CreateAsync(TransactionDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                // Duplicate submission (same draft id): return what was already saved, write nothing.
                var already = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == draft.Id, cancellationToken).ConfigureAwait(false);
                if (already is not null)
                {
                    var existingFee = await FindFeeAsync(db, already.Id, tracked: false, cancellationToken).ConfigureAwait(false);
                    return new TransactionSaveResult(already, existingFee, WasDuplicate: true);
                }

                var valid = await ValidateAsync(db, draft, existing: null, cancellationToken).ConfigureAwait(false);
                var now = _clock.UtcNow.UtcDateTime;

                var transaction = new Transaction { Id = draft.Id, CreatedAtUtc = now };
                Apply(transaction, valid, now);
                db.Transactions.Add(transaction);

                Transaction? fee = null;
                if (valid.Fee > 0)
                {
                    fee = NewFee(transaction, valid, now);
                    db.Transactions.Add(fee);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new TransactionSaveResult(transaction, fee, WasDuplicate: false);
            }
        }
    }

    public async Task<TransactionSaveResult> UpdateAsync(TransactionDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == draft.Id, cancellationToken).ConfigureAwait(false)
                                  ?? throw new FinanceValidationException("Id", ValidationCodes.TransactionNotFound);
                if (transaction.ParentTransactionId is not null)
                {
                    throw new FinanceValidationException("Id", ValidationCodes.FeeEditedThroughTransfer);
                }

                var valid = await ValidateAsync(db, draft, transaction, cancellationToken).ConfigureAwait(false);
                var now = _clock.UtcNow.UtcDateTime;
                Apply(transaction, valid, now);

                var fee = await FindFeeAsync(db, transaction.Id, tracked: true, cancellationToken).ConfigureAwait(false);
                if (valid.Fee > 0)
                {
                    if (fee is null)
                    {
                        fee = NewFee(transaction, valid, now);
                        db.Transactions.Add(fee);
                    }
                    else
                    {
                        fee.Amount = valid.Fee;
                        fee.AccountId = transaction.AccountId;
                        fee.CategoryId = valid.FeeCategoryId;
                        fee.Date = transaction.Date;
                        fee.Description = transaction.Description;
                        fee.UpdatedAtUtc = now;
                    }
                }
                else if (fee is not null)
                {
                    // Fee removed (or the record is no longer a transfer): keep it recoverable.
                    fee.IsDeleted = true;
                    fee.DeletedAtUtc = now;
                    fee.UpdatedAtUtc = now;
                    fee = null;
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new TransactionSaveResult(transaction, fee, WasDuplicate: false);
            }
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false)
                                  ?? throw new FinanceValidationException("Id", ValidationCodes.TransactionNotFound);
                if (transaction.ParentTransactionId is not null)
                {
                    throw new FinanceValidationException("Id", ValidationCodes.FeeEditedThroughTransfer);
                }

                // The same timestamp marks the fee as deleted together with its transfer (used by restore).
                var now = _clock.UtcNow.UtcDateTime;
                MarkDeleted(transaction, now);
                var fee = await FindFeeAsync(db, id, tracked: true, cancellationToken).ConfigureAwait(false);
                if (fee is not null)
                {
                    MarkDeleted(fee, now);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var transaction = await db.Transactions.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false)
                                  ?? throw new FinanceValidationException("Id", ValidationCodes.TransactionNotFound);
                if (transaction.ParentTransactionId is not null)
                {
                    throw new FinanceValidationException("Id", ValidationCodes.FeeEditedThroughTransfer);
                }

                if (!transaction.IsDeleted)
                {
                    return;
                }

                var deletedAt = transaction.DeletedAtUtc;
                var now = _clock.UtcNow.UtcDateTime;
                MarkRestored(transaction, now);

                // Restore only the fee that was deleted together with the transfer, not one removed earlier by an edit.
                var fees = await db.Transactions.IgnoreQueryFilters()
                    .Where(t => t.ParentTransactionId == id && t.IsDeleted && t.DeletedAtUtc == deletedAt)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var fee in fees)
                {
                    MarkRestored(fee, now);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<Transaction?> GetFeeAsync(Guid transferId, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await FindFeeAsync(db, transferId, tracked: false, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Transaction?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<PagedResult<Transaction>> QueryAsync(TransactionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var q = ApplyFilters(db, query);
            var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);
            q = query.Sort switch
            {
                TransactionSort.DateAscending => q.OrderBy(t => t.Date).ThenBy(t => t.CreatedAtUtc),
                TransactionSort.AmountDescending => q.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Date),
                TransactionSort.AmountAscending => q.OrderBy(t => t.Amount).ThenByDescending(t => t.Date),
                _ => q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAtUtc),
            };

            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            return new PagedResult<Transaction>(items, total, page, pageSize);
        }
    }

    public async Task<TransactionSummary> SummarizeAsync(TransactionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var groups = await ApplyFilters(db, query)
                .GroupBy(t => t.Type)
                .Select(g => new { Type = g.Key, Count = g.Count(), Sum = g.Sum(t => t.Amount) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new TransactionSummary(
                groups.Sum(g => g.Count),
                groups.FirstOrDefault(g => g.Type == TransactionType.Income)?.Sum ?? 0,
                groups.FirstOrDefault(g => g.Type == TransactionType.Expense)?.Sum ?? 0);
        }
    }

    public async Task<IncomeExpenseTotals> GetIncomeExpenseTotalsAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var totals = await db.Transactions.AsNoTracking()
                .Where(t => t.Date >= range.Start && t.Date <= range.End
                            && (t.Type == TransactionType.Income || t.Type == TransactionType.Expense))
                .GroupBy(t => t.Type)
                .Select(g => new { Type = g.Key, Sum = g.Sum(t => t.Amount) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new IncomeExpenseTotals(
                totals.FirstOrDefault(t => t.Type == TransactionType.Income)?.Sum ?? 0,
                totals.FirstOrDefault(t => t.Type == TransactionType.Expense)?.Sum ?? 0);
        }
    }

    public async Task<IReadOnlyList<MonthlyTotals>> GetMonthlyTotalsAsync(DateOnly lastMonth, int months, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(months, 120);
        var end = DateRange.MonthOf(lastMonth).End;
        var start = DateRange.MonthOf(lastMonth.AddMonths(-(months - 1))).Start;

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Project only the needed columns and aggregate in memory (exact long sums, no SQL date functions).
            var rows = await db.Transactions.AsNoTracking()
                .Where(t => t.Date >= start && t.Date <= end
                            && (t.Type == TransactionType.Income || t.Type == TransactionType.Expense))
                .Select(t => new { t.Date, t.Type, t.Amount })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var result = new List<MonthlyTotals>(months);
            for (var i = 0; i < months; i++)
            {
                var month = start.AddMonths(i);
                var inMonth = rows.Where(r => r.Date.Year == month.Year && r.Date.Month == month.Month).ToList();
                result.Add(new MonthlyTotals(
                    month.Year,
                    month.Month,
                    MoneyMath.Sum(inMonth.Where(r => r.Type == TransactionType.Income).Select(r => r.Amount)),
                    MoneyMath.Sum(inMonth.Where(r => r.Type == TransactionType.Expense).Select(r => r.Amount))));
            }

            return result;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetExpenseTotalsByCategoryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Transactions.AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.Date >= range.Start && t.Date <= range.End)
                .GroupBy(t => t.CategoryId!.Value)
                .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Amount) })
                .ToDictionaryAsync(x => x.Id, x => x.Sum, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The single implementation of transaction filtering (list, summary and export use it).</summary>
    private static IQueryable<Transaction> ApplyFilters(AstraDbContext db, TransactionQuery query)
    {
        var q = query.DeletedOnly
            ? db.Transactions.IgnoreQueryFilters().AsNoTracking().Where(t => t.IsDeleted)
            : db.Transactions.AsNoTracking();

        if (query.From is { } from)
        {
            q = q.Where(t => t.Date >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(t => t.Date <= to);
        }

        if (query.Types is { Count: > 0 } types)
        {
            q = q.Where(t => types.Contains(t.Type));
        }

        if (query.CategoryIds is { Count: > 0 } categoryIds)
        {
            q = q.Where(t => t.CategoryId != null && categoryIds.Contains(t.CategoryId.Value));
        }

        if (query.AccountIds is { Count: > 0 } accountIds)
        {
            q = q.Where(t => accountIds.Contains(t.AccountId)
                             || (t.DestinationAccountId != null && accountIds.Contains(t.DestinationAccountId.Value)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            var searchCategories = query.SearchCategoryIds ?? [];
            var searchAccounts = query.SearchAccountIds ?? [];
            var amount = query.SearchAmount ?? -1;
            q = q.Where(t => EF.Functions.Like(t.Description, pattern, "\\")
                             || (t.Notes != null && EF.Functions.Like(t.Notes, pattern, "\\"))
                             || (t.CategoryId != null && searchCategories.Contains(t.CategoryId.Value))
                             || searchAccounts.Contains(t.AccountId)
                             || (t.DestinationAccountId != null && searchAccounts.Contains(t.DestinationAccountId.Value))
                             || t.Amount == amount);
        }

        return q;
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("%", "\\%", StringComparison.Ordinal)
         .Replace("_", "\\_", StringComparison.Ordinal);

    private static void Apply(Transaction t, ValidDraft v, DateTime now)
    {
        t.Type = v.Type;
        t.Amount = v.Amount;
        t.Description = v.Description;
        t.Notes = v.Notes;
        t.CategoryId = v.CategoryId;
        t.AccountId = v.AccountId;
        t.DestinationAccountId = v.DestinationAccountId;
        t.Date = v.Date;
        t.UpdatedAtUtc = now;
    }

    private static Transaction NewFee(Transaction transfer, ValidDraft v, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Type = TransactionType.Expense,
        Amount = v.Fee,
        Description = transfer.Description,
        CategoryId = v.FeeCategoryId,
        AccountId = transfer.AccountId,
        Date = transfer.Date,
        ParentTransactionId = transfer.Id,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    private static void MarkDeleted(Transaction t, DateTime now)
    {
        t.IsDeleted = true;
        t.DeletedAtUtc = now;
        t.UpdatedAtUtc = now;
    }

    private static void MarkRestored(Transaction t, DateTime now)
    {
        t.IsDeleted = false;
        t.DeletedAtUtc = null;
        t.UpdatedAtUtc = now;
    }

    private static async Task<Transaction?> FindFeeAsync(AstraDbContext db, Guid parentId, bool tracked, CancellationToken ct)
    {
        var source = tracked ? db.Transactions : db.Transactions.AsNoTracking();
        return await source.FirstOrDefaultAsync(t => t.ParentTransactionId == parentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a draft against the database. Newly referenced accounts/categories must be active;
    /// references the transaction already had may be inactive (editing history stays possible).
    /// </summary>
    private static async Task<ValidDraft> ValidateAsync(AstraDbContext db, TransactionDraft d, Transaction? existing, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        var isTransfer = d.Type == TransactionType.Transfer;

        if (!Enum.IsDefined(d.Type))
        {
            errors.Add(new("Type", ValidationCodes.Required));
        }

        if (d.Amount <= 0)
        {
            errors.Add(new("Amount", ValidationCodes.AmountMustBePositive));
        }
        else if (d.Amount > MoneyMath.MaxAmount)
        {
            errors.Add(new("Amount", ValidationCodes.AmountTooLarge));
        }

        if (d.TransferFee < 0)
        {
            errors.Add(new("TransferFee", ValidationCodes.AmountMustBePositive));
        }
        else if (d.TransferFee > MoneyMath.MaxAmount)
        {
            errors.Add(new("TransferFee", ValidationCodes.AmountTooLarge));
        }
        else if (d.TransferFee > 0 && !isTransfer)
        {
            errors.Add(new("TransferFee", ValidationCodes.FeeOnlyForTransfer));
        }

        if (d.Date == default)
        {
            errors.Add(new("Date", ValidationCodes.Required));
        }

        var description = d.Description?.Trim() ?? string.Empty;
        if (description.Length > MaxDescriptionLength)
        {
            errors.Add(new("Description", ValidationCodes.TooLong));
        }

        var notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();
        if (notes?.Length > MaxNotesLength)
        {
            errors.Add(new("Notes", ValidationCodes.TooLong));
        }

        var previouslyReferenced = existing is null
            ? new HashSet<Guid>()
            : new HashSet<Guid>(new[] { existing.AccountId, existing.DestinationAccountId ?? Guid.Empty, existing.CategoryId ?? Guid.Empty });

        await CheckAccountAsync(db, d.AccountId, "AccountId", previouslyReferenced, errors, ct).ConfigureAwait(false);

        Guid? feeCategoryId = null;
        if (isTransfer)
        {
            if (d.DestinationAccountId is null || d.DestinationAccountId == Guid.Empty)
            {
                errors.Add(new("DestinationAccountId", ValidationCodes.TransferDestinationRequired));
            }
            else if (d.DestinationAccountId == d.AccountId)
            {
                errors.Add(new("DestinationAccountId", ValidationCodes.TransferSameAccount));
            }
            else
            {
                await CheckAccountAsync(db, d.DestinationAccountId.Value, "DestinationAccountId", previouslyReferenced, errors, ct).ConfigureAwait(false);
            }

            if (d.CategoryId is not null)
            {
                errors.Add(new("CategoryId", ValidationCodes.CategoryNotAllowedForTransfer));
            }

            if (d.TransferFee > 0)
            {
                feeCategoryId = await ResolveFeeCategoryAsync(db, d.FeeCategoryId, errors, ct).ConfigureAwait(false);
            }
        }
        else
        {
            if (d.DestinationAccountId is not null)
            {
                errors.Add(new("DestinationAccountId", ValidationCodes.DestinationOnlyForTransfer));
            }

            if (d.CategoryId is null || d.CategoryId == Guid.Empty)
            {
                errors.Add(new("CategoryId", ValidationCodes.Required));
            }
            else
            {
                var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == d.CategoryId, ct).ConfigureAwait(false);
                var expectedKind = d.Type == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;
                if (category is null)
                {
                    errors.Add(new("CategoryId", ValidationCodes.CategoryNotFound));
                }
                else if (category.Kind != expectedKind)
                {
                    errors.Add(new("CategoryId", ValidationCodes.CategoryKindMismatch));
                }
                else if (!category.IsActive && !previouslyReferenced.Contains(category.Id))
                {
                    errors.Add(new("CategoryId", ValidationCodes.CategoryInactive));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }

        return new ValidDraft(
            d.Type,
            d.Amount,
            description,
            notes,
            isTransfer ? null : d.CategoryId,
            d.AccountId,
            isTransfer ? d.DestinationAccountId : null,
            d.Date,
            isTransfer ? d.TransferFee : 0,
            feeCategoryId);
    }

    private static async Task CheckAccountAsync(AstraDbContext db, Guid accountId, string field, HashSet<Guid> previouslyReferenced, List<ValidationError> errors, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
        if (account is null)
        {
            errors.Add(new(field, ValidationCodes.AccountNotFound));
        }
        else if (!account.IsActive && !previouslyReferenced.Contains(accountId))
        {
            errors.Add(new(field, ValidationCodes.AccountInactive));
        }
    }

    private static async Task<Guid?> ResolveFeeCategoryAsync(AstraDbContext db, Guid? requested, List<ValidationError> errors, CancellationToken ct)
    {
        var category = requested is { } id
            ? await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            : await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.SystemKey == DefaultCategories.FeesKey, ct).ConfigureAwait(false);

        if (category is null)
        {
            errors.Add(new("FeeCategoryId", ValidationCodes.CategoryNotFound));
            return null;
        }

        if (category.Kind != CategoryKind.Expense)
        {
            errors.Add(new("FeeCategoryId", ValidationCodes.CategoryKindMismatch));
            return null;
        }

        return category.Id;
    }

    private sealed record ValidDraft(
        TransactionType Type,
        long Amount,
        string Description,
        string? Notes,
        Guid? CategoryId,
        Guid AccountId,
        Guid? DestinationAccountId,
        DateOnly Date,
        long Fee,
        Guid? FeeCategoryId);
}
