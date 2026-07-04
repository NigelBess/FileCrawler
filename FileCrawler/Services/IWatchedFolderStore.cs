using System.Collections.Generic;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>Persists the list of watched-folder paths across runs.</summary>
public interface IWatchedFolderStore
{
    Task<IReadOnlyList<string>> LoadAsync();
    Task SaveAsync(IEnumerable<string> folders);
}
