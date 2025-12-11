namespace OneRoomHealth.Hardware.Abstractions;

/// <summary>
/// Represents the health status of a hardware device.
/// </summary>
public enum DeviceHealth
{
    /// <summary>
    /// Device is not responding or unreachable.
    /// </summary>
    Offline,

    /// <summary>
    /// Device is responding but experiencing issues (partial failure, degraded performance).
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Device is fully operational.
    /// </summary>
    Healthy
}
