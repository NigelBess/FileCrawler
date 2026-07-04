using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>Runs capped, cancellable name searches over the file index.</summary>
public interface ISearchService
{
    Task<SearchResults> SearchAsync(string query, CancellationToken ct);
}
