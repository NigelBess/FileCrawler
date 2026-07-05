using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>
/// Persists the last search term and filter selections via <see cref="UserPersistentData"/>
/// (stored under %LOCALAPPDATA%\FileCrawler\search-state.json).
/// </summary>
public sealed class SearchStateStore : ISearchStateStore
{
    private const string Key = "searchState";

    private readonly UserPersistentData _data = new("search-state");

    public Task<SearchState?> LoadAsync() =>
        Task.Run(() => _data.TryLoad<SearchState>(Key, out var state) ? state : null);

    public Task SaveAsync(SearchState state) => Task.Run(() => _data.Save(Key, state));
}
