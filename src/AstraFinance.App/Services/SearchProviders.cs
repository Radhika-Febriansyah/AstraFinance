using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;

namespace AstraFinance.App.Services;

/// <summary>Global search over transaction descriptions, notes and exact amounts. Opens the transaction's details.</summary>
public sealed class TransactionSearchProvider : ISearchProvider
{
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;

    public TransactionSearchProvider(
        ITransactionService transactions, ICategoryService categories, IAccountService accounts, IRegionalFormatService format, ILocalizationService localization)
    {
        _transactions = transactions;
        _categories = categories;
        _accounts = accounts;
        _format = format;
        _localization = localization;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        long? amount = MoneyParser.TryParse(query, _format.Current, out var parsed) && parsed > 0 ? parsed : null;
        var page = await _transactions.QueryAsync(
            new TransactionQuery { Search = query, SearchAmount = amount, PageSize = maxResults },
            cancellationToken).ConfigureAwait(true);
        if (page.Items.Count == 0)
        {
            return [];
        }

        var lookups = await FinanceLookups.LoadAsync(_categories, _accounts, _localization).ConfigureAwait(true);
        return page.Items
            .Select(t => new SearchResult(
                SearchCategory.Transactions,
                lookups.Title(t),
                $"{_format.FormatDate(t.Date)} · {_format.FormatMoney(t.Amount)} · {(t.Type == TransactionType.Transfer ? lookups.AccountText(t) : lookups.CategoryName(t.CategoryId))}",
                t.Type == TransactionType.Transfer ? "Icon.Transactions" : lookups.CategoryIcon(t.CategoryId),
                new NavigationTarget(AppPage.Transactions, t.Id),
                SearchMatcher.ContainsScore))
            .ToList();
    }
}

/// <summary>Global search over category names (localized). Opens Transactions filtered by the category.</summary>
public sealed class CategorySearchProvider : ISearchProvider
{
    private readonly ICategoryService _categories;
    private readonly ILocalizationService _localization;

    public CategorySearchProvider(ICategoryService categories, ILocalizationService localization)
    {
        _categories = categories;
        _localization = localization;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var all = await _categories.ListAsync(null, false, cancellationToken).ConfigureAwait(true);
        return all
            .Select(c => (Category: c, Name: DefaultCategories.DisplayName(c, _localization)))
            .Select(x => (x.Category, x.Name, Score: SearchMatcher.Score(normalized, x.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SearchResult(
                SearchCategory.Categories,
                x.Name,
                _localization.GetString(x.Category.Kind == CategoryKind.Income ? "TransactionType.Income" : "TransactionType.Expense"),
                x.Category.IconKey,
                new NavigationTarget(AppPage.Transactions, x.Category.Id),
                x.Score))
            .ToList();
    }
}

/// <summary>Global search over budgets (by category name). Opens the Budgets page.</summary>
public sealed class BudgetSearchProvider : ISearchProvider
{
    private readonly IBudgetService _budgets;
    private readonly ICategoryService _categories;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;

    public BudgetSearchProvider(IBudgetService budgets, ICategoryService categories, IRegionalFormatService format, ILocalizationService localization)
    {
        _budgets = budgets;
        _categories = categories;
        _format = format;
        _localization = localization;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var budgets = await _budgets.ListAsync(cancellationToken).ConfigureAwait(true);
        if (budgets.Count == 0)
        {
            return [];
        }

        var categories = (await _categories.ListAsync(null, true, cancellationToken).ConfigureAwait(true)).ToDictionary(c => c.Id);
        return budgets
            .Where(b => categories.ContainsKey(b.CategoryId))
            .Select(b => (Budget: b, Category: categories[b.CategoryId], Name: DefaultCategories.DisplayName(categories[b.CategoryId], _localization)))
            .Select(x => (x.Budget, x.Category, x.Name, Score: SearchMatcher.Score(normalized, x.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SearchResult(
                SearchCategory.Budgets,
                x.Name,
                _localization.Format("Search.BudgetSubtitle", _format.FormatMoney(x.Budget.Amount)),
                x.Category.IconKey,
                new NavigationTarget(AppPage.Budgets, x.Budget.Id),
                x.Score))
            .ToList();
    }
}

/// <summary>Global search over savings goals. Opens the goal's details.</summary>
public sealed class SavingsGoalSearchProvider : ISearchProvider
{
    private readonly ISavingsGoalService _goals;
    private readonly IRegionalFormatService _format;

    public SavingsGoalSearchProvider(ISavingsGoalService goals, IRegionalFormatService format)
    {
        _goals = goals;
        _format = format;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var goals = await _goals.ListAsync(includeArchived: true, cancellationToken).ConfigureAwait(true);
        return goals
            .Select(g => (Goal: g, Score: SearchMatcher.Score(normalized, g.Goal.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SearchResult(
                SearchCategory.SavingsGoals,
                x.Goal.Goal.Name,
                $"{_format.FormatMoney(x.Goal.Allocated)} / {_format.FormatMoney(x.Goal.Goal.TargetAmount)}",
                x.Goal.Goal.IconKey,
                new NavigationTarget(AppPage.SavingsGoals, x.Goal.Goal.Id),
                x.Score))
            .ToList();
    }
}

/// <summary>Bills and subscriptions by name (paused ones included).</summary>
public sealed class BillSearchProvider : ISearchProvider
{
    private readonly IBillService _bills;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;

    public BillSearchProvider(IBillService bills, IRegionalFormatService format, ILocalizationService localization, IClock clock)
    {
        _bills = bills;
        _format = format;
        _localization = localization;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var bills = await _bills.ListAsync(_clock.Today, includePaused: true, cancellationToken).ConfigureAwait(true);
        return bills
            .Select(b => (Bill: b, Score: SearchMatcher.Score(normalized, b.Bill.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SearchResult(
                SearchCategory.Bills,
                x.Bill.Bill.Name,
                _localization.Format("Search.BillSubtitle", _format.FormatMoney(x.Bill.Bill.Amount), _localization.GetString($"Recurrence.{x.Bill.Bill.Recurrence}"), _format.FormatDate(x.Bill.NextDue)),
                x.Bill.Category?.IconKey ?? "Icon.Bills",
                new NavigationTarget(AppPage.Bills, x.Bill.Bill.Id),
                x.Score))
            .ToList();
    }
}
