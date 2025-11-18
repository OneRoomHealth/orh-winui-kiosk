using System.Text.Json.Serialization;

namespace KioskApp;

/// <summary>
/// Root configuration object for the kiosk application.
/// Loaded from %ProgramData%\OneRoomHealth\Kiosk\config.json
/// </summary>
public class KioskConfiguration
{
    [JsonPropertyName("kiosk")]
    public KioskSettings Kiosk { get; set; } = new();

    [JsonPropertyName("debug")]
    public DebugSettings Debug { get; set; } = new();

    [JsonPropertyName("exit")]
    public ExitSettings Exit { get; set; } = new();

    [JsonPropertyName("logging")]
    public LoggingSettings Logging { get; set; } = new();

    [JsonPropertyName("httpApi")]
    public HttpApiSettings HttpApi { get; set; } = new();
}

/// <summary>
/// Core kiosk display and behavior settings.
/// </summary>
public class KioskSettings
{
    [JsonPropertyName("defaultUrl")]
    public string DefaultUrl { get; set; } = "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default";

    [JsonPropertyName("targetMonitorIndex")]
    public int TargetMonitorIndex { get; set; } = 1; // Default to second monitor (index 1)

    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; } = true;

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("videoMode")]
    public VideoModeSettings VideoMode { get; set; } = new();
}

/// <summary>
/// Video mode settings for Flic button video control.
/// </summary>
public class VideoModeSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("carescapeVideoPath")]
    public string CarescapeVideoPath { get; set; } = @"C:\Videos\carescape.mp4";

    [JsonPropertyName("demoVideoPath")]
    public string DemoVideoPath { get; set; } = @"C:\Videos\demo.mp4";

    [JsonPropertyName("carescapeVolume")]
    public double CarescapeVolume { get; set; } = 50;

    [JsonPropertyName("demoVolume")]
    public double DemoVolume { get; set; } = 75;

    [JsonPropertyName("targetMonitor")]
    public int TargetMonitor { get; set; } = 1; // Default to second monitor (same as main window)

    [JsonPropertyName("flicButtonEnabled")]
    public bool FlicButtonEnabled { get; set; } = true;
}

/// <summary>
/// Debug mode settings for developer access.
/// </summary>
public class DebugSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true; // Enabled by default for development

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Ctrl+Shift+I";

    [JsonPropertyName("autoOpenDevTools")]
    public bool AutoOpenDevTools { get; set; } = false;

    [JsonPropertyName("windowSizePercent")]
    public int WindowSizePercent { get; set; } = 80;
}

/// <summary>
/// Exit mechanism settings for kiosk mode escape.
/// </summary>
public class ExitSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true; // Enabled by default for development

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Ctrl+Shift+Q";

    [JsonPropertyName("requirePassword")]
    public bool RequirePassword { get; set; } = true;

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = ""; // Empty by default - will be set on first run

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 5000; // 5 second timeout for dialogs
}

/// <summary>
/// Logging configuration.
/// </summary>
public class LoggingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "Info";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "%LocalAppData%\\OneRoomHealthKiosk\\logs";

    [JsonPropertyName("maxSizeKb")]
    public int MaxSizeKb { get; set; } = 10240;

    [JsonPropertyName("maxFiles")]
    public int MaxFiles { get; set; } = 5;
}

/// <summary>
/// HTTP API settings for remote navigation control.
/// </summary>
public class HttpApiSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8787;

    [JsonPropertyName("allowRemote")]
    public bool AllowRemote { get; set; } = false;
}
