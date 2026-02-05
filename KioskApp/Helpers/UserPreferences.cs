using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KioskApp.Helpers;

/// <summary>
/// User preferences that persist between sessions.
/// Stored in %LocalAppData%\OneRoomHealthKiosk\preferences.json
/// </summary>
public class UserPreferences
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneRoomHealthKiosk",
        "preferences.json");

    private static UserPreferences? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the singleton instance, loading from disk if needed.
    /// </summary>
    public static UserPreferences Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= Load();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Whether to use Hardware API mode (true) or Navigate mode (false) on startup.
    /// Default is true (Hardware API mode).
    /// </summary>
    [JsonPropertyName("useHardwareApiMode")]
    public bool UseHardwareApiMode { get; set; } = true;

    /// <summary>
    /// Timestamp of last preference save.
    /// </summary>
    [JsonPropertyName("lastSaved")]
    public DateTime LastSaved { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Loads preferences from disk, or returns defaults if file doesn't exist.
    /// </summary>
    private static UserPreferences Load()
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                var json = File.ReadAllText(PreferencesPath);
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json);
                if (prefs != null)
                {
                    Logger.Log($"User preferences loaded: UseHardwareApiMode={prefs.UseHardwareApiMode}");
                    return prefs;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load user preferences: {ex.Message}");
        }

        Logger.Log("Using default user preferences (Hardware API mode)");
        return new UserPreferences();
    }

    /// <summary>
    /// Saves current preferences to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            LastSaved = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PreferencesPath, json);
            Logger.Log($"User preferences saved: UseHardwareApiMode={UseHardwareApiMode}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save user preferences: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the Hardware API mode preference and saves.
    /// </summary>
    public void SetHardwareApiMode(bool enabled)
    {
        if (UseHardwareApiMode != enabled)
        {
            UseHardwareApiMode = enabled;
            Save();
        }
    }
}
