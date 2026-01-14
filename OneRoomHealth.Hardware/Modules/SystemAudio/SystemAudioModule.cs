using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Modules.SystemAudio;

/// <summary>
/// Hardware module for controlling Windows system audio (speakers and microphone).
/// Uses NAudio/CoreAudioApi for audio device control.
/// </summary>
public class SystemAudioModule : HardwareModuleBase
{
    private readonly SystemAudioConfiguration _config;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SystemAudioDeviceState _state = new();

    // NAudio devices
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _speakerDevice;
    private MMDevice? _microphoneDevice;

    public override string ModuleName => "SystemAudio";

    public SystemAudioModule(
        ILogger<SystemAudioModule> logger,
        SystemAudioConfiguration config)
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

        Logger.LogInformation("{ModuleName}: Initializing system audio controls", ModuleName);

        await _stateLock.WaitAsync();
        try
        {
            // Initialize NAudio device enumerator
            _deviceEnumerator = new MMDeviceEnumerator();

            // Get default speakers
            try
            {
                _speakerDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Logger.LogInformation("{ModuleName}: Default speaker: {Name}",
                    ModuleName, _speakerDevice.FriendlyName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Could not get default speaker", ModuleName);
            }

            // Get default microphone
            try
            {
                _microphoneDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                Logger.LogInformation("{ModuleName}: Default microphone: {Name}",
                    ModuleName, _microphoneDevice.FriendlyName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Could not get default microphone", ModuleName);
            }

            // Update initial state
            UpdateAudioState();

            _state.Health = DeviceHealth.Healthy;
            _state.LastSeen = DateTime.UtcNow;

            IsInitialized = true;
            Logger.LogInformation("{ModuleName}: Initialization complete", ModuleName);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Initialization failed", ModuleName);
            _state.Health = DeviceHealth.Unhealthy;
            _state.Errors.Add($"Initialization failed: {ex.Message}");
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void UpdateAudioState()
    {
        try
        {
            // Update speaker state
            if (_speakerDevice?.AudioEndpointVolume != null)
            {
                _state.SpeakerVolume = (int)(_speakerDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                _state.SpeakerMuted = _speakerDevice.AudioEndpointVolume.Mute;
            }

            // Update microphone state
            if (_microphoneDevice?.AudioEndpointVolume != null)
            {
                _state.MicrophoneVolume = (int)(_microphoneDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                _state.MicrophoneMuted = _microphoneDevice.AudioEndpointVolume.Mute;
            }

            _state.LastSeen = DateTime.UtcNow;
            _state.Health = DeviceHealth.Healthy;
            _state.Errors.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Error updating audio state", ModuleName);
            _state.Health = DeviceHealth.Unhealthy;
            _state.Errors.Add($"Update failed: {ex.Message}");
        }
    }

    public override async Task<List<DeviceInfo>> GetDevicesAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            return new List<DeviceInfo>
            {
                new DeviceInfo
                {
                    Id = "0",
                    Name = "System Audio",
                    Model = "Windows Audio",
                    Health = _state.Health,
                    LastSeen = _state.LastSeen,
                    DeviceType = "SystemAudio"
                }
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public override async Task<object?> GetDeviceStatusAsync(string deviceId)
    {
        if (deviceId != "0") return null;

        await _stateLock.WaitAsync();
        try
        {
            UpdateAudioState();

            return new SystemAudioStatus
            {
                Id = "0",
                Name = "System Audio",
                Health = _state.Health,
                LastSeen = _state.LastSeen,
                SpeakerVolume = _state.SpeakerVolume,
                SpeakerMuted = _state.SpeakerMuted,
                MicrophoneVolume = _state.MicrophoneVolume,
                MicrophoneMuted = _state.MicrophoneMuted,
                ComputerName = _state.ComputerName,
                Platform = _state.Platform,
                Errors = _state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get speaker volume.
    /// </summary>
    public async Task<(int Volume, bool Muted)> GetSpeakerVolumeAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            UpdateAudioState();
            return (_state.SpeakerVolume, _state.SpeakerMuted);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set speaker volume (0-100).
    /// </summary>
    public async Task SetSpeakerVolumeAsync(int volume)
    {
        if (volume < 0 || volume > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            if (_speakerDevice?.AudioEndpointVolume == null)
                throw new InvalidOperationException("Speaker device not available");

            _speakerDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f;
            _state.SpeakerVolume = volume;
            _state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Speaker volume set to {Volume}%", ModuleName, volume);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Increase speaker volume by step.
    /// </summary>
    public async Task SpeakerVolumeUpAsync(int step = 5)
    {
        var (currentVolume, _) = await GetSpeakerVolumeAsync();
        var newVolume = Math.Min(100, currentVolume + step);
        await SetSpeakerVolumeAsync(newVolume);
    }

    /// <summary>
    /// Decrease speaker volume by step.
    /// </summary>
    public async Task SpeakerVolumeDownAsync(int step = 5)
    {
        var (currentVolume, _) = await GetSpeakerVolumeAsync();
        var newVolume = Math.Max(0, currentVolume - step);
        await SetSpeakerVolumeAsync(newVolume);
    }

    /// <summary>
    /// Set speaker mute state.
    /// </summary>
    public async Task SetSpeakerMuteAsync(bool muted)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_speakerDevice?.AudioEndpointVolume == null)
                throw new InvalidOperationException("Speaker device not available");

            _speakerDevice.AudioEndpointVolume.Mute = muted;
            _state.SpeakerMuted = muted;
            _state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Speaker {State}", ModuleName, muted ? "muted" : "unmuted");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get microphone volume.
    /// </summary>
    public async Task<(int Volume, bool Muted)> GetMicrophoneVolumeAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            UpdateAudioState();
            return (_state.MicrophoneVolume, _state.MicrophoneMuted);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set microphone volume (0-100).
    /// </summary>
    public async Task SetMicrophoneVolumeAsync(int volume)
    {
        if (volume < 0 || volume > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            if (_microphoneDevice?.AudioEndpointVolume == null)
                throw new InvalidOperationException("Microphone device not available");

            _microphoneDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f;
            _state.MicrophoneVolume = volume;
            _state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Microphone volume set to {Volume}%", ModuleName, volume);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Increase microphone volume by step.
    /// </summary>
    public async Task MicrophoneVolumeUpAsync(int step = 5)
    {
        var (currentVolume, _) = await GetMicrophoneVolumeAsync();
        var newVolume = Math.Min(100, currentVolume + step);
        await SetMicrophoneVolumeAsync(newVolume);
    }

    /// <summary>
    /// Decrease microphone volume by step.
    /// </summary>
    public async Task MicrophoneVolumeDownAsync(int step = 5)
    {
        var (currentVolume, _) = await GetMicrophoneVolumeAsync();
        var newVolume = Math.Max(0, currentVolume - step);
        await SetMicrophoneVolumeAsync(newVolume);
    }

    /// <summary>
    /// Set microphone mute state.
    /// </summary>
    public async Task SetMicrophoneMuteAsync(bool muted)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_microphoneDevice?.AudioEndpointVolume == null)
                throw new InvalidOperationException("Microphone device not available");

            _microphoneDevice.AudioEndpointVolume.Mute = muted;
            _state.MicrophoneMuted = muted;
            _state.LastSeen = DateTime.UtcNow;

            Logger.LogInformation("{ModuleName}: Microphone {State}", ModuleName, muted ? "muted" : "unmuted");
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
                await _stateLock.WaitAsync(cancellationToken);
                try
                {
                    var previousHealth = _state.Health;
                    UpdateAudioState();

                    if (previousHealth != _state.Health)
                    {
                        OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
                        {
                            DeviceId = "0",
                            NewHealth = _state.Health,
                            PreviousHealth = previousHealth,
                            ErrorMessage = _state.Errors.LastOrDefault()
                        });
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error in monitoring loop", ModuleName);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);

        await base.ShutdownAsync();

        // Dispose NAudio resources
        _speakerDevice?.Dispose();
        _microphoneDevice?.Dispose();
        _deviceEnumerator?.Dispose();

        // Dispose synchronization primitives
        _stateLock.Dispose();

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}
