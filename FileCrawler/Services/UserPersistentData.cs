using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace FileCrawler.Services;

/// <summary>
/// Stores and fetches arbitrary key/value data on the user's local machine.<br/>
/// Data is stored at %LOCALAPPDATA%\FileCrawler\{settingsFileNameNoExtension}.json as a JSON
/// object mapping each key to its JSON-serialized value.
/// </summary>
public class UserPersistentData
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    /// <summary>
    /// Stores and fetches data on the user's local machine.<br/>
    /// Data is stored at %LOCALAPPDATA%\FileCrawler\<paramref name="settingsFileNameNoExtension"/>.json
    /// </summary>
    public UserPersistentData(string settingsFileNameNoExtension)
    {
        _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileCrawler", $"{settingsFileNameNoExtension}.json");
    }

    /// <summary>Loads the raw stored string for the given key, if it exists.</summary>
    public bool TryLoad(string key, [NotNullWhen(returnValue: true)] out string? value)
    {
        var settings = Load();
        return settings.TryGetValue(key, out value);
    }

    /// <summary>Loads the value for the given key and deserializes it from JSON, if it exists.</summary>
    public bool TryLoad<T>(string key, [NotNullWhen(returnValue: true)] out T? value)
    {
        if (!TryLoad(key, out var strValue))
        {
            value = default;
            return false;
        }
        try
        {
            value = JsonSerializer.Deserialize<T>(strValue);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Loads all stored keys and their raw (JSON) values.</summary>
    public Dictionary<string, string> Load()
    {
        if (!File.Exists(_settingsFilePath)) return new();
        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>
    /// Stores <paramref name="value"/> (serialized as JSON) under <paramref name="key"/>.<br/>
    /// A null value removes the key.
    /// </summary>
    public void Save<T>(string key, T? value)
    {
        var json = value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);
        Save(key, json);
    }

    /// <summary>
    /// Stores the raw string <paramref name="value"/> under <paramref name="key"/>.<br/>
    /// A null value removes the key.
    /// </summary>
    public void Save(string key, string? value)
    {
        var existingData = Load();
        if (value is null)
            existingData.Remove(key);
        else
            existingData[key] = value;
        SaveAndOverwriteAll(existingData);
    }

    /// <summary>Replaces the entire data store with the given dictionary.</summary>
    public void SaveAndOverwriteAll(Dictionary<string, string> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        var tmp = _settingsFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, SerializerOptions));
        File.Move(tmp, _settingsFilePath, overwrite: true);
    }
}
