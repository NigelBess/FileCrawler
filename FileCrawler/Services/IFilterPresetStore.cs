using System.Collections.Generic;

namespace FileCrawler.Services;

/// <summary>A named, saved snapshot the user can re-apply with one click. The snapshot (<see cref="FilterState"/>)
/// covers the filter bar and the result sort order.</summary>
public sealed record FilterPreset(string Name, FilterState State);

/// <summary>Loads and persists the user's saved filter presets on the local machine.</summary>
public interface IFilterPresetStore
{
    /// <summary>Loads the saved presets in display order, or an empty list when none have been saved.</summary>
    IReadOnlyList<FilterPreset> Load();

    /// <summary>Persists <paramref name="presets"/>, replacing any previously saved set.</summary>
    void Save(IReadOnlyList<FilterPreset> presets);
}

/// <summary>Persists presets via <see cref="UserPersistentData"/> (%LOCALAPPDATA%\FileCrawler\filter-presets.json).</summary>
public sealed class FilterPresetStore : IFilterPresetStore
{
    private const string Key = "presets";

    private readonly UserPersistentData _data = new("filter-presets");

    public IReadOnlyList<FilterPreset> Load() =>
        _data.TryLoad<List<FilterPreset>>(Key, out var presets) ? presets : new List<FilterPreset>();

    public void Save(IReadOnlyList<FilterPreset> presets) => _data.Save(Key, presets);
}
