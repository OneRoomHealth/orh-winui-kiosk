using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Camera;

/// <summary>
/// Runtime state tracking for a camera device.
/// </summary>
internal class CameraDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required CameraDeviceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the device responded (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Whether the camera is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Current PTZ position.
    /// </summary>
    public PtzPosition PtzPosition { get; set; } = new();

    /// <summary>
    /// Whether auto-tracking is enabled.
    /// </summary>
    public bool AutoTrackingEnabled { get; set; } = false;

    /// <summary>
    /// Whether auto-framing is enabled.
    /// </summary>
    public bool AutoFramingEnabled { get; set; } = false;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Whether we're connected to the camera device.
    /// </summary>
    public bool Connected { get; set; } = false;
}

/// <summary>
/// PTZ position values.
/// </summary>
public class PtzPosition
{
    /// <summary>
    /// Pan value (-1.0 to 1.0).
    /// </summary>
    public double Pan { get; set; } = 0.0;

    /// <summary>
    /// Tilt value (-1.0 to 1.0).
    /// </summary>
    public double Tilt { get; set; } = 0.0;

    /// <summary>
    /// Zoom value (0.0 to 1.0).
    /// </summary>
    public double Zoom { get; set; } = 1.0;
}

/// <summary>
/// Detailed status response for a camera device.
/// </summary>
public class CameraStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? DeviceId { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public PtzPosition PtzPosition { get; set; } = new();
    public bool AutoTrackingEnabled { get; set; }
    public bool AutoFramingEnabled { get; set; }
    public List<string> Errors { get; set; } = new();
}
