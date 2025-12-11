using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Lighting;

/// <summary>
/// Runtime state tracking for a lighting device.
/// </summary>
internal class LightingDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required LightingDeviceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the device was updated (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Whether the light is enabled (on/off).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Current brightness (0-100).
    /// </summary>
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Current color values.
    /// </summary>
    public RgbwColor Color { get; set; } = new();

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Whether connected to DMX controller.
    /// </summary>
    public bool Connected { get; set; } = false;
}

/// <summary>
/// RGBW color values.
/// </summary>
public class RgbwColor
{
    /// <summary>
    /// Red channel (0-255).
    /// </summary>
    public int Red { get; set; } = 255;

    /// <summary>
    /// Green channel (0-255).
    /// </summary>
    public int Green { get; set; } = 255;

    /// <summary>
    /// Blue channel (0-255).
    /// </summary>
    public int Blue { get; set; } = 255;

    /// <summary>
    /// White channel (0-255).
    /// </summary>
    public int White { get; set; } = 0;

    public RgbwColor Clone() => new()
    {
        Red = Red,
        Green = Green,
        Blue = Blue,
        White = White
    };
}

/// <summary>
/// Detailed status response for a lighting device.
/// </summary>
public class LightingStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool Enabled { get; set; }
    public int Brightness { get; set; }
    public RgbwColor Color { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool DmxConnected { get; set; }
}
