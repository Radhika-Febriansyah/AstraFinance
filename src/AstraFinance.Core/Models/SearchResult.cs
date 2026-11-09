using AstraFinance.Core.Navigation;

namespace AstraFinance.Core.Models;

/// <summary>Result categories, in the order they are displayed.</summary>
public enum SearchCategory
{
    Pages = 0,
    Transactions = 1,
    Accounts = 2,
    Categories = 3,
    Budgets = 4,
    SavingsGoals = 5,
    Bills = 6,
}

/// <summary>Where a search result navigates to. <paramref name="EntityId"/> is null for page-level targets.</summary>
public sealed record NavigationTarget(AppPage Page, Guid? EntityId = null);

/// <summary>A single global-search hit.</summary>
public sealed record SearchResult(
    SearchCategory Category,
    string Title,
    string? Subtitle,
    string IconKey,
    NavigationTarget Target,
    int Score);

/// <summary>Search results for one category.</summary>
public sealed record SearchResultGroup(SearchCategory Category, IReadOnlyList<SearchResult> Results);
