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
