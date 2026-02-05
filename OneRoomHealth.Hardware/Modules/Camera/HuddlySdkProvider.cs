using System.Collections.Concurrent;
using Huddly.Sdk;
using Microsoft.Extensions.Logging;

namespace OneRoomHealth.Hardware.Modules.Camera;

/// <summary>
/// Singleton provider for the Huddly SDK. Manages ISdk lifecycle and device events.
/// Supports stopping and restarting without full disposal.
/// </summary>
public sealed class HuddlySdkProvider : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HuddlySdkProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IDevice> _connectedDevices = new();

    private ISdk? _sdk;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
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
    /// Can be called multiple times - will restart monitoring if previously stopped.
    /// </summary>
    /// <param name="useUsbDiscovery">Enable USB device discovery.</param>
    /// <param name="useIpDiscovery">Enable IP device discovery.</param>
    /// <param name="cancellationToken">Cancellation token for the initialization process.</param>
    public async Task InitializeAsync(bool useUsbDiscovery, bool useIpDiscovery, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HuddlySdkProvider));

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // If already monitoring, just return
            if (_isMonitoring && _sdk != null)
            {
                _logger.LogInformation("SDK already initialized and monitoring");
                return;
            }

            _logger.LogInformation("Initializing Huddly SDK (USB: {Usb}, IP: {Ip})", useUsbDiscovery, useIpDiscovery);

            // Create SDK instance if needed
            if (_sdk == null)
            {
                _sdk = Sdk.CreateDefault(_loggerFactory);

                // Subscribe to events BEFORE calling StartMonitoring (SDK requirement)
                // Using lambda to extract device from event args
                _sdk.DeviceConnected += (sender, e) => OnSdkDeviceConnected(e.Device);
                _sdk.DeviceDisconnected += (sender, e) => OnSdkDeviceDisconnected(e.Device);
            }

            // Create a new cancellation token source for this monitoring session
            _monitoringCts = new CancellationTokenSource();
            var monitoringToken = _monitoringCts.Token;

            // Start device monitoring as a background task that runs indefinitely.
            // We don't await this because we want monitoring to continue until stopped.
            _monitoringTask = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("SDK monitoring task started");
                    // Use a very long timeout (24 hours) to keep monitoring alive
                    // If it ever completes, restart it
                    while (!_disposed && !monitoringToken.IsCancellationRequested)
                    {
                        try
                        {
                            await _sdk!.StartMonitoring(86400000, monitoringToken); // 24 hours
                            if (!monitoringToken.IsCancellationRequested)
                            {
                                _logger.LogWarning("SDK monitoring completed unexpectedly, restarting...");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("SDK monitoring cancelled");
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!monitoringToken.IsCancellationRequested)
                            {
                                _logger.LogError(ex, "SDK monitoring error, restarting in 5 seconds...");
                                await Task.Delay(5000, monitoringToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SDK monitoring task failed");
                }
                finally
                {
                    _logger.LogInformation("SDK monitoring task exited");
                }
            }, monitoringToken);

            _isMonitoring = true;

            // Give the SDK a moment to discover initial devices
            await Task.Delay(2000, cancellationToken);

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
    /// Stops SDK monitoring without disposing the provider.
    /// Can be restarted by calling InitializeAsync again.
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed) return;

        await _initLock.WaitAsync();
        try
        {
            if (!_isMonitoring)
            {
                _logger.LogInformation("SDK monitoring already stopped");
                return;
            }

            _logger.LogInformation("Stopping Huddly SDK monitoring...");

            // Cancel the monitoring task
            _monitoringCts?.Cancel();

            // Wait for the monitoring task to complete
            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("SDK monitoring task did not complete within timeout");
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            // Cleanup
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
            _isMonitoring = false;

            // Clear connected devices (they'll be rediscovered on restart)
            _connectedDevices.Clear();

            _logger.LogInformation("Huddly SDK monitoring stopped");
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

        // Cancel monitoring task - don't wait synchronously to avoid UI thread deadlock
        // The task will exit cleanly when it sees the cancellation token
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;
        _monitoringTask = null; // Task will complete on its own

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
