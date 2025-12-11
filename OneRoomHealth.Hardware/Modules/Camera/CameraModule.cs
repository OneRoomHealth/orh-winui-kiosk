using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneRoomHealth.Hardware.Modules.Camera;

/// <summary>
/// Hardware module for controlling Huddly cameras via CameraController.exe REST API.
/// Supports PTZ control, auto-tracking, and auto-framing.
/// </summary>
public class CameraModule : HardwareModuleBase
{
    private readonly CameraConfiguration _config;
    private readonly Dictionary<string, CameraDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // CameraController process and HTTP client
    private Process? _controllerProcess;
    private readonly HttpClient _httpClient;
    private readonly string _controllerApiUrl;
    private bool _controllerRunning = false;

    public override string ModuleName => "Camera";

    public CameraModule(
        ILogger<CameraModule> logger,
        CameraConfiguration config)
        : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        IsEnabled = config.Enabled;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _controllerApiUrl = $"http://localhost:{config.ControllerApiPort}";
    }

    public override async Task<bool> InitializeAsync()
    {
        if (!IsEnabled)
        {
            Logger.LogInformation("{ModuleName}: Module is disabled", ModuleName);
            return false;
        }

        Logger.LogInformation("{ModuleName}: Initializing with {Count} cameras", ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            // Initialize device states from configuration
            foreach (var device in _config.Devices)
            {
                _deviceStates[device.Id] = new CameraDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    Enabled = true
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered camera '{Name}' (ID: {Id})",
                    ModuleName, device.Name, device.Id);
            }

            // Start CameraController if configured
            if (_config.AutoStartController)
            {
                await StartCameraControllerAsync();
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

    private async Task StartCameraControllerAsync()
    {
        try
        {
            var exePath = _config.ControllerExePath;
            if (!File.Exists(exePath))
            {
                // Try relative to app directory
                exePath = Path.Combine(AppContext.BaseDirectory, _config.ControllerExePath);
            }

            if (!File.Exists(exePath))
            {
                Logger.LogWarning("{ModuleName}: CameraController.exe not found at {Path}",
                    ModuleName, _config.ControllerExePath);
                return;
            }

            Logger.LogInformation("{ModuleName}: Starting CameraController from {Path}", ModuleName, exePath);

            _controllerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--port {_config.ControllerApiPort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _controllerProcess.Exited += (s, e) =>
            {
                Logger.LogWarning("{ModuleName}: CameraController process exited", ModuleName);
                _controllerRunning = false;
            };

            _controllerProcess.Start();
            _controllerRunning = true;

            // Wait for the API to become available
            await WaitForControllerApiAsync();

            Logger.LogInformation("{ModuleName}: CameraController started on port {Port}",
                ModuleName, _config.ControllerApiPort);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Failed to start CameraController", ModuleName);
        }
    }

    private async Task WaitForControllerApiAsync()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_controllerApiUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // API not ready yet
            }
            await Task.Delay(500);
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
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                return null;

            // Try to get PTZ from CameraController
            if (_controllerRunning)
            {
                try
                {
                    var response = await _httpClient.GetAsync(
                        $"{_controllerApiUrl}/cameras/{state.Config.DeviceId}/ptz");
                    if (response.IsSuccessStatusCode)
                    {
                        var ptz = await response.Content.ReadFromJsonAsync<ControllerPtzResponse>();
                        if (ptz != null)
                        {
                            state.PtzPosition.Pan = ptz.Pan;
                            state.PtzPosition.Tilt = ptz.Tilt;
                            state.PtzPosition.Zoom = ptz.Zoom;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{ModuleName}: Failed to get PTZ from controller", ModuleName);
                }
            }

            return new PtzPosition
            {
                Pan = state.PtzPosition.Pan,
                Tilt = state.PtzPosition.Tilt,
                Zoom = state.PtzPosition.Zoom
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Set PTZ position.
    /// </summary>
    public async Task<PtzPosition?> SetPtzPositionAsync(string deviceId, double? pan, double? tilt, double? zoom)
    {
        CameraDeviceState? state;
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");
        }
        finally
        {
            _stateLock.Release();
        }

        // Disable auto-tracking when manually controlling PTZ
        if (state.AutoTrackingEnabled || state.AutoFramingEnabled)
        {
            await SetAutoTrackingAsync(deviceId, false);
        }

        var newPan = pan ?? state.PtzPosition.Pan;
        var newTilt = tilt ?? state.PtzPosition.Tilt;
        var newZoom = zoom ?? state.PtzPosition.Zoom;

        // Clamp values
        newPan = Math.Clamp(newPan, -1.0, 1.0);
        newTilt = Math.Clamp(newTilt, -1.0, 1.0);
        newZoom = Math.Clamp(newZoom, 0.0, 1.0);

        // Send to CameraController
        if (_controllerRunning)
        {
            try
            {
                var content = JsonContent.Create(new { pan = newPan, tilt = newTilt, zoom = newZoom });
                var response = await _httpClient.PutAsync(
                    $"{_controllerApiUrl}/cameras/{state.Config.DeviceId}/ptz",
                    content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Failed to set PTZ", ModuleName);
                throw;
            }
        }

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
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Camera device '{deviceId}' not found");
        }
        finally
        {
            _stateLock.Release();
        }

        // Send to CameraController
        if (_controllerRunning)
        {
            try
            {
                var content = JsonContent.Create(new { enabled });
                var response = await _httpClient.PutAsync(
                    $"{_controllerApiUrl}/cameras/{state.Config.DeviceId}/auto-framing",
                    content);
                // Don't throw on failure, just log
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("{ModuleName}: CameraController returned {StatusCode} for auto-tracking",
                        ModuleName, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Failed to set auto-tracking on controller", ModuleName);
            }
        }

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
                // Check controller health
                await CheckControllerHealthAsync();

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

    private async Task CheckControllerHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_controllerApiUrl}/health");
            _controllerRunning = response.IsSuccessStatusCode;
        }
        catch
        {
            _controllerRunning = false;
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

            if (_controllerRunning)
            {
                try
                {
                    var response = await _httpClient.GetAsync(
                        $"{_controllerApiUrl}/cameras/{state.Config.DeviceId}",
                        cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        state.Connected = true;
                        state.Health = DeviceHealth.Healthy;
                        state.LastSeen = DateTime.UtcNow;
                        state.Errors.Clear();
                    }
                    else
                    {
                        state.Connected = false;
                        state.Health = DeviceHealth.Offline;
                    }
                }
                catch (Exception ex)
                {
                    state.Connected = false;
                    state.Health = DeviceHealth.Unhealthy;
                    state.Errors.Add($"Health check failed: {ex.Message}");
                    if (state.Errors.Count > 10)
                        state.Errors = state.Errors.TakeLast(10).ToList();
                }
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

        // Stop CameraController process
        if (_controllerProcess != null && !_controllerProcess.HasExited)
        {
            try
            {
                _controllerProcess.Kill();
                await _controllerProcess.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error killing CameraController process", ModuleName);
            }
        }

        _httpClient.Dispose();

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }
}

/// <summary>
/// Response from CameraController PTZ endpoint.
/// </summary>
internal class ControllerPtzResponse
{
    [JsonPropertyName("pan")]
    public double Pan { get; set; }

    [JsonPropertyName("tilt")]
    public double Tilt { get; set; }

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; }
}
