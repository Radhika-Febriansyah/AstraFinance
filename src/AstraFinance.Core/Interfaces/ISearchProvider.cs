using AstraFinance.Core.Models;

namespace AstraFinance.Core.Interfaces;

/// <summary>
/// Supplies results for one area of the application to global search.
/// Each feature module registers its own provider as it is implemented.
/// </summary>
public interface ISearchProvider
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}
