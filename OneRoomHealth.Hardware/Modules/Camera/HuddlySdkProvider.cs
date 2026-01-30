using System.Collections.Concurrent;
using Huddly.Sdk;
using Microsoft.Extensions.Logging;

namespace OneRoomHealth.Hardware.Modules.Camera;

/// <summary>
/// Singleton provider for the Huddly SDK. Manages ISdk lifecycle and device events.
/// </summary>
public sealed class HuddlySdkProvider : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HuddlySdkProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IDevice> _connectedDevices = new();

    private ISdk? _sdk;
    private bool _isMonitoring;
    private bool _disposed;

    /// <summary>
    /// Fired when a Huddly device connects.
    /// </summary>
    public event EventHandler<IDevice>? DeviceConnected;

    /// <summary>
    /// Fired when a Huddly device disconnects.
    /// </summary>
    public event EventHandler<IDevice>? DeviceDisconnected;

    public HuddlySdkProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<HuddlySdkProvider>();
    }

    /// <summary>
    /// Gets whether the SDK is currently monitoring for devices.
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// Initializes the SDK and starts device monitoring.
    /// </summary>
    /// <param name="useUsbDiscovery">Enable USB device discovery.</param>
    /// <param name="useIpDiscovery">Enable IP device discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(bool useUsbDiscovery, bool useIpDiscovery, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HuddlySdkProvider));

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_sdk != null)
            {
                _logger.LogWarning("SDK already initialized");
                return;
            }

            _logger.LogInformation("Initializing Huddly SDK (USB: {Usb}, IP: {Ip})", useUsbDiscovery, useIpDiscovery);

            // Create SDK instance
            _sdk = Sdk.CreateDefault(_loggerFactory);

            // Subscribe to events BEFORE calling StartMonitoring (SDK requirement)
            // Using lambda to extract device from event args
            _sdk.DeviceConnected += (sender, e) => OnSdkDeviceConnected(e.Device);
            _sdk.DeviceDisconnected += (sender, e) => OnSdkDeviceDisconnected(e.Device);

            // Start device monitoring (timeout in milliseconds, cancellation token)
            await _sdk.StartMonitoring(30000, cancellationToken);
            _isMonitoring = true;

            _logger.LogInformation("Huddly SDK initialized and monitoring for devices");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Huddly SDK");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets a device by its serial number.
    /// </summary>
    /// <param name="serialNumber">The device serial number.</param>
    /// <returns>The device if connected, null otherwise.</returns>
    public IDevice? GetDeviceBySerial(string serialNumber)
    {
        if (string.IsNullOrEmpty(serialNumber)) return null;
        _connectedDevices.TryGetValue(serialNumber, out var device);
        return device;
    }

    /// <summary>
    /// Gets all currently connected devices.
    /// </summary>
    public IReadOnlyCollection<IDevice> GetConnectedDevices()
    {
        return _connectedDevices.Values.ToList().AsReadOnly();
    }

    private void OnSdkDeviceConnected(IDevice device)
    {
        var serial = device.Serial;

        _logger.LogInformation("Huddly device connected: {Serial} ({Model})", serial, device.Model);

        if (!string.IsNullOrEmpty(serial))
        {
            _connectedDevices[serial] = device;
        }

        // Re-raise the event for subscribers
        DeviceConnected?.Invoke(this, device);
    }

    private void OnSdkDeviceDisconnected(IDevice device)
    {
        var serial = device.Serial;

        _logger.LogInformation("Huddly device disconnected: {Serial}", serial);

        if (!string.IsNullOrEmpty(serial))
        {
            _connectedDevices.TryRemove(serial, out _);
        }

        // Re-raise the event for subscribers
        DeviceDisconnected?.Invoke(this, device);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing Huddly SDK provider");

        if (_sdk != null)
        {
            _sdk.Dispose();
            _sdk = null;
        }

        _connectedDevices.Clear();
        _isMonitoring = false;
        _initLock.Dispose();

        _logger.LogInformation("Huddly SDK provider disposed");
    }
}
