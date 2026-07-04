using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>Runs capped, cancellable searches over the file index by name and structured filters.</summary>
public interface ISearchService
{
    Task<SearchResults> SearchAsync(SearchCriteria criteria, CancellationToken ct);
}
