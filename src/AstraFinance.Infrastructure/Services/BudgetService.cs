using System.Globalization;
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
/// Monthly budgets per expense category (one budget per category). Spending is the sum of
/// non-deleted Expense transactions in the month — income and transfers never count.
/// </summary>
public sealed class BudgetService : IBudgetService
{
    public const int MaxNotesLength = 2000;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;

    public BudgetService(IDbContextFactory<AstraDbContext> factory, ISettingsStore settings, IClock clock)
    {
        _factory = factory;
        _settings = settings;
        _clock = clock;
    }

    public async Task<Budget> CreateAsync(BudgetDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await ValidateAsync(db, draft, null, cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow.UtcDateTime;
            var budget = new Budget { Id = Guid.NewGuid(), CreatedAtUtc = now };
            Apply(budget, draft, now);
            db.Budgets.Add(budget);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return budget;
        }
    }

    public async Task<Budget> UpdateAsync(Guid id, BudgetDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false)
                         ?? throw new FinanceValidationException("Id", ValidationCodes.BudgetNotFound);
            await ValidateAsync(db, draft, id, cancellationToken).ConfigureAwait(false);
            Apply(budget, draft, _clock.UtcNow.UtcDateTime);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return budget;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false)
                         ?? throw new FinanceValidationException("Id", ValidationCodes.BudgetNotFound);
            db.Budgets.Remove(budget);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Budget?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Budgets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Budget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Budgets.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BudgetOverview> GetOverviewAsync(DateOnly anyDayInMonth, CancellationToken cancellationToken = default)
    {
        var month = DateRange.MonthOf(anyDayInMonth);
        var defaultThreshold = await GetDefaultWarningThresholdAsync(cancellationToken).ConfigureAwait(false);

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var budgets = await db.Budgets.AsNoTracking()
                .Where(b => b.IsActive && b.StartDate <= month.End)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var categoryIds = budgets.Select(b => b.CategoryId).ToList();
            var categories = await db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
            var spent = await db.Transactions.AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.Date >= month.Start && t.Date <= month.End
                            && t.CategoryId != null && categoryIds.Contains(t.CategoryId.Value))
                .GroupBy(t => t.CategoryId!.Value)
                .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Amount) })
                .ToDictionaryAsync(x => x.Id, x => x.Sum, cancellationToken).ConfigureAwait(false);

            var items = budgets
                .Select(b => new BudgetStatusItem(
                    b,
                    categories[b.CategoryId],
                    BudgetEvaluator.Evaluate(b.Amount, spent.GetValueOrDefault(b.CategoryId), b.WarningThresholdPercent ?? defaultThreshold)))
                .OrderByDescending(i => i.Evaluation.Status)
                .ThenByDescending(i => i.Evaluation.UsagePercent)
                .ThenBy(i => i.Category.SortOrder)
                .ToList();

            return new BudgetOverview(
                month,
                items,
                MoneyMath.Sum(items.Select(i => i.Evaluation.Limit)),
                MoneyMath.Sum(items.Select(i => i.Evaluation.Spent)));
        }
    }

    public async Task<int> GetDefaultWarningThresholdAsync(CancellationToken cancellationToken = default)
    {
        var text = await _settings.GetAsync(SettingKeys.BudgetWarningThreshold, cancellationToken).ConfigureAwait(false);
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 100
            ? value
            : BudgetEvaluator.DefaultWarningThresholdPercent;
    }

    public Task SetDefaultWarningThresholdAsync(int percent, CancellationToken cancellationToken = default)
    {
        if (percent is < 1 or > 100)
        {
            throw new FinanceValidationException("WarningThresholdPercent", ValidationCodes.ThresholdOutOfRange);
        }

        return _settings.SetAsync(SettingKeys.BudgetWarningThreshold, percent.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    private static void Apply(Budget budget, BudgetDraft draft, DateTime now)
    {
        budget.CategoryId = draft.CategoryId;
        budget.Amount = draft.Amount;
        budget.Period = BudgetPeriod.Monthly;
        budget.StartDate = DateRange.MonthOf(draft.StartMonth).Start;
        budget.WarningThresholdPercent = draft.WarningThresholdPercent;
        budget.Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
        budget.IsActive = true;
        budget.UpdatedAtUtc = now;
    }

    private static async Task ValidateAsync(AstraDbContext db, BudgetDraft draft, Guid? exceptId, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        if (draft.Amount <= 0)
        {
            errors.Add(new("Amount", ValidationCodes.AmountMustBePositive));
        }
        else if (draft.Amount > MoneyMath.MaxAmount)
        {
            errors.Add(new("Amount", ValidationCodes.AmountTooLarge));
        }

        if (draft.WarningThresholdPercent is < 1 or > 100)
        {
            errors.Add(new("WarningThresholdPercent", ValidationCodes.ThresholdOutOfRange));
        }

        if (draft.Notes?.Trim().Length > MaxNotesLength)
        {
            errors.Add(new("Notes", ValidationCodes.TooLong));
        }

        if (draft.StartMonth == default)
        {
            errors.Add(new("StartMonth", ValidationCodes.Required));
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == draft.CategoryId, ct).ConfigureAwait(false);
        if (category is null)
        {
            errors.Add(new("CategoryId", ValidationCodes.CategoryNotFound));
        }
        else if (category.Kind != CategoryKind.Expense)
        {
            // Only Expense transactions count toward budgets.
            errors.Add(new("CategoryId", ValidationCodes.CategoryKindMismatch));
        }
        else if (await db.Budgets.AnyAsync(b => b.CategoryId == draft.CategoryId && b.Id != exceptId, ct).ConfigureAwait(false))
        {
            errors.Add(new("CategoryId", ValidationCodes.BudgetExistsForCategory));
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }
}
