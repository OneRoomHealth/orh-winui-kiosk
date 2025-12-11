namespace OneRoomHealth.Hardware.Abstractions;

/// <summary>
/// Interface that all hardware modules must implement.
/// Provides standard lifecycle management and device query methods.
/// </summary>
public interface IHardwareModule
{
    /// <summary>
    /// The name of this hardware module (e.g., "Camera", "Display", "Lighting").
    /// </summary>
    string ModuleName { get; }

    /// <summary>
    /// Indicates whether this module is enabled in configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Indicates whether this module has been successfully initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initialize the hardware module, load configuration, and prepare for operation.
    /// </summary>
    /// <returns>True if initialization was successful, false otherwise.</returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Start background monitoring and health checking for all devices.
    /// </summary>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task StartMonitoringAsync();

    /// <summary>
    /// Stop background monitoring and release resources.
    /// </summary>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task StopMonitoringAsync();

    /// <summary>
    /// Get a list of all devices managed by this module.
    /// </summary>
    /// <returns>List of device information.</returns>
    Task<List<DeviceInfo>> GetDevicesAsync();

    /// <summary>
    /// Get detailed status for a specific device.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <returns>Device status, or null if device not found.</returns>
    Task<object?> GetDeviceStatusAsync(string deviceId);

    /// <summary>
    /// Shutdown the module and cleanup all resources.
    /// </summary>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task ShutdownAsync();

    /// <summary>
    /// Event raised when a device's health status changes.
    /// </summary>
    event EventHandler<DeviceHealthChangedEventArgs>? DeviceHealthChanged;
}

/// <summary>
/// Event arguments for device health changes.
/// </summary>
public class DeviceHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// The device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// The new health status.
    /// </summary>
    public required DeviceHealth NewHealth { get; init; }

    /// <summary>
    /// The previous health status.
    /// </summary>
    public DeviceHealth PreviousHealth { get; init; }

    /// <summary>
    /// Timestamp of the health change (UTC).
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional error message if health changed due to an error.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
