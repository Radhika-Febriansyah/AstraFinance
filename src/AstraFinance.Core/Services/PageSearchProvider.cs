using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Core.Services;

/// <summary>Lets users jump to any main page by typing its localized or English name.</summary>
public sealed class PageSearchProvider : ISearchProvider
{
    private readonly ILocalizationService _localization;

    public PageSearchProvider(ILocalizationService localization)
    {
        _localization = localization;
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var results = new List<SearchResult>();

        foreach (var page in PageCatalog.All)
        {
            var title = _localization.GetString(page.TitleKey);
            var description = _localization.GetString(page.DescriptionKey);
            var score = Math.Max(
                SearchMatcher.Score(normalized, title),
                SearchMatcher.Score(normalized, page.Page.ToString()));

            // Description matches rank below title matches.
            score = Math.Max(score, SearchMatcher.Score(normalized, description) / 2);

            if (score > 0)
            {
                results.Add(new SearchResult(
                    SearchCategory.Pages, title, description, page.IconKey, new NavigationTarget(page.Page), score));
            }
        }

        IReadOnlyList<SearchResult> top = results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
        return Task.FromResult(top);
    }
}
