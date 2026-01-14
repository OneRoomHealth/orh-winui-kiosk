using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using FTD2XX_NET;

namespace OneRoomHealth.Hardware.Modules.Lighting;

/// <summary>
/// Hardware module for controlling RGB/RGBW lights via DMX512 using FTDI USB adapters.
/// </summary>
public class LightingModule : HardwareModuleBase
{
    private readonly LightingConfiguration _config;
    private readonly Dictionary<string, LightingDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // DMX state
    private FTDI? _ftdiDevice;
    private bool _dmxEnabled = false;
    private readonly byte[] _dmxBuffer = new byte[513]; // DMX universe + start byte
    private Task? _dmxSenderTask;
    private CancellationTokenSource? _dmxCts;

    public override string ModuleName => "Lighting";

    public LightingModule(
        ILogger<LightingModule> logger,
        LightingConfiguration config)
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

        Logger.LogInformation("{ModuleName}: Initializing with {Count} lights", ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            // Initialize device states from configuration
            foreach (var device in _config.Devices)
            {
                _deviceStates[device.Id] = new LightingDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    Enabled = true,
                    Brightness = 100,
                    Color = new RgbwColor { Red = 255, Green = 255, Blue = 255, White = 0 }
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered light '{Name}' (ID: {Id})",
                    ModuleName, device.Name, device.Id);
            }

            // Initialize DMX
            await InitializeDmxAsync();

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

    private async Task InitializeDmxAsync()
    {
        try
        {
            Logger.LogInformation("{ModuleName}: Initializing DMX via FTDI", ModuleName);

            _ftdiDevice = new FTDI();

            // Get number of FTDI devices
            uint deviceCount = 0;
            var status = _ftdiDevice.GetNumberOfDevices(ref deviceCount);

            if (status != FTDI.FT_STATUS.FT_OK || deviceCount == 0)
            {
                Logger.LogWarning("{ModuleName}: No FTDI devices found, DMX disabled", ModuleName);
                _dmxEnabled = false;
                return;
            }

            Logger.LogInformation("{ModuleName}: Found {Count} FTDI device(s)", ModuleName, deviceCount);

            // Open first device
            status = _ftdiDevice.OpenByIndex(0);
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                Logger.LogError("{ModuleName}: Failed to open FTDI device: {Status}", ModuleName, status);
                _dmxEnabled = false;
                return;
            }

            // Configure for DMX512: 250kbaud, 8N2
            status = _ftdiDevice.SetBaudRate(250000);
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                Logger.LogError("{ModuleName}: Failed to set baud rate: {Status}", ModuleName, status);
                _dmxEnabled = false;
                return;
            }

            status = _ftdiDevice.SetDataCharacteristics(
                FTDI.FT_DATA_BITS.FT_BITS_8,
                FTDI.FT_STOP_BITS.FT_STOP_BITS_2,
                FTDI.FT_PARITY.FT_PARITY_NONE);

            if (status != FTDI.FT_STATUS.FT_OK)
            {
                Logger.LogError("{ModuleName}: Failed to set data characteristics: {Status}", ModuleName, status);
                _dmxEnabled = false;
                return;
            }

            status = _ftdiDevice.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0);
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                Logger.LogError("{ModuleName}: Failed to set flow control: {Status}", ModuleName, status);
                _dmxEnabled = false;
                return;
            }

            // Purge buffers
            _ftdiDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

            _dmxEnabled = true;

            // Mark all devices as connected
            foreach (var state in _deviceStates.Values)
            {
                state.Connected = true;
                state.Health = DeviceHealth.Healthy;
                state.LastSeen = DateTime.UtcNow;
            }

            // Start DMX sender task
            _dmxCts = new CancellationTokenSource();
            _dmxSenderTask = Task.Run(() => DmxSenderLoopAsync(_dmxCts.Token));

            Logger.LogInformation("{ModuleName}: DMX initialized successfully at {Fps} FPS",
                ModuleName, _config.Fps);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Failed to initialize DMX", ModuleName);
            _dmxEnabled = false;
        }

        await Task.CompletedTask;
    }

    private async Task DmxSenderLoopAsync(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _config.Fps);
        Logger.LogInformation("{ModuleName}: DMX sender started ({Fps} FPS, {Interval}ms)",
            ModuleName, _config.Fps, frameInterval.TotalMilliseconds);

        while (!cancellationToken.IsCancellationRequested && _dmxEnabled && _ftdiDevice != null)
        {
            try
            {
                // Send DMX break (set break, clear break)
                _ftdiDevice.SetBreak(true);
                await Task.Delay(1, cancellationToken); // Break ~176us minimum
                _ftdiDevice.SetBreak(false);
                await Task.Delay(1, cancellationToken); // MAB ~12us minimum

                // Send DMX data
                uint bytesWritten = 0;
                _ftdiDevice.Write(_dmxBuffer, _dmxBuffer.Length, ref bytesWritten);

                await Task.Delay(frameInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: DMX sender error", ModuleName);
                await Task.Delay(100, cancellationToken);
            }
        }

        Logger.LogInformation("{ModuleName}: DMX sender stopped", ModuleName);
    }

    private void UpdateDmxBuffer(string deviceId)
    {
        if (!_deviceStates.TryGetValue(deviceId, out var state))
            return;

        var mapping = state.Config.ChannelMapping;
        if (mapping == null)
            return;

        // Calculate actual color values (apply brightness)
        var scale = state.Enabled ? state.Brightness / 100.0 : 0;
        var r = (byte)(state.Color.Red * scale);
        var g = (byte)(state.Color.Green * scale);
        var b = (byte)(state.Color.Blue * scale);
        var w = (byte)(state.Color.White * scale);

        // Update DMX buffer (channel 0 is start byte, channels are 1-indexed)
        if (mapping.Red > 0 && mapping.Red <= 512)
            _dmxBuffer[mapping.Red] = r;
        if (mapping.Green > 0 && mapping.Green <= 512)
            _dmxBuffer[mapping.Green] = g;
        if (mapping.Blue > 0 && mapping.Blue <= 512)
            _dmxBuffer[mapping.Blue] = b;
        if (mapping.White > 0 && mapping.White <= 512)
            _dmxBuffer[mapping.White] = w;

        Logger.LogDebug("{ModuleName}: DMX buffer updated for {DeviceId}: R={R} G={G} B={B} W={W}",
            ModuleName, deviceId, r, g, b, w);
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
                DeviceType = "Lighting"
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

            return new LightingStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Model = state.Config.Model,
                Health = state.Health,
                LastSeen = state.LastSeen,
                Enabled = state.Enabled,
                Brightness = state.Brightness,
                Color = state.Color.Clone(),
                Errors = state.Errors.ToList(),
                DmxConnected = _dmxEnabled
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Enable or disable a light.
    /// </summary>
    public async Task SetEnabledAsync(string deviceId, bool enabled)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Lighting device '{deviceId}' not found");

            state.Enabled = enabled;
            state.LastSeen = DateTime.UtcNow;
            UpdateDmxBuffer(deviceId);

            Logger.LogInformation("{ModuleName}: Light '{Name}' {State}",
                ModuleName, state.Config.Name, enabled ? "enabled" : "disabled");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get brightness.
    /// </summary>
    public async Task<int?> GetBrightnessAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.TryGetValue(deviceId, out var state) ? state.Brightness : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set brightness (0-100).
    /// </summary>
    public async Task SetBrightnessAsync(string deviceId, int brightness)
    {
        if (brightness < 0 || brightness > 100)
            throw new ArgumentOutOfRangeException(nameof(brightness), "Brightness must be 0-100");

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Lighting device '{deviceId}' not found");

            state.Brightness = brightness;
            state.LastSeen = DateTime.UtcNow;
            UpdateDmxBuffer(deviceId);

            Logger.LogInformation("{ModuleName}: Light '{Name}' brightness set to {Brightness}%",
                ModuleName, state.Config.Name, brightness);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Get current color.
    /// </summary>
    public async Task<RgbwColor?> GetColorAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.TryGetValue(deviceId, out var state) ? state.Color.Clone() : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set color.
    /// </summary>
    public async Task SetColorAsync(string deviceId, int red, int green, int blue, int white = 0)
    {
        // Validate
        if (red < 0 || red > 255) throw new ArgumentOutOfRangeException(nameof(red));
        if (green < 0 || green > 255) throw new ArgumentOutOfRangeException(nameof(green));
        if (blue < 0 || blue > 255) throw new ArgumentOutOfRangeException(nameof(blue));
        if (white < 0 || white > 255) throw new ArgumentOutOfRangeException(nameof(white));

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Lighting device '{deviceId}' not found");

            state.Color.Red = red;
            state.Color.Green = green;
            state.Color.Blue = blue;
            state.Color.White = white;
            state.LastSeen = DateTime.UtcNow;
            UpdateDmxBuffer(deviceId);

            Logger.LogInformation("{ModuleName}: Light '{Name}' color set to RGBW({R},{G},{B},{W})",
                ModuleName, state.Config.Name, red, green, blue, white);
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
                    foreach (var state in _deviceStates.Values)
                    {
                        var previousHealth = state.Health;

                        if (_dmxEnabled)
                        {
                            state.Connected = true;
                            state.Health = DeviceHealth.Healthy;
                        }
                        else
                        {
                            state.Connected = false;
                            state.Health = DeviceHealth.Offline;
                        }

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

        // Stop DMX sender
        _dmxCts?.Cancel();
        if (_dmxSenderTask != null)
        {
            try
            {
                await _dmxSenderTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        // Close FTDI device
        if (_ftdiDevice != null)
        {
            try
            {
                _ftdiDevice.Close();
            }
            catch { }
        }

        await base.ShutdownAsync();

        // Dispose synchronization primitives
        _stateLock.Dispose();
        _dmxCts?.Dispose();

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}
