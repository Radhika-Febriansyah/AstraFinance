using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Savings goals funded by allocation (earmarking). Allocations are recorded as signed history
/// entries and never touch account balances. Every allocation is validated inside a database
/// transaction against the source account's current unallocated balance.
/// </summary>
public sealed class SavingsGoalService : ISavingsGoalService
{
    public const int MaxNameLength = 100;
    public const int MaxNoteLength = 500;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IClock _clock;

    public SavingsGoalService(IDbContextFactory<AstraDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<SavingsGoal> CreateAsync(SavingsGoalDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var now = _clock.UtcNow.UtcDateTime;
            var goal = new SavingsGoal { Id = Guid.NewGuid(), Status = SavingsGoalStatus.Active, CreatedAtUtc = now };
            Apply(goal, draft, now);
            db.SavingsGoals.Add(goal);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return goal;
        }
    }

    public async Task<SavingsGoal> UpdateAsync(Guid id, SavingsGoalDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var goal = await FindGoalAsync(db, id, cancellationToken).ConfigureAwait(false);
            Apply(goal, draft, _clock.UtcNow.UtcDateTime);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return goal;
        }
    }

    public async Task<IReadOnlyList<SavingsGoalSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var goals = await db.SavingsGoals.AsNoTracking()
                .Where(g => includeArchived || g.Status != SavingsGoalStatus.Archived)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var shares = await SharesAsync(db, cancellationToken).ConfigureAwait(false);
            return goals
                .Select(g => Summarize(g, shares))
                .OrderBy(s => s.Goal.Status)
                .ThenBy(s => s.Goal.TargetDate ?? DateOnly.MaxValue)
                .ThenBy(s => s.Goal.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public async Task<SavingsGoalSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var goal = await db.SavingsGoals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
            return goal is null ? null : Summarize(goal, await SharesAsync(db, cancellationToken).ConfigureAwait(false));
        }
    }

    public Task<GoalAllocation> AllocateAsync(Guid goalId, Guid accountId, long amount, DateOnly effectiveDate, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(goalId, accountId, amount, effectiveDate, note, release: false, cancellationToken);

    public Task<GoalAllocation> ReleaseAsync(Guid goalId, Guid accountId, long amount, DateOnly effectiveDate, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(goalId, accountId, amount, effectiveDate, note, release: true, cancellationToken);

    public async Task<IReadOnlyList<GoalAllocation>> GetHistoryAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.GoalAllocations.AsNoTracking()
                .Where(a => a.GoalId == goalId)
                .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetStatusAsync(Guid goalId, SavingsGoalStatus status, CancellationToken cancellationToken = default)
    {
        if (status == SavingsGoalStatus.Archived)
        {
            await ArchiveAsync(goalId, _clock.Today, cancellationToken).ConfigureAwait(false);
            return;
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var goal = await FindGoalAsync(db, goalId, cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow.UtcDateTime;
            goal.Status = status;
            goal.CompletedAtUtc = status == SavingsGoalStatus.Completed ? now : null;
            goal.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ArchiveAsync(Guid goalId, DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var goal = await FindGoalAsync(db, goalId, cancellationToken).ConfigureAwait(false);
                var now = _clock.UtcNow.UtcDateTime;

                // Release everything the goal still holds so the money becomes available again,
                // recording each release in the history.
                var held = await db.GoalAllocations.Where(a => a.GoalId == goalId)
                    .GroupBy(a => a.AccountId)
                    .Select(g => new { AccountId = g.Key, Sum = g.Sum(a => a.Amount) })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var share in held.Where(h => h.Sum > 0))
                {
                    db.GoalAllocations.Add(new GoalAllocation
                    {
                        Id = Guid.NewGuid(),
                        GoalId = goalId,
                        AccountId = share.AccountId,
                        Amount = -share.Sum,
                        Date = effectiveDate,
                        Note = null,
                        CreatedAtUtc = now,
                    });
                }

                goal.Status = SavingsGoalStatus.Archived;
                goal.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<AccountAllocationInfo>> GetAccountAllocationsAsync(CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var balances = await BalanceQueries.ComponentsAsync(db, null, cancellationToken).ConfigureAwait(false);
            var allocated = await AllocatedByAccountAsync(db, cancellationToken).ConfigureAwait(false);
            return balances
                .Select(b => new AccountAllocationInfo(b.Key, BalanceCalculator.Compute(b.Value), allocated.GetValueOrDefault(b.Key)))
                .ToList();
        }
    }

    private static SavingsGoalSummary Summarize(SavingsGoal goal, IReadOnlyList<(Guid GoalId, Guid AccountId, long Sum)> shares)
    {
        var mine = shares.Where(s => s.GoalId == goal.Id && s.Sum != 0)
            .Select(s => new GoalAccountShare(s.AccountId, s.Sum))
            .ToList();
        return new SavingsGoalSummary(goal, MoneyMath.Sum(mine.Select(s => s.Amount)), mine);
    }

    private static async Task<IReadOnlyList<(Guid GoalId, Guid AccountId, long Sum)>> SharesAsync(AstraDbContext db, CancellationToken ct) =>
        (await db.GoalAllocations.AsNoTracking()
            .GroupBy(a => new { a.GoalId, a.AccountId })
            .Select(g => new { g.Key.GoalId, g.Key.AccountId, Sum = g.Sum(a => a.Amount) })
            .ToListAsync(ct).ConfigureAwait(false))
        .Select(x => (x.GoalId, x.AccountId, x.Sum))
        .ToList();

    private static async Task<Dictionary<Guid, long>> AllocatedByAccountAsync(AstraDbContext db, CancellationToken ct) =>
        await db.GoalAllocations.AsNoTracking()
            .GroupBy(a => a.AccountId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Sum, ct).ConfigureAwait(false);

    private static void Validate(SavingsGoalDraft draft)
    {
        var errors = new List<ValidationError>();
        var name = draft.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add(new("Name", ValidationCodes.Required));
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(new("Name", ValidationCodes.TooLong));
        }

        if (draft.TargetAmount <= 0)
        {
            errors.Add(new("TargetAmount", ValidationCodes.AmountMustBePositive));
        }
        else if (draft.TargetAmount > MoneyMath.MaxAmount)
        {
            errors.Add(new("TargetAmount", ValidationCodes.AmountTooLarge));
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }

    private static void Apply(SavingsGoal goal, SavingsGoalDraft draft, DateTime now)
    {
        goal.Name = draft.Name.Trim();
        goal.TargetAmount = draft.TargetAmount;
        goal.TargetDate = draft.TargetDate;
        goal.IconKey = draft.IconKey;
        goal.ColorHex = draft.ColorHex;
        goal.UpdatedAtUtc = now;
    }

    private static async Task<SavingsGoal> FindGoalAsync(AstraDbContext db, Guid id, CancellationToken ct) =>
        await db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false)
        ?? throw new FinanceValidationException("GoalId", ValidationCodes.GoalNotFound);

    private async Task<GoalAllocation> RecordAsync(Guid goalId, Guid accountId, long amount, DateOnly effectiveDate, string? note, bool release, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        if (amount <= 0)
        {
            errors.Add(new("Amount", ValidationCodes.AmountMustBePositive));
        }
        else if (amount > MoneyMath.MaxAmount)
        {
            errors.Add(new("Amount", ValidationCodes.AmountTooLarge));
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > MaxNoteLength)
        {
            errors.Add(new("Note", ValidationCodes.TooLong));
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }

        var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Validate and write in one transaction so concurrent allocations can't overspend an account.
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var goal = await FindGoalAsync(db, goalId, ct).ConfigureAwait(false);
                if (goal.Status == SavingsGoalStatus.Archived)
                {
                    throw new FinanceValidationException("GoalId", ValidationCodes.GoalArchived);
                }

                var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false)
                              ?? throw new FinanceValidationException("AccountId", ValidationCodes.AccountNotFound);

                if (release)
                {
                    var heldHere = await db.GoalAllocations
                        .Where(a => a.GoalId == goalId && a.AccountId == accountId)
                        .SumAsync(a => a.Amount, ct).ConfigureAwait(false);
                    if (!AllocationRules.CanRelease(amount, heldHere))
                    {
                        throw new FinanceValidationException("Amount", ValidationCodes.ReleaseExceedsAllocated);
                    }
                }
                else
                {
                    if (!account.IsActive)
                    {
                        throw new FinanceValidationException("AccountId", ValidationCodes.AccountInactive);
                    }

                    var balance = await BalanceQueries.BalanceAsync(db, accountId, ct).ConfigureAwait(false);
                    var allocatedFromAccount = await db.GoalAllocations
                        .Where(a => a.AccountId == accountId)
                        .SumAsync(a => a.Amount, ct).ConfigureAwait(false);
                    if (!AllocationRules.CanAllocate(amount, balance, allocatedFromAccount))
                    {
                        throw new FinanceValidationException("Amount", ValidationCodes.AllocationExceedsAvailable);
                    }
                }

                var entry = new GoalAllocation
                {
                    Id = Guid.NewGuid(),
                    GoalId = goalId,
                    AccountId = accountId,
                    Amount = release ? -amount : amount,
                    Date = effectiveDate,
                    Note = trimmedNote,
                    CreatedAtUtc = _clock.UtcNow.UtcDateTime,
                };
                db.GoalAllocations.Add(entry);
                goal.UpdatedAtUtc = entry.CreatedAtUtc;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return entry;
            }
        }
    }
}
