using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.Microphone;

/// <summary>
/// Hardware module for controlling network microphones.
/// Provides mute/unmute and volume control via HTTP API.
/// </summary>
public class MicrophoneModule : HardwareModuleBase
{
    private readonly MicrophoneConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, MicrophoneDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Microphone";

    public MicrophoneModule(
        ILogger<MicrophoneModule> logger,
        MicrophoneConfiguration config,
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

        Logger.LogInformation("{ModuleName}: Initializing with {Count} microphones",
            ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            foreach (var device in _config.Devices)
            {
                _deviceStates[device.Id] = new MicrophoneDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    Volume = 75,
                    Muted = false
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered microphone '{Name}' (ID: {Id})",
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
            // For network microphones, we would check HTTP connectivity here
            // This is a placeholder for actual network microphone API integration
            state.Connected = true;
            state.Health = DeviceHealth.Healthy;
            state.LastSeen = DateTime.UtcNow;
            state.Errors.Clear();

            Logger.LogInformation("{ModuleName}: Connected to microphone '{Name}'",
                ModuleName, state.Config.Name);
        }
        catch (Exception ex)
        {
            state.Connected = false;
            state.Health = DeviceHealth.Offline;
            state.Errors.Add($"Connection failed: {ex.Message}");

            Logger.LogWarning(ex, "{ModuleName}: Failed to connect to microphone '{Name}'",
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
                DeviceType = "Microphone"
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

            return new MicrophoneStatus
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
    /// Get mute state.
    /// </summary>
    public async Task<bool?> GetMuteAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.TryGetValue(deviceId, out var state) ? state.Muted : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set mute state.
    /// </summary>
    public async Task SetMuteAsync(string deviceId, bool muted)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Microphone device '{deviceId}' not found");

            // TODO: Send mute command to actual network microphone
            state.Muted = muted;
            state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Microphone '{Name}' {State}",
                ModuleName, state.Config.Name, muted ? "muted" : "unmuted");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get volume.
    /// </summary>
    public async Task<int?> GetVolumeAsync(string deviceId)
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
    /// Set volume (0-100).
    /// </summary>
    public async Task SetVolumeAsync(string deviceId, int volume)
    {
        if (volume < 0 || volume > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Microphone device '{deviceId}' not found");

            // TODO: Send volume command to actual network microphone
            state.Volume = volume;
            state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Microphone '{Name}' volume set to {Volume}%",
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

            // For network microphones, check connectivity
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
        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}
