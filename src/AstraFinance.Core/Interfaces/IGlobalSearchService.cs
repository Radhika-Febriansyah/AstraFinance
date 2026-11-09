using AstraFinance.Core.Models;

namespace AstraFinance.Core.Interfaces;

public interface IGlobalSearchService
{
    /// <summary>Minimum trimmed query length that triggers a search.</summary>
    int MinimumQueryLength { get; }

    Task<IReadOnlyList<SearchResultGroup>> SearchAsync(string query, CancellationToken cancellationToken);
}
