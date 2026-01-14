using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using System.Text.Json;

namespace OneRoomHealth.Hardware.Modules.Display;

/// <summary>
/// Hardware module for controlling Novastar LED displays via HTTP API.
/// Supports multiple IP addresses per display for redundancy.
/// </summary>
public class DisplayModule : HardwareModuleBase
{
    private readonly HttpClient _httpClient;
    private readonly DisplayConfiguration _config;
    private readonly Dictionary<string, DisplayDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Display";

    public DisplayModule(
        ILogger<DisplayModule> logger,
        DisplayConfiguration config,
        HttpClient httpClient)
        : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        IsEnabled = config.Enabled;
    }

    public override async Task<bool> InitializeAsync()
    {
        if (!IsEnabled)
        {
            Logger.LogInformation("{ModuleName}: Module is disabled", ModuleName);
            return false;
        }

        Logger.LogInformation("{ModuleName}: Initializing with {Count} displays", ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            foreach (var device in _config.Devices)
            {
                _deviceStates[device.Id] = new DisplayDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    IpHealthStatus = device.IpAddresses.ToDictionary(ip => ip, ip => false)
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered display '{Name}' (ID: {Id}) with {IpCount} IP addresses",
                    ModuleName, device.Name, device.Id, device.IpAddresses.Count);
            }

            IsInitialized = true;
            Logger.LogInformation("{ModuleName}: Initialization complete", ModuleName);
            return true;
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
                DeviceType = "Display"
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

            return new DisplayStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Model = state.Config.Model,
                Health = state.Health,
                LastSeen = state.LastSeen,
                Brightness = state.Brightness,
                Enabled = state.Enabled,
                IpAddresses = state.Config.IpAddresses.ToList(),
                IpHealthStatus = new Dictionary<string, bool>(state.IpHealthStatus),
                Errors = state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set display brightness (0-100).
    /// </summary>
    public async Task SetBrightnessAsync(string deviceId, int brightness)
    {
        if (brightness < 0 || brightness > 100)
            throw new ArgumentOutOfRangeException(nameof(brightness), "Brightness must be 0-100");

        await _stateLock.WaitAsync();
        DisplayDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Display device '{deviceId}' not found");
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation(
            "{ModuleName}: Setting brightness for '{Name}' to {Brightness}%",
            ModuleName, state.Config.Name, brightness);

        // Convert 0-100 to Novastar 0-1 range
        var novastarBrightness = brightness / 100.0;

        // Try all IP addresses
        var success = false;
        foreach (var ip in state.Config.IpAddresses)
        {
            try
            {
                var url = $"http://{ip}:{state.Config.Port}/api/v1/screen/displayparams";
                var payload = new { brightness = novastarBrightness };
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PutAsync(url, content);
                response.EnsureSuccessStatusCode();

                Logger.LogDebug("{ModuleName}: Brightness set successfully via {Ip}", ModuleName, ip);
                success = true;
                break; // Success on one IP is sufficient
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "{ModuleName}: Failed to set brightness via {Ip}: {Error}",
                    ModuleName, ip, ex.Message);
            }
        }

        if (!success)
            throw new Exception($"Failed to set brightness on all IP addresses for display '{deviceId}'");

        // Update cached state
        await _stateLock.WaitAsync();
        try
        {
            state.Brightness = brightness;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Enable or disable display (turn on/off).
    /// </summary>
    public async Task SetEnabledAsync(string deviceId, bool enabled)
    {
        await _stateLock.WaitAsync();
        DisplayDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Display device '{deviceId}' not found");
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation(
            "{ModuleName}: Setting display '{Name}' enabled={Enabled}",
            ModuleName, state.Config.Name, enabled);

        var success = false;
        foreach (var ip in state.Config.IpAddresses)
        {
            try
            {
                var url = $"http://{ip}:{state.Config.Port}/api/v1/screen/output/display/state";
                var payload = new { enabled };
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PutAsync(url, content);
                response.EnsureSuccessStatusCode();

                Logger.LogDebug("{ModuleName}: Display state set successfully via {Ip}", ModuleName, ip);
                success = true;
                break;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "{ModuleName}: Failed to set display state via {Ip}: {Error}",
                    ModuleName, ip, ex.Message);
            }
        }

        if (!success)
            throw new Exception($"Failed to set display state on all IP addresses for display '{deviceId}'");

        await _stateLock.WaitAsync();
        try
        {
            state.Enabled = enabled;
        }
        finally
        {
            _stateLock.Release();
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
                var deviceIds = _deviceStates.Keys.ToList();
                var tasks = deviceIds.Select(id => CheckDeviceHealthAsync(id, cancellationToken));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error in monitoring loop", ModuleName);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task CheckDeviceHealthAsync(string deviceId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        DisplayDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                return;
        }
        finally
        {
            _stateLock.Release();
        }

        var previousHealth = state.Health;
        var healthyIpCount = 0;

        // Check all IP addresses
        foreach (var ip in state.Config.IpAddresses)
        {
            try
            {
                var url = $"http://{ip}:{state.Config.Port}/api/v1/screen";
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5)); // 5s timeout per IP

                var response = await _httpClient.GetAsync(url, cts.Token);
                var isHealthy = response.IsSuccessStatusCode;

                state.IpHealthStatus[ip] = isHealthy;
                if (isHealthy)
                {
                    healthyIpCount++;

                    // Try to read current brightness
                    try
                    {
                        var json = await response.Content.ReadAsStringAsync(cts.Token);
                        var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("brightness", out var brightnessElement))
                        {
                            var novastarBrightness = brightnessElement.GetDouble();
                            state.Brightness = (int)(novastarBrightness * 100);
                        }
                    }
                    catch { /* Ignore parsing errors */ }
                }
            }
            catch (OperationCanceledException)
            {
                state.IpHealthStatus[ip] = false;
                Logger.LogDebug("{ModuleName}: Health check timeout for {Ip}", ModuleName, ip);
            }
            catch (Exception ex)
            {
                state.IpHealthStatus[ip] = false;
                Logger.LogDebug("{ModuleName}: Health check failed for {Ip}: {Error}", ModuleName, ip, ex.Message);
            }
        }

        // Determine overall health
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (healthyIpCount == state.Config.IpAddresses.Count)
            {
                state.Health = DeviceHealth.Healthy;
                state.LastSeen = DateTime.UtcNow;
                state.Errors.Clear();
            }
            else if (healthyIpCount > 0)
            {
                state.Health = DeviceHealth.Unhealthy;
                state.LastSeen = DateTime.UtcNow;
                state.Errors = new List<string> { $"Only {healthyIpCount}/{state.Config.IpAddresses.Count} IPs responding" };
            }
            else
            {
                state.Health = DeviceHealth.Offline;
                state.Errors = new List<string> { "No IP addresses responding" };
            }

            // Raise event if health changed
            if (previousHealth != state.Health)
            {
                OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
                {
                    DeviceId = deviceId,
                    NewHealth = state.Health,
                    PreviousHealth = previousHealth,
                    ErrorMessage = state.Errors.FirstOrDefault()
                });
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public override async Task ShutdownAsync()
    {
        await base.ShutdownAsync();
        _stateLock.Dispose();
        _httpClient.Dispose();
        Logger.LogInformation("{ModuleName}: Resources disposed", ModuleName);
    }
}
