namespace OneRoomHealth.Hardware.Abstractions;

/// <summary>
/// Basic information about a hardware device.
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// Unique identifier for the device (e.g., "0", "1").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name of the device.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Device model or type.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Current health status of the device.
    /// </summary>
    public DeviceHealth Health { get; set; }

    /// <summary>
    /// Last time the device was seen or responded (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Device type category.
    /// </summary>
    public string? DeviceType { get; init; }
}
