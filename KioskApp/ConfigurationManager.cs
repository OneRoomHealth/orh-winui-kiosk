using System;
using System.IO;
using System.Text.Json;

namespace KioskApp;

/// <summary>
/// Manages loading and saving of kiosk configuration.
/// Configuration is stored at %ProgramData%\OneRoomHealth\Kiosk\config.json
/// </summary>
public static class ConfigurationManager
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OneRoomHealth", "Kiosk", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads configuration from disk, or returns default configuration if file doesn't exist.
    /// </summary>
    public static KioskConfiguration Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                Logger.Log($"Loading configuration from: {ConfigPath}");
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<KioskConfiguration>(json, JsonOptions);

                if (config != null)
                {
                    Logger.Log("Configuration loaded successfully");
                    return config;
                }
                else
                {
                    Logger.Log("Failed to deserialize configuration, using defaults");
                }
            }
            else
            {
                Logger.Log($"Configuration file not found at {ConfigPath}, creating default configuration");
                var defaultConfig = new KioskConfiguration();
                Save(defaultConfig); // Create default config file
                return defaultConfig;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load configuration: {ex.Message}");
        }

        // Return default configuration on any error
        return new KioskConfiguration();
    }

    /// <summary>
    /// Saves configuration to disk.
    /// </summary>
    public static void Save(KioskConfiguration config)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);

            Logger.Log($"Configuration saved to: {ConfigPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    public static string GetConfigPath()
    {
        return ConfigPath;
    }
}
