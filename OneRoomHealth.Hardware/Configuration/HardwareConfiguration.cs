using System.Text.Json.Serialization;

namespace OneRoomHealth.Hardware.Configuration;

/// <summary>
/// Root hardware configuration containing all hardware module settings.
/// </summary>
public class HardwareConfiguration
{
    [JsonPropertyName("cameras")]
    public CameraConfiguration? Cameras { get; set; }

    [JsonPropertyName("displays")]
    public DisplayConfiguration? Displays { get; set; }

    [JsonPropertyName("lighting")]
    public LightingConfiguration? Lighting { get; set; }

    [JsonPropertyName("systemAudio")]
    public SystemAudioConfiguration? SystemAudio { get; set; }

    [JsonPropertyName("microphones")]
    public MicrophoneConfiguration? Microphones { get; set; }

    [JsonPropertyName("speakers")]
    public SpeakerConfiguration? Speakers { get; set; }

    [JsonPropertyName("biamp")]
    public BiampConfiguration? Biamp { get; set; }

    [JsonPropertyName("media")]
    public MediaConfiguration? Media { get; set; }

    [JsonPropertyName("firefly")]
    public FireflyConfiguration? Firefly { get; set; }
}

/// <summary>
/// Base configuration for hardware modules.
/// </summary>
public abstract class ModuleConfigurationBase
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("monitorInterval")]
    public double MonitorInterval { get; set; } = 5.0;
}

/// <summary>
/// Camera module configuration.
/// </summary>
public class CameraConfiguration : ModuleConfigurationBase
{
    /// <summary>
    /// Enable USB device discovery for Huddly cameras.
    /// </summary>
    [JsonPropertyName("useUsbDiscovery")]
    public bool UseUsbDiscovery { get; set; } = true;

    /// <summary>
    /// Enable IP device discovery for Huddly cameras (e.g., Huddly L1).
    /// </summary>
    [JsonPropertyName("useIpDiscovery")]
    public bool UseIpDiscovery { get; set; } = false;

    /// <summary>
    /// Automatically register any discovered Huddly camera, even if not explicitly configured.
    /// When true, cameras not in the devices list will be auto-added with generated IDs.
    /// When false (default), only cameras with matching deviceId in the devices list are used.
    /// </summary>
    [JsonPropertyName("autoDiscover")]
    public bool AutoDiscover { get; set; } = false;

    [JsonPropertyName("devices")]
    public List<CameraDeviceConfig> Devices { get; set; } = new();
}

public class CameraDeviceConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

/// <summary>
/// Display module configuration.
/// </summary>
public class DisplayConfiguration : ModuleConfigurationBase
{
    [JsonPropertyName("devices")]
    public List<DisplayDeviceConfig> Devices { get; set; } = new();
}

public class DisplayDeviceConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("ipAddresses")]
    public List<string> IpAddresses { get; set; } = new();

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8001;
}

/// <summary>
/// Lighting module configuration.
/// </summary>
public class LightingConfiguration : ModuleConfigurationBase
{
    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 25;

    [JsonPropertyName("devices")]
    public List<LightingDeviceConfig> Devices { get; set; } = new();
}

public class LightingDeviceConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("channelMapping")]
    public DmxChannelMapping? ChannelMapping { get; set; }
}

public class DmxChannelMapping
{
    [JsonPropertyName("red")]
    public int Red { get; set; }

    [JsonPropertyName("green")]
    public int Green { get; set; }

    [JsonPropertyName("blue")]
    public int Blue { get; set; }

    [JsonPropertyName("white")]
    public int White { get; set; }
}

/// <summary>
/// System audio configuration.
/// </summary>
public class SystemAudioConfiguration : ModuleConfigurationBase
{
    public SystemAudioConfiguration()
    {
        MonitorInterval = 1.0;
    }
}

/// <summary>
/// Microphone module configuration.
/// </summary>
public class MicrophoneConfiguration : ModuleConfigurationBase
{
    [JsonPropertyName("devices")]
    public List<AudioDeviceConfig> Devices { get; set; } = new();

    public MicrophoneConfiguration()
    {
        MonitorInterval = 1.0;
    }
}

/// <summary>
/// Speaker module configuration.
/// </summary>
public class SpeakerConfiguration : ModuleConfigurationBase
{
    [JsonPropertyName("devices")]
    public List<AudioDeviceConfig> Devices { get; set; } = new();

    public SpeakerConfiguration()
    {
        MonitorInterval = 1.0;
    }
}

public class AudioDeviceConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}

/// <summary>
/// Biamp module configuration for Parlé VBC 2800 video conferencing codecs.
/// </summary>
public class BiampConfiguration : ModuleConfigurationBase
{
    [JsonPropertyName("devices")]
    public List<BiampDeviceConfig> Devices { get; set; } = new();
}

public class BiampDeviceConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("ipAddress")]
    public required string IpAddress { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; } = 23;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "control";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

/// <summary>
/// Media serving configuration for video and audio file endpoints.
/// </summary>
public class MediaConfiguration
{
    /// <summary>
    /// Enable media file serving endpoint.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base directory containing media files to serve.
    /// Supports environment variable expansion (e.g., %USERPROFILE%\Videos).
    /// </summary>
    [JsonPropertyName("baseDirectory")]
    public string BaseDirectory { get; set; } = "";

    /// <summary>
    /// Additional directories to search for media files.
    /// Files are served from the first directory where they are found.
    /// </summary>
    [JsonPropertyName("additionalDirectories")]
    public List<string> AdditionalDirectories { get; set; } = new();

    /// <summary>
    /// Allowed file extensions (without dot). Empty list allows all common media types.
    /// Default: mp4, webm, ogg, mp3, wav, m4a
    /// </summary>
    [JsonPropertyName("allowedExtensions")]
    public List<string> AllowedExtensions { get; set; } = new() { "mp4", "webm", "ogg", "mp3", "wav", "m4a" };
}

/// <summary>
/// Firefly UVC otoscope camera module configuration.
/// Manages the FireflyCapture.Bridge subprocess and downstream image delivery.
/// </summary>
public class FireflyConfiguration : ModuleConfigurationBase
{
    /// <summary>
    /// Path to the FireflyCapture.Bridge.exe (32-bit bridge process).
    /// Relative paths are resolved from the kiosk exe directory.
    /// </summary>
    [JsonPropertyName("bridgeExePath")]
    public string BridgeExePath { get; set; } = "hardware\\firefly\\FireflyCapture.Bridge.exe";

    /// <summary>
    /// TCP port the bridge process listens on. Must match Bridge:Port in bridge appsettings.json.
    /// Default: 5200.
    /// </summary>
    [JsonPropertyName("bridgePort")]
    public int BridgePort { get; set; } = 5200;

    /// <summary>
    /// Path to SnapDll.dll passed to the bridge process. Relative to the bridge exe directory.
    /// Default: "SnapDll.dll" (same folder as bridge exe).
    /// </summary>
    [JsonPropertyName("snapDllPath")]
    public string SnapDllPath { get; set; } = "SnapDll.dll";

    /// <summary>
    /// How often the bridge polls IsButtonpress() in milliseconds. Default: 10.
    /// </summary>
    [JsonPropertyName("pollingIntervalMs")]
    public int PollingIntervalMs { get; set; } = 10;

    /// <summary>
    /// Seconds to wait for the bridge process to become healthy on startup.
    /// Default: 10.
    /// </summary>
    [JsonPropertyName("startupGracePeriodSeconds")]
    public int StartupGracePeriodSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of times to restart the bridge process before marking it degraded.
    /// Default: 5.
    /// </summary>
    [JsonPropertyName("maxRestartAttempts")]
    public int MaxRestartAttempts { get; set; } = 5;

    /// <summary>
    /// Downstream image delivery configuration.
    /// When Enabled is false captured images are returned from the API but not forwarded.
    /// </summary>
    [JsonPropertyName("downstream")]
    public FireflyDownstreamConfig Downstream { get; set; } = new();
}

/// <summary>
/// Downstream HTTP delivery settings for captured Firefly images.
/// </summary>
public class FireflyDownstreamConfig
{
    /// <summary>Whether to forward captured images to the downstream URL.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>Full URL to POST the captured image to.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// Delivery method. One of: "multipart" (multipart/form-data),
    /// "base64" (JSON body { "image": "&lt;base64&gt;" }), "raw" (application/octet-stream).
    /// Default: "multipart".
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "multipart";

    /// <summary>
    /// Optional Authorization header value (e.g., "Bearer __TOKEN__").
    /// When null or empty no Authorization header is sent.
    /// </summary>
    [JsonPropertyName("authHeader")]
    public string? AuthHeader { get; set; }

    /// <summary>
    /// Field name used for the image file in multipart delivery. Default: "image".
    /// </summary>
    [JsonPropertyName("multipartFieldName")]
    public string MultipartFieldName { get; set; } = "image";

    /// <summary>HTTP timeout for the downstream request in seconds. Default: 30.</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;
}
