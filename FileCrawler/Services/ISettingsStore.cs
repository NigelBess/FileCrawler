using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>Loads and persists <see cref="AppSettings"/> on the local machine.</summary>
public interface ISettingsStore
{
    /// <summary>Loads the saved settings, or the defaults when none have been saved.</summary>
    AppSettings Load();

    /// <summary>Persists <paramref name="settings"/>.</summary>
    void Save(AppSettings settings);
}

/// <summary>Persists settings via <see cref="UserPersistentData"/> (%LOCALAPPDATA%\FileCrawler\settings.json).</summary>
public sealed class SettingsStore : ISettingsStore
{
    private const string Key = "appSettings";

    private readonly UserPersistentData _data = new("settings");

    public AppSettings Load() => _data.TryLoad<AppSettings>(Key, out var settings) ? settings : new AppSettings();

    public void Save(AppSettings settings) => _data.Save(Key, settings);
}
