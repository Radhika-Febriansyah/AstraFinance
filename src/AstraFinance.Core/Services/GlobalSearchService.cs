using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;

namespace AstraFinance.Core.Services;

/// <summary>Fans a query out to all registered <see cref="ISearchProvider"/>s and groups the results.</summary>
public sealed class GlobalSearchService : IGlobalSearchService
{
    public const int MaxResultsPerProvider = 8;

    private readonly IReadOnlyList<ISearchProvider> _providers;

    public GlobalSearchService(IEnumerable<ISearchProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
    }

    public int MinimumQueryLength => 1;

    public async Task<IReadOnlyList<SearchResultGroup>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < MinimumQueryLength)
        {
            return [];
        }

        var tasks = _providers.Select(p => p.SearchAsync(trimmed, MaxResultsPerProvider, cancellationToken));
        var resultSets = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return resultSets
            .SelectMany(r => r)
            .GroupBy(r => r.Category)
            .OrderBy(g => g.Key)
            .Select(g => new SearchResultGroup(
                g.Key,
                g.OrderByDescending(r => r.Score)
                 .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                 .ToList()))
            .ToList();
    }
}
