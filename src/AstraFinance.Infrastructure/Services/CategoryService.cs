using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

public sealed class CategoryService : ICategoryService
{
    public const int MaxNameLength = 100;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IClock _clock;

    public CategoryService(IDbContextFactory<AstraDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Category>> ListAsync(CategoryKind? kind = null, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var query = db.Categories.AsNoTracking();
        if (kind is not null)
        {
            query = query.Where(c => c.Kind == kind);
        }

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task<Category?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Category> CreateAsync(CategoryDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = ValidateName(draft.Name);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await EnsureUniqueNameAsync(db, name, draft.Kind, null, cancellationToken);

        var now = _clock.UtcNow.UtcDateTime;
        var maxOrder = await db.Categories.Where(c => c.Kind == draft.Kind).MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? 0;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = draft.Kind,
            IconKey = draft.IconKey,
            ColorHex = draft.ColorHex,
            IsActive = true,
            SortOrder = maxOrder + 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<Category> UpdateAsync(Guid id, string? name, string iconKey, string colorHex, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new FinanceValidationException(nameof(id), ValidationCodes.CategoryNotFound);

        if (name is not null)
        {
            var validName = ValidateName(name);
            await EnsureUniqueNameAsync(db, validName, category.Kind, id, cancellationToken);

            // Renaming a built-in category turns it into a custom name (no longer localized).
            category.Name = validName;
            category.SystemKey = null;
        }

        category.IconKey = iconKey;
        category.ColorHex = colorHex;
        category.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new FinanceValidationException(nameof(id), ValidationCodes.CategoryNotFound);
        if (!isActive && category.SystemKey == DefaultCategories.FeesKey)
        {
            // Transfer fees are recorded in this category; deactivating it would make transfers with fees fail.
            throw new FinanceValidationException(nameof(id), ValidationCodes.FeeCategoryRequired);
        }

        category.IsActive = isActive;
        category.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Categories.Where(c => c.SystemKey != null).Select(c => c.SystemKey!).ToListAsync(cancellationToken);
        var now = _clock.UtcNow.UtcDateTime;
        var order = 0;
        foreach (var def in DefaultCategories.All)
        {
            order++;
            if (existing.Contains(def.Key, StringComparer.Ordinal))
            {
                continue;
            }

            db.Categories.Add(new Category
            {
                Id = Guid.NewGuid(),
                Name = def.Key,
                SystemKey = def.Key,
                Kind = def.Kind,
                IconKey = def.IconKey,
                ColorHex = def.ColorHex,
                IsActive = true,
                SortOrder = order,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new FinanceValidationException("Name", ValidationCodes.Required);
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new FinanceValidationException("Name", ValidationCodes.TooLong);
        }

        return trimmed;
    }

    private static async Task EnsureUniqueNameAsync(AstraDbContext db, string name, CategoryKind kind, Guid? exceptId, CancellationToken ct)
    {
        // The category table is small; compare in memory for full Unicode case-insensitivity
        // (SQLite's upper() only folds ASCII).
        var names = await db.Categories
            .Where(c => c.Kind == kind && c.SystemKey == null && c.Id != exceptId)
            .Select(c => c.Name)
            .ToListAsync(ct);
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FinanceValidationException("Name", ValidationCodes.DuplicateName);
        }
    }
}
