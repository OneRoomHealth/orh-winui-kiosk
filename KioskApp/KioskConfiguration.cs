using System.Text.Json.Serialization;
using OneRoomHealth.Hardware.Configuration;

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

    [JsonPropertyName("hardware")]
    public HardwareConfiguration Hardware { get; set; } = new();
}

/// <summary>
/// Core kiosk display and behavior settings.
/// </summary>
public class KioskSettings
{
    /// <summary>
    /// Machine type identifier. Determines hardware profile and default behavior.
    /// Values: "carewall" (full AV, secondary display), "providerhub" (no DMX, primary display).
    /// </summary>
    [JsonPropertyName("machineType")]
    public string MachineType { get; set; } = "carewall";

    [JsonPropertyName("defaultUrl")]
    public string DefaultUrl { get; set; } = "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default";

    [JsonPropertyName("targetMonitorIndex")]
    public int TargetMonitorIndex { get; set; } = 1; // 1-based index (1 = primary, 2 = secondary, etc.)

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

    [JsonPropertyName("demoVideoPath1")]
    public string DemoVideoPath1 { get; set; } = @"C:\Videos\demo1.mp4";

    [JsonPropertyName("demoVideoPath2")]
    public string DemoVideoPath2 { get; set; } = @"C:\Videos\demo2.mp4";

    [JsonPropertyName("mpvPath")]
    public string? MpvPath { get; set; }

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

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = ""; // Empty uses default password "admin123"
}

/// <summary>
/// Logging configuration.
/// </summary>
public class LoggingSettings
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "%LocalAppData%\\OneRoomHealthKiosk\\logs";

    [JsonPropertyName("maxSizeKb")]
    public int MaxSizeKb { get; set; } = 10240;

    [JsonPropertyName("maxFiles")]
    public int MaxFiles { get; set; } = 5;
}

