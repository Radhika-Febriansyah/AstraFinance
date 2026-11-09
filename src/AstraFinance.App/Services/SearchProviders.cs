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
