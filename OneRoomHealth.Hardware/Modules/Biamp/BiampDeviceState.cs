using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Biamp;

/// <summary>
/// Runtime state tracking for a Biamp device.
/// </summary>
internal class BiampDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required BiampDeviceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the device responded (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Whether we're connected to the Biamp device.
    /// </summary>
    public bool Connected { get; set; } = false;

    /// <summary>
    /// Firmware version reported by the device.
    /// </summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// Serial number reported by the device.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Current pan position (-100 to +100).
    /// </summary>
    public double Pan { get; set; } = 0.0;

    /// <summary>
    /// Current tilt position (-100 to +100).
    /// </summary>
    public double Tilt { get; set; } = 0.0;

    /// <summary>
    /// Current zoom level (1.0 to 5.0).
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Whether autoframing is enabled.
    /// </summary>
    public bool AutoframingEnabled { get; set; } = false;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Detailed status response for a Biamp device.
/// </summary>
public class BiampStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? IpAddress { get; init; }
    public int Port { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool Connected { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public double Pan { get; set; }
    public double Tilt { get; set; }
    public double Zoom { get; set; }
    public bool AutoframingEnabled { get; set; }
    public List<string> Errors { get; set; } = new();
}
