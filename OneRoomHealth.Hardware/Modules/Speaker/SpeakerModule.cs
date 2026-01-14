using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Speaker;

/// <summary>
/// Hardware module for controlling network speakers.
/// Provides volume control via HTTP API.
/// </summary>
public class SpeakerModule : HardwareModuleBase
{
    private readonly SpeakerConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, SpeakerDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Speaker";

    public SpeakerModule(
        ILogger<SpeakerModule> logger,
        SpeakerConfiguration config,
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

        Logger.LogInformation("{ModuleName}: Initializing with {Count} speakers",
            ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            foreach (var device in _config.Devices)
            {
                _deviceStates[device.Id] = new SpeakerDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    Volume = 70,
                    Muted = false
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered speaker '{Name}' (ID: {Id})",
                    ModuleName, device.Name, device.Id);
            }

            // Try to connect to all devices
            foreach (var deviceId in _deviceStates.Keys)
            {
                await TryConnectDeviceAsync(deviceId);
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

    private async Task TryConnectDeviceAsync(string deviceId)
    {
        if (!_deviceStates.TryGetValue(deviceId, out var state))
            return;

        try
        {
            // For network speakers, we would check HTTP connectivity here
            // This is a placeholder for actual network speaker API integration
            state.Connected = true;
            state.Health = DeviceHealth.Healthy;
            state.LastSeen = DateTime.UtcNow;
            state.Errors.Clear();

            Logger.LogInformation("{ModuleName}: Connected to speaker '{Name}'",
                ModuleName, state.Config.Name);
        }
        catch (Exception ex)
        {
            state.Connected = false;
            state.Health = DeviceHealth.Offline;
            state.Errors.Add($"Connection failed: {ex.Message}");

            Logger.LogWarning(ex, "{ModuleName}: Failed to connect to speaker '{Name}'",
                ModuleName, state.Config.Name);
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
                Model = null,
                Health = state.Health,
                LastSeen = state.LastSeen,
                DeviceType = "Speaker"
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

            return new SpeakerStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                DeviceId = state.Config.DeviceId,
                Health = state.Health,
                LastSeen = state.LastSeen,
                Connected = state.Connected,
                Muted = state.Muted,
                Volume = state.Volume,
                Errors = state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get combined speaker volume status (for /speakers/volume endpoint).
    /// </summary>
    public async Task<SpeakerVolumeStatus> GetVolumeStatusAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            // Return first speaker's status as system speakers
            var firstDevice = _deviceStates.Values.FirstOrDefault();
            return new SpeakerVolumeStatus
            {
                Volume = firstDevice?.Volume ?? 70,
                Muted = firstDevice?.Muted ?? false
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set volume for all speakers (0-100).
    /// </summary>
    public async Task SetVolumeAsync(int volume)
    {
        if (volume < 0 || volume > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            foreach (var state in _deviceStates.Values)
            {
                // TODO: Send volume command to actual network speaker
                state.Volume = volume;
                state.LastSeen = DateTime.UtcNow;
            }

            Logger.LogInformation("{ModuleName}: All speakers volume set to {Volume}%",
                ModuleName, volume);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get volume for specific speaker.
    /// </summary>
    public async Task<int?> GetDeviceVolumeAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.TryGetValue(deviceId, out var state) ? state.Volume : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set volume for specific speaker.
    /// </summary>
    public async Task SetDeviceVolumeAsync(string deviceId, int volume)
    {
        if (volume < 0 || volume > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Speaker device '{deviceId}' not found");

            // TODO: Send volume command to actual network speaker
            state.Volume = volume;
            state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Speaker '{Name}' volume set to {Volume}%",
                ModuleName, state.Config.Name, volume);
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
                foreach (var deviceId in deviceIds)
                {
                    await CheckDeviceHealthAsync(deviceId, cancellationToken);
                }
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
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                return;

            var previousHealth = state.Health;

            // For network speakers, check connectivity
            // This is a placeholder - actual implementation would ping the device
            if (state.Connected)
            {
                state.Health = DeviceHealth.Healthy;
                state.LastSeen = DateTime.UtcNow;
            }
            else
            {
                state.Health = DeviceHealth.Offline;
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
        finally
        {
            _stateLock.Release();
        }
    }

    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);
        await base.ShutdownAsync();

        // Dispose resources
        _stateLock.Dispose();
        _httpClient.Dispose();

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}
