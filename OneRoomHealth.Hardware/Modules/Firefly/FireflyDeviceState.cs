using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Firefly;

/// <summary>
/// Runtime state for a single Firefly UVC otoscope camera.
/// </summary>
internal sealed class FireflyDeviceState
{
    /// <summary>Logical device identifier (e.g., "firefly-0").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (from Windows.Devices.Enumeration).</summary>
    public required string FriendlyName { get; set; }

    /// <summary>
    /// Inferred Firefly model from USB PID.
    /// e.g., "DE300", "DE400", "GT700", "GT800", or "Unknown".
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// Full Windows device interface path (\\?\USB#VID_21CD&amp;PID_xxxx#...).
    /// Used as the DeviceId for Windows.Media.Capture.MediaCapture.
    /// </summary>
    public required string DeviceInterfaceId { get; set; }

    /// <summary>Current health reported by the module monitor loop.</summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>Whether this device was found during the last enumeration pass.</summary>
    public bool IsConnected { get; set; }

    /// <summary>UTC timestamp of the last successful detection.</summary>
    public DateTime LastSeen { get; set; }

    /// <summary>Number of capture operations completed this session.</summary>
    public int CaptureCount { get; set; }

    /// <summary>UTC timestamp of the most recent capture, or null if none yet.</summary>
    public DateTime? LastCaptureAt { get; set; }

    /// <summary>Accumulated error messages from recent failures.</summary>
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Public DTO returned by <c>GET /api/v1/firefly/{id}</c>.
/// </summary>
public sealed class FireflyDeviceStatus
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required string Model { get; init; }
    public required string DeviceInterfaceId { get; init; }
    public DeviceHealth Health { get; init; }
    public bool IsConnected { get; init; }
    public DateTime LastSeen { get; init; }
    public int CaptureCount { get; init; }
    public DateTime? LastCaptureAt { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}
