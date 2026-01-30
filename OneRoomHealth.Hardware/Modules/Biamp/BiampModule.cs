using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Biamp;

/// <summary>
/// Hardware module for controlling Biamp Parlé VBC 2800 video conferencing codecs via Telnet.
/// Supports PTZ control, autoframing, and device reboot.
/// </summary>
public class BiampModule : HardwareModuleBase
{
    private readonly BiampConfiguration _config;
    private readonly Dictionary<string, BiampDeviceState> _deviceStates = new();
    private readonly Dictionary<string, BiampTelnetClient> _telnetClients = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Biamp";

    public BiampModule(
        ILogger<BiampModule> logger,
        BiampConfiguration config)
        : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        IsEnabled = config.Enabled;
    }

    public override async Task<bool> InitializeAsync()
    {
        if (!IsEnabled)
        {
            Logger.LogInformation("{ModuleName}: Module is disabled", ModuleName);
            return false;
        }

        Logger.LogInformation("{ModuleName}: Initializing with {Count} devices", ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            foreach (var device in _config.Devices)
            {
                // Create device state
                _deviceStates[device.Id] = new BiampDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    Connected = false
                };

                // Create Telnet client
                var client = new BiampTelnetClient(
                    Logger,
                    device.IpAddress,
                    device.Port,
                    device.Username,
                    device.Password);

                _telnetClients[device.Id] = client;

                Logger.LogInformation(
                    "{ModuleName}: Registered device '{Name}' (ID: {Id}, IP: {IpAddress}:{Port})",
                    ModuleName, device.Name, device.Id, device.IpAddress, device.Port);

                // Attempt initial connection
                try
                {
                    if (await client.ConnectAsync())
                    {
                        await SyncDeviceInfoAsync(device.Id);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "{ModuleName}: Initial connection to '{Name}' failed, will retry during monitoring",
                        ModuleName, device.Name);
                }
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
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Sync device info (firmware version, serial number) after connection.
    /// </summary>
    private async Task SyncDeviceInfoAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            return;
        if (!_deviceStates.TryGetValue(deviceId, out var state))
            return;

        try
        {
            // Get firmware version
            var version = await client.SendCommandAsync("DEVICE get version");
            if (version != null)
            {
                state.FirmwareVersion = version;
            }

            // Get serial number
            var serial = await client.SendCommandAsync("DEVICE get serialNumber");
            if (serial != null)
            {
                state.SerialNumber = serial;
            }

            // Update state
            state.Connected = true;
            state.Health = DeviceHealth.Healthy;
            state.LastSeen = DateTime.UtcNow;
            state.Errors.Clear();

            Logger.LogInformation(
                "{ModuleName}: Device '{Name}' connected (FW: {Version}, SN: {Serial})",
                ModuleName, state.Config.Name, state.FirmwareVersion, state.SerialNumber);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "{ModuleName}: Failed to sync device info for '{Name}'",
                ModuleName, state.Config.Name);
            state.Errors.Add($"Sync failed: {ex.Message}");
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
                Model = state.Config.Model ?? "Parlé VBC 2800",
                Health = state.Health,
                LastSeen = state.LastSeen,
                DeviceType = "Biamp"
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

            return new BiampStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Model = state.Config.Model ?? "Parlé VBC 2800",
                IpAddress = state.Config.IpAddress,
                Port = state.Config.Port,
                Health = state.Health,
                LastSeen = state.LastSeen,
                Connected = state.Connected,
                FirmwareVersion = state.FirmwareVersion,
                SerialNumber = state.SerialNumber,
                Pan = state.Pan,
                Tilt = state.Tilt,
                Zoom = state.Zoom,
                AutoframingEnabled = state.AutoframingEnabled,
                Errors = state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    #region Pan Control

    /// <summary>
    /// Get current pan position.
    /// </summary>
    public async Task<double?> GetPanAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendCommandAsync("Camera get pan");
        if (result != null && double.TryParse(result, out var pan))
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Pan = pan;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            return pan;
        }
        return null;
    }

    /// <summary>
    /// Set pan position (-100 to +100).
    /// </summary>
    public async Task<bool> SetPanAsync(string deviceId, double value)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        value = Math.Clamp(value, -100.0, 100.0);
        var result = await client.SendCommandAsync($"Camera set pan {value:F0}");

        if (result != null)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Pan = value;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            Logger.LogInformation("{ModuleName}: Set pan to {Value} for device {DeviceId}", ModuleName, value, deviceId);
            return true;
        }
        return false;
    }

    #endregion

    #region Tilt Control

    /// <summary>
    /// Get current tilt position.
    /// </summary>
    public async Task<double?> GetTiltAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendCommandAsync("Camera get tilt");
        if (result != null && double.TryParse(result, out var tilt))
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Tilt = tilt;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            return tilt;
        }
        return null;
    }

    /// <summary>
    /// Set tilt position (-100 to +100).
    /// </summary>
    public async Task<bool> SetTiltAsync(string deviceId, double value)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        value = Math.Clamp(value, -100.0, 100.0);
        var result = await client.SendCommandAsync($"Camera set tilt {value:F0}");

        if (result != null)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Tilt = value;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            Logger.LogInformation("{ModuleName}: Set tilt to {Value} for device {DeviceId}", ModuleName, value, deviceId);
            return true;
        }
        return false;
    }

    #endregion

    #region Zoom Control

    /// <summary>
    /// Get current zoom level.
    /// </summary>
    public async Task<double?> GetZoomAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendCommandAsync("Camera get zoom");
        if (result != null && double.TryParse(result, out var zoom))
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Zoom = zoom;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            return zoom;
        }
        return null;
    }

    /// <summary>
    /// Set zoom level (1.0 to 5.0).
    /// </summary>
    public async Task<bool> SetZoomAsync(string deviceId, double value)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        value = Math.Clamp(value, 1.0, 5.0);
        var result = await client.SendCommandAsync($"Camera set zoom {value:F1}");

        if (result != null)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Zoom = value;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            Logger.LogInformation("{ModuleName}: Set zoom to {Value} for device {DeviceId}", ModuleName, value, deviceId);
            return true;
        }
        return false;
    }

    #endregion

    #region Autoframing Control

    /// <summary>
    /// Get autoframing state.
    /// </summary>
    public async Task<bool?> GetAutoframingAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendCommandAsync("Camera get autoframing");
        if (result != null)
        {
            var enabled = result.ToLowerInvariant().Contains("true") || result == "1";
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.AutoframingEnabled = enabled;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            return enabled;
        }
        return null;
    }

    /// <summary>
    /// Set autoframing state.
    /// </summary>
    public async Task<bool> SetAutoframingAsync(string deviceId, bool enabled)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var value = enabled ? "true" : "false";
        var result = await client.SendCommandAsync($"Camera set autoframing {value}");

        if (result != null)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.AutoframingEnabled = enabled;
                    state.LastSeen = DateTime.UtcNow;
                }
            }
            finally
            {
                _stateLock.Release();
            }
            Logger.LogInformation("{ModuleName}: Set autoframing to {Value} for device {DeviceId}",
                ModuleName, enabled, deviceId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggle autoframing state.
    /// </summary>
    public async Task<bool?> ToggleAutoframingAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendCommandAsync("Camera toggle autoframing");
        if (result != null)
        {
            // Query new state
            return await GetAutoframingAsync(deviceId);
        }
        return null;
    }

    #endregion

    #region System Commands

    /// <summary>
    /// Reboot the Biamp device.
    /// </summary>
    public async Task<bool> RebootAsync(string deviceId)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            throw new KeyNotFoundException($"Biamp device '{deviceId}' not found");

        var result = await client.SendRebootAsync();

        if (result)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Connected = false;
                    state.Health = DeviceHealth.Offline;
                }
            }
            finally
            {
                _stateLock.Release();
            }

            Logger.LogInformation("{ModuleName}: Reboot command sent to device {DeviceId}", ModuleName, deviceId);
            return true;
        }
        return false;
    }

    #endregion

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
                foreach (var deviceId in _deviceStates.Keys.ToList())
                {
                    await CheckDeviceHealthAsync(deviceId, cancellationToken);
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

    private async Task CheckDeviceHealthAsync(string deviceId, CancellationToken ct)
    {
        if (!_telnetClients.TryGetValue(deviceId, out var client))
            return;
        if (!_deviceStates.TryGetValue(deviceId, out var state))
            return;

        var previousHealth = state.Health;

        try
        {
            // Simple health check - query firmware version
            var result = await client.SendCommandAsync("DEVICE get version", ct);

            await _stateLock.WaitAsync(ct);
            try
            {
                if (result != null)
                {
                    state.Health = DeviceHealth.Healthy;
                    state.Connected = true;
                    state.LastSeen = DateTime.UtcNow;
                    state.FirmwareVersion = result;
                    state.Errors.Clear();
                }
                else
                {
                    state.Health = DeviceHealth.Unhealthy;
                    state.Errors.Add("Health check returned null");
                    if (state.Errors.Count > 10)
                        state.Errors = state.Errors.TakeLast(10).ToList();
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
        catch (Exception ex)
        {
            await _stateLock.WaitAsync(ct);
            try
            {
                state.Health = DeviceHealth.Offline;
                state.Connected = false;
                state.Errors.Add($"Health check failed: {ex.Message}");
                if (state.Errors.Count > 10)
                    state.Errors = state.Errors.TakeLast(10).ToList();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        if (previousHealth != state.Health)
        {
            OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
            {
                DeviceId = deviceId,
                NewHealth = state.Health,
                PreviousHealth = previousHealth,
                ErrorMessage = state.Errors.LastOrDefault()
            });
        }
    }

    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);

        await base.ShutdownAsync();

        // Dispose all Telnet clients
        await _stateLock.WaitAsync();
        try
        {
            foreach (var client in _telnetClients.Values)
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{ModuleName}: Error disposing Telnet client", ModuleName);
                }
            }
            _telnetClients.Clear();

            foreach (var state in _deviceStates.Values)
            {
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
