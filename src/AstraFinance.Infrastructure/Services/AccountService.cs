using AstraFinance.Core.Entities;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

public sealed class AccountService : IAccountService
{
    public const int MaxNameLength = 100;
    public const int MaxReasonLength = 500;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IClock _clock;

    public AccountService(IDbContextFactory<AstraDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<Account> CreateAsync(AccountDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = ValidateName(draft.Name);
        if (Math.Abs(draft.OpeningBalance) > MoneyMath.MaxAmount)
        {
            throw new FinanceValidationException("OpeningBalance", ValidationCodes.AmountTooLarge);
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var now = _clock.UtcNow.UtcDateTime;
            var maxOrder = await db.Accounts.MaxAsync(a => (int?)a.SortOrder, cancellationToken).ConfigureAwait(false) ?? 0;
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = draft.Type,
                OpeningBalance = draft.OpeningBalance,
                IconKey = draft.IconKey,
                ColorHex = draft.ColorHex,
                IsActive = true,
                SortOrder = maxOrder + 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return account;
        }
    }

    public async Task<Account> UpdateAsync(Guid id, AccountUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var name = ValidateName(update.Name);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var account = await FindAsync(db, id, cancellationToken).ConfigureAwait(false);
            account.Name = name;
            account.Type = update.Type;
            account.IconKey = update.IconKey;
            account.ColorHex = update.ColorHex;
            account.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return account;
        }
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var account = await FindAsync(db, id, cancellationToken).ConfigureAwait(false);
            account.IsActive = isActive;
            account.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AccountSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                return null;
            }

            var components = (await BalanceQueries.ComponentsAsync(db, [id], cancellationToken).ConfigureAwait(false))[id];
            return new AccountSummary(account, BalanceCalculator.Compute(components), components);
        }
    }

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var query = db.Accounts.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(a => a.IsActive);
            }

            var accounts = await query.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
            var components = await BalanceQueries.ComponentsAsync(db, accounts.Select(a => a.Id).ToList(), cancellationToken).ConfigureAwait(false);
            return accounts
                .Select(a => new AccountSummary(a, BalanceCalculator.Compute(components[a.Id]), components[a.Id]))
                .ToList();
        }
    }

    public async Task<long> GetBalanceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await FindAsync(db, id, cancellationToken).ConfigureAwait(false);
            return await BalanceQueries.BalanceAsync(db, id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<long> GetTotalBalanceAsync(CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var all = await BalanceQueries.ComponentsAsync(db, null, cancellationToken).ConfigureAwait(false);
            return MoneyMath.Sum(all.Values.Select(BalanceCalculator.Compute));
        }
    }

    public async Task<BalanceAdjustment> AdjustBalanceAsync(Guid accountId, long targetBalance, string reason, DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
        {
            throw new FinanceValidationException("Reason", ValidationCodes.Required);
        }

        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new FinanceValidationException("Reason", ValidationCodes.TooLong);
        }

        if (Math.Abs(targetBalance) > MoneyMath.MaxAmount)
        {
            throw new FinanceValidationException("TargetBalance", ValidationCodes.AmountTooLarge);
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Read-then-write inside one database transaction so the delta is based on a consistent balance.
            var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var account = await FindAsync(db, accountId, cancellationToken).ConfigureAwait(false);
                if (!account.IsActive)
                {
                    throw new FinanceValidationException("AccountId", ValidationCodes.AccountInactive);
                }

                var current = await BalanceQueries.BalanceAsync(db, accountId, cancellationToken).ConfigureAwait(false);
                long delta;
                try
                {
                    delta = MoneyMath.Subtract(targetBalance, current);
                }
                catch (OverflowException)
                {
                    throw new FinanceValidationException("TargetBalance", ValidationCodes.Overflow);
                }

                if (delta == 0)
                {
                    throw new FinanceValidationException("TargetBalance", ValidationCodes.BalanceUnchanged);
                }

                var now = _clock.UtcNow.UtcDateTime;
                var adjustment = new BalanceAdjustment
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Amount = delta,
                    BalanceBefore = current,
                    Reason = trimmedReason,
                    Date = effectiveDate,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.BalanceAdjustments.Add(adjustment);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return adjustment;
            }
        }
    }

    public async Task<IReadOnlyList<BalanceAdjustment>> ListAdjustmentsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.BalanceAdjustments.AsNoTracking()
                .Where(a => a.AccountId == accountId)
                .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new FinanceValidationException("Name", ValidationCodes.Required);
        }

        return trimmed.Length > MaxNameLength
            ? throw new FinanceValidationException("Name", ValidationCodes.TooLong)
            : trimmed;
    }

    private static async Task<Account> FindAsync(AstraDbContext db, Guid id, CancellationToken ct) =>
        await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false)
        ?? throw new FinanceValidationException("AccountId", ValidationCodes.AccountNotFound);
}
