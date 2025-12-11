using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Microphone;

/// <summary>
/// Runtime state tracking for a network microphone device.
/// </summary>
internal class MicrophoneDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required AudioDeviceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the device responded (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Whether connected to the device.
    /// </summary>
    public bool Connected { get; set; } = false;

    /// <summary>
    /// Current mute state.
    /// </summary>
    public bool Muted { get; set; } = false;

    /// <summary>
    /// Current volume (0-100).
    /// </summary>
    public int Volume { get; set; } = 75;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Detailed status response for a microphone device.
/// </summary>
public class MicrophoneStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DeviceId { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool Connected { get; set; }
    public bool Muted { get; set; }
    public int Volume { get; set; }
    public List<string> Errors { get; set; } = new();
}
