using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;

namespace AstraFinance.Tests.Core;

public class SearchTests
{
    [Theory]
    [InlineData("budgets", "Budgets", SearchMatcher.ExactScore)]
    [InlineData("bud", "Budgets", SearchMatcher.PrefixScore)]
    [InlineData("sub", "Bills & Subscriptions", SearchMatcher.WordPrefixScore)]
    [InlineData("ills", "Bills & Subscriptions", SearchMatcher.ContainsScore)]
    [InlineData("xyz", "Budgets", 0)]
    [InlineData("kafe", "Kafé", SearchMatcher.ExactScore)]
    public void Scores_matches(string query, string candidate, int expected) =>
        Assert.Equal(expected, SearchMatcher.Score(SearchMatcher.Normalize(query), candidate));

    [Fact]
    public async Task Page_search_uses_localized_titles()
    {
        var provider = new PageSearchProvider(new LocalizationService(AppLanguage.Indonesian));
        var results = await provider.SearchAsync("angg", 8, CancellationToken.None);

        Assert.Equal(AppPage.Budgets, results[0].Target.Page);
        Assert.Equal("Anggaran", results[0].Title);
    }

    [Fact]
    public async Task Page_search_also_matches_english_page_names_in_indonesian()
    {
        var provider = new PageSearchProvider(new LocalizationService(AppLanguage.Indonesian));
        var results = await provider.SearchAsync("settings", 8, CancellationToken.None);

        Assert.Equal(AppPage.Settings, results[0].Target.Page);
        Assert.Equal("Pengaturan", results[0].Title);
    }

    [Fact]
    public async Task Global_search_groups_results_by_category_in_display_order()
    {
        var service = new GlobalSearchService(
        [
            new StubProvider(new SearchResult(SearchCategory.Accounts, "Cash", null, "Icon.Accounts", new NavigationTarget(AppPage.Accounts, Guid.NewGuid()), 80)),
            new StubProvider(
                new SearchResult(SearchCategory.Pages, "Accounts", null, "Icon.Accounts", new NavigationTarget(AppPage.Accounts), 60),
                new SearchResult(SearchCategory.Pages, "Analytics", null, "Icon.Analytics", new NavigationTarget(AppPage.Analytics), 90)),
        ]);

        var groups = await service.SearchAsync("a", CancellationToken.None);

        Assert.Equal([SearchCategory.Pages, SearchCategory.Accounts], groups.Select(g => g.Category));
        Assert.Equal(["Analytics", "Accounts"], groups[0].Results.Select(r => r.Title));
    }

    [Fact]
    public async Task Blank_query_returns_nothing_without_calling_providers()
    {
        var stub = new StubProvider();
        var groups = await new GlobalSearchService([stub]).SearchAsync("   ", CancellationToken.None);
        Assert.Empty(groups);
        Assert.Equal(0, stub.Calls);
    }

    private sealed class StubProvider(params SearchResult[] results) : ISearchProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<SearchResult>>(results);
        }
    }
}
