namespace OneRoomHealth.Hardware.Api.Models;

/// <summary>
/// System status response model.
/// </summary>
public class SystemStatus
{
    /// <summary>
    /// Application name.
    /// </summary>
    public string Name { get; set; } = "OneRoom Health Kiosk";

    /// <summary>
    /// Application version.
    /// </summary>
    public string Version { get; set; } = "2.0.0";

    /// <summary>
    /// Current server time (UTC).
    /// </summary>
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Server uptime in seconds.
    /// </summary>
    public double UptimeSeconds { get; set; }

    /// <summary>
    /// Hardware module status summary.
    /// </summary>
    public Dictionary<string, ModuleStatus> Modules { get; set; } = new();
}

/// <summary>
/// Status of a hardware module.
/// </summary>
public class ModuleStatus
{
    /// <summary>
    /// Module name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the module is enabled in configuration.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the module has been initialized.
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// Number of devices managed by this module.
    /// </summary>
    public int DeviceCount { get; set; }

    /// <summary>
    /// Number of healthy devices.
    /// </summary>
    public int HealthyDevices { get; set; }

    /// <summary>
    /// Number of unhealthy devices.
    /// </summary>
    public int UnhealthyDevices { get; set; }

    /// <summary>
    /// Number of offline devices.
    /// </summary>
    public int OfflineDevices { get; set; }
}
