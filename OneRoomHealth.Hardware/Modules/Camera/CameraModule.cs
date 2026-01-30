using Huddly.Sdk;
using Huddly.Sdk.Models;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Camera;

/// <summary>
/// Hardware module for controlling Huddly cameras via direct SDK integration.
/// Supports PTZ control, auto-tracking, and auto-framing.
/// </summary>
public class CameraModule : HardwareModuleBase
{
    private readonly CameraConfiguration _config;
    private readonly HuddlySdkProvider _sdkProvider;
    private readonly Dictionary<string, CameraDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Camera";

    public CameraModule(
        ILogger<CameraModule> logger,
        CameraConfiguration config,
        HuddlySdkProvider sdkProvider)
        : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _sdkProvider = sdkProvider ?? throw new ArgumentNullException(nameof(sdkProvider));
        IsEnabled = config.Enabled;
    }

    public override async Task<bool> InitializeAsync()
    {
        if (!IsEnabled)
        {
            Logger.LogInformation("{ModuleName}: Module is disabled", ModuleName);
            return false;
        }

        Logger.LogInformation("{ModuleName}: Initializing with {Count} cameras", ModuleName, _config.Devices.Count);

        try
        {
            // Initialize device states from configuration (requires lock)
            await _stateLock.WaitAsync();
            try
            {
                foreach (var device in _config.Devices)
                {
                    _deviceStates[device.Id] = new CameraDeviceState
                    {
                        Config = device,
                        Health = DeviceHealth.Offline,
                        Enabled = true
                    };

                    Logger.LogInformation(
                        "{ModuleName}: Registered camera '{Name}' (ID: {Id}, DeviceId: {DeviceId})",
                        ModuleName, device.Name, device.Id, device.DeviceId);
                }

                // Subscribe to SDK device events
                _sdkProvider.DeviceConnected += OnHuddlyDeviceConnected;
                _sdkProvider.DeviceDisconnected += OnHuddlyDeviceDisconnected;
            }
            finally
            {
                _stateLock.Release();
            }

            // Initialize SDK and start device monitoring (no lock needed)
            await _sdkProvider.InitializeAsync(
                _config.UseUsbDiscovery,
                _config.UseIpDiscovery);

            // Check if any configured devices are already connected
            // IMPORTANT: Lock is released before this loop to avoid deadlock,
            // since TryMatchAndConnectDevice also acquires the lock
            foreach (var connectedDevice in _sdkProvider.GetConnectedDevices())
            {
                await TryMatchAndConnectDevice(connectedDevice);
            }

            IsInitialized = true;
            Logger.LogInformation("{ModuleName}: Initialization complete", ModuleName);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Initialization failed", ModuleName);
            return false;
        }
    }

    private async void OnHuddlyDeviceConnected(object? sender, IDevice device)
    {
        Logger.LogInformation("{ModuleName}: Huddly device connected: {Serial} ({Model})",
            ModuleName, device.Serial, device.Model);

        await TryMatchAndConnectDevice(device);
    }

    private async Task TryMatchAndConnectDevice(IDevice device)
    {
        var serial = device.Serial;
        if (string.IsNullOrEmpty(serial))
        {
            Logger.LogWarning("{ModuleName}: Connected device has no serial number", ModuleName);
            return;
        }

        CameraDeviceState? matchingState;
        DeviceHealth previousHealth;

        await _stateLock.WaitAsync();
        try
        {
            // Find matching configured device by DeviceId (serial number)
            matchingState = _deviceStates.Values
                .FirstOrDefault(s => s.Config.DeviceId == serial);

            if (matchingState == null)
            {
                Logger.LogInformation(
                    "{ModuleName}: Connected device {Serial} not in configuration, ignoring",
                    ModuleName, serial);
                return;
            }

            previousHealth = matchingState.Health;

            matchingState.SdkDevice = device;
            matchingState.Connected = true;
            matchingState.Health = DeviceHealth.Healthy;
            matchingState.LastSeen = DateTime.UtcNow;
            matchingState.Errors.Clear();

            Logger.LogInformation(
                "{ModuleName}: Camera '{Name}' connected (Serial: {Serial})",
                ModuleName, matchingState.Config.Name, serial);
        }
        finally
        {
            _stateLock.Release();
        }

        // Sync initial PTZ state from device OUTSIDE the lock
        // This performs network I/O and can take several seconds
        await SyncDeviceStateFromDeviceAsync(matchingState, device);

        if (previousHealth != matchingState.Health)
        {
            OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
            {
                DeviceId = matchingState.Config.Id,
                NewHealth = matchingState.Health,
                PreviousHealth = previousHealth
            });
        }
    }

    private async void OnHuddlyDeviceDisconnected(object? sender, IDevice device)
    {
        var serial = device.Serial;

        Logger.LogInformation("{ModuleName}: Huddly device disconnected: {Serial}", ModuleName, serial);

        await _stateLock.WaitAsync();
        try
        {
            var matchingState = _deviceStates.Values
                .FirstOrDefault(s => s.Config.DeviceId == serial);

            if (matchingState == null)
                return;

            var previousHealth = matchingState.Health;

            // Clear SDK device reference - it cannot be reused after disconnect
            matchingState.SdkDevice = null;
            matchingState.Connected = false;
            matchingState.Health = DeviceHealth.Offline;

            Logger.LogInformation(
                "{ModuleName}: Camera '{Name}' disconnected",
                ModuleName, matchingState.Config.Name);

            if (previousHealth != matchingState.Health)
            {
                OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
                {
                    DeviceId = matchingState.Config.Id,
                    NewHealth = matchingState.Health,
                    PreviousHealth = previousHealth
                });
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public override async Task<List<DeviceInfo>> GetDevicesAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.Values.Select(state => new DeviceInfo
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Model = state.Config.Model,
                Health = state.Health,
                LastSeen = state.LastSeen,
                DeviceType = "Camera"
            }).ToList();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public override async Task<object?> GetDeviceStatusAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                return null;

            return new CameraStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Model = state.Config.Model,
                DeviceId = state.Config.DeviceId,
                Health = state.Health,
                LastSeen = state.LastSeen,
                Enabled = state.Enabled,
                Connected = state.Connected,
                PtzPosition = new PtzPosition
                {
                    Pan = state.PtzPosition.Pan,
                    Tilt = state.PtzPosition.Tilt,
                    Zoom = state.PtzPosition.Zoom
                },
                AutoTrackingEnabled = state.AutoTrackingEnabled,
                AutoFramingEnabled = state.AutoFramingEnabled,
                Errors = state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Enable or disable a camera.
    /// </summary>
    public async Task SetEnabledAsync(string deviceId, bool enabled)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");

            state.Enabled = enabled;
            Logger.LogInformation("{ModuleName}: Camera '{Name}' {State}",
                ModuleName, state.Config.Name, enabled ? "enabled" : "disabled");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get current PTZ position.
    /// </summary>
    public async Task<PtzPosition?> GetPtzPositionAsync(string deviceId)
    {
        CameraDeviceState? state;
        IDevice? device;

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                return null;
            device = state.SdkDevice;
        }
        finally
        {
            _stateLock.Release();
        }

        // Try to get fresh PTZ from device
        if (device != null)
        {
            try
            {
                var panResult = await device.GetPan();
                var tiltResult = await device.GetTilt();
                var zoomResult = await device.GetZoom();

                await _stateLock.WaitAsync();
                try
                {
                    if (panResult.IsSuccess)
                        state.PtzPosition.Pan = panResult.Value;
                    if (tiltResult.IsSuccess)
                        state.PtzPosition.Tilt = tiltResult.Value;
                    if (zoomResult.IsSuccess)
                        state.PtzPosition.Zoom = zoomResult.Value;
                }
                finally
                {
                    _stateLock.Release();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Failed to get PTZ from device", ModuleName);
            }
        }

        return new PtzPosition
        {
            Pan = state.PtzPosition.Pan,
            Tilt = state.PtzPosition.Tilt,
            Zoom = state.PtzPosition.Zoom
        };
    }

    /// <summary>
    /// Set PTZ position.
    /// </summary>
    public async Task<PtzPosition?> SetPtzPositionAsync(string deviceId, double? pan, double? tilt, double? zoom)
    {
        CameraDeviceState? state;
        IDevice? device;

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");
            device = state.SdkDevice;
        }
        finally
        {
            _stateLock.Release();
        }

        if (device == null)
            throw new InvalidOperationException($"Camera '{deviceId}' is not connected");

        // Disable auto-tracking when manually controlling PTZ
        if (state.AutoTrackingEnabled || state.AutoFramingEnabled)
        {
            await SetAutoTrackingAsync(deviceId, false);
        }

        var newPan = Math.Clamp(pan ?? state.PtzPosition.Pan, -1.0, 1.0);
        var newTilt = Math.Clamp(tilt ?? state.PtzPosition.Tilt, -1.0, 1.0);
        var newZoom = Math.Clamp(zoom ?? state.PtzPosition.Zoom, 0.0, 1.0);

        // Apply PTZ via SDK
        var panResult = await device.SetPan(newPan);
        var tiltResult = await device.SetTilt(newTilt);
        var zoomResult = await device.SetZoom(newZoom);

        // Check for errors
        if (!panResult.IsSuccess)
        {
            Logger.LogError("{ModuleName}: SetPan failed: {Message}", ModuleName, panResult.Message);
            throw new InvalidOperationException($"SetPan failed: {panResult.Message}");
        }
        if (!tiltResult.IsSuccess)
        {
            Logger.LogError("{ModuleName}: SetTilt failed: {Message}", ModuleName, tiltResult.Message);
            throw new InvalidOperationException($"SetTilt failed: {tiltResult.Message}");
        }
        if (!zoomResult.IsSuccess)
        {
            Logger.LogError("{ModuleName}: SetZoom failed: {Message}", ModuleName, zoomResult.Message);
            throw new InvalidOperationException($"SetZoom failed: {zoomResult.Message}");
        }

        // Update cached state
        await _stateLock.WaitAsync();
        try
        {
            state.PtzPosition.Pan = newPan;
            state.PtzPosition.Tilt = newTilt;
            state.PtzPosition.Zoom = newZoom;
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Set PTZ for camera '{Name}': Pan={Pan}, Tilt={Tilt}, Zoom={Zoom}",
            ModuleName, state.Config.Name, newPan, newTilt, newZoom);

        return new PtzPosition
        {
            Pan = newPan,
            Tilt = newTilt,
            Zoom = newZoom
        };
    }

    /// <summary>
    /// Get auto-tracking state.
    /// </summary>
    public async Task<(bool Supported, bool Enabled)> GetAutoTrackingAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");

            return (true, state.AutoTrackingEnabled);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set auto-tracking state.
    /// </summary>
    public async Task<bool> SetAutoTrackingAsync(string deviceId, bool enabled)
    {
        CameraDeviceState? state;
        IDevice? device;

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");
            device = state.SdkDevice;
        }
        finally
        {
            _stateLock.Release();
        }

        if (device == null)
        {
            Logger.LogWarning("{ModuleName}: Cannot set auto-tracking - device not connected", ModuleName);
            return false;
        }

        try
        {
            // Use GeniusFraming for auto-tracking enabled
            // Use cast to FramingMode with value 0 for "off" (manual mode)
            var mode = enabled ? FramingMode.GeniusFraming : (FramingMode)0;
            var result = await device.SetFramingMode(mode);

            if (result.IsSuccess)
            {
                await _stateLock.WaitAsync();
                try
                {
                    state.AutoTrackingEnabled = enabled;
                    state.AutoFramingEnabled = enabled;
                }
                finally
                {
                    _stateLock.Release();
                }

                Logger.LogInformation("{ModuleName}: Camera '{Name}' auto-tracking {State}",
                    ModuleName, state.Config.Name, enabled ? "enabled" : "disabled");
                return true;
            }
            else
            {
                Logger.LogWarning("{ModuleName}: SetFramingMode failed: {Message}",
                    ModuleName, result.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Error setting auto-tracking", ModuleName);
            return false;
        }
    }

    protected override async Task MonitorDevicesAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_config.MonitorInterval);
        Logger.LogInformation(
            "{ModuleName}: Starting device monitoring (interval: {Interval}s)",
            ModuleName, _config.MonitorInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Capture snapshot of connected devices while holding lock (quick operation)
                List<(CameraDeviceState State, IDevice Device, DeviceHealth PreviousHealth)> devicesToSync;

                await _stateLock.WaitAsync(cancellationToken);
                try
                {
                    devicesToSync = _deviceStates.Values
                        .Where(s => s.SdkDevice != null && s.Connected)
                        .Select(s => (s, s.SdkDevice!, s.Health))
                        .ToList();

                    // Update LastSeen for all connected devices
                    foreach (var (state, _, _) in devicesToSync)
                    {
                        state.LastSeen = DateTime.UtcNow;
                    }
                }
                finally
                {
                    _stateLock.Release();
                }

                // Sync device states OUTSIDE the lock to avoid blocking other operations
                // Network I/O in SyncDeviceStateFromDeviceAsync can take seconds per device
                foreach (var (state, device, previousHealth) in devicesToSync)
                {
                    try
                    {
                        await SyncDeviceStateFromDeviceAsync(state, device);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "{ModuleName}: Failed to sync state for '{Name}'",
                            ModuleName, state.Config.Name);
                    }

                    // Check for health changes after sync
                    if (previousHealth != state.Health)
                    {
                        OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
                        {
                            DeviceId = state.Config.Id,
                            NewHealth = state.Health,
                            PreviousHealth = previousHealth
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error in monitoring loop", ModuleName);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    /// <summary>
    /// Sync PTZ and framing state from a device without holding the state lock.
    /// This method performs network I/O and should not be called while holding _stateLock.
    /// </summary>
    private async Task SyncDeviceStateFromDeviceAsync(CameraDeviceState state, IDevice device)
    {
        try
        {
            // Get current PTZ values (network I/O - can be slow)
            var panResult = await device.GetPan();
            var tiltResult = await device.GetTilt();
            var zoomResult = await device.GetZoom();

            // Update state under lock (quick operation)
            await _stateLock.WaitAsync();
            try
            {
                if (panResult.IsSuccess)
                    state.PtzPosition.Pan = panResult.Value;
                if (tiltResult.IsSuccess)
                    state.PtzPosition.Tilt = tiltResult.Value;
                if (zoomResult.IsSuccess)
                    state.PtzPosition.Zoom = zoomResult.Value;
            }
            finally
            {
                _stateLock.Release();
            }

            // Get framing mode if supported (network I/O)
            try
            {
                var framingResult = await device.GetFramingMode();
                if (framingResult.IsSuccess)
                {
                    var isAutoEnabled = (int)framingResult.Value != 0;

                    await _stateLock.WaitAsync();
                    try
                    {
                        state.AutoTrackingEnabled = isAutoEnabled;
                        state.AutoFramingEnabled = isAutoEnabled;
                    }
                    finally
                    {
                        _stateLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "{ModuleName}: GetFramingMode not supported for '{Name}'",
                    ModuleName, state.Config.Name);
            }

            Logger.LogDebug(
                "{ModuleName}: Synced state for '{Name}': PTZ({Pan}, {Tilt}, {Zoom}), AutoTracking={Auto}",
                ModuleName, state.Config.Name,
                state.PtzPosition.Pan, state.PtzPosition.Tilt, state.PtzPosition.Zoom,
                state.AutoTrackingEnabled);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Failed to sync device state for '{Name}'",
                ModuleName, state.Config.Name);
        }
    }

    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);

        // Unsubscribe from SDK events
        _sdkProvider.DeviceConnected -= OnHuddlyDeviceConnected;
        _sdkProvider.DeviceDisconnected -= OnHuddlyDeviceDisconnected;

        await base.ShutdownAsync();

        // Clear device states
        await _stateLock.WaitAsync();
        try
        {
            foreach (var state in _deviceStates.Values)
            {
                state.SdkDevice = null;
                state.Connected = false;
            }
            _deviceStates.Clear();
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}
