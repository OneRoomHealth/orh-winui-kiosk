using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Display;

/// <summary>
/// Runtime state tracking for a display device.
/// </summary>
internal class DisplayDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required DisplayDeviceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the device responded (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Current brightness (0-100).
    /// </summary>
    public int Brightness { get; set; } = 0;

    /// <summary>
    /// Whether the display is enabled (on/off).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Per-IP health status for multi-IP displays.
    /// </summary>
    public Dictionary<string, bool> IpHealthStatus { get; set; } = new();
}

/// <summary>
/// Detailed status response for a display device.
/// </summary>
public class DisplayStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public int Brightness { get; set; }
    public bool Enabled { get; set; }
    public List<string> IpAddresses { get; set; } = new();
    public Dictionary<string, bool> IpHealthStatus { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
