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
/// Includes automatic restart with exponential backoff and phantom process cleanup.
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

    // Restart strategy tracking
    private int _consecutiveHealthFailures = 0;
    private int _restartAttempts = 0;
    private DateTime? _lastRestartTime = null;
    private DateTime? _gracePeriodEndTime = null;
    private bool _isRestarting = false;

    // Process names to clean up (phantom process cleanup)
    private static readonly string[] PhantomProcessNames = { "CameraController", "HuddlyDeviceManager" };

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
            // Clean up any phantom processes before starting
            await CleanupPhantomProcessesAsync();

            var exePath = ResolveControllerPath();
            if (exePath == null)
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
                Logger.LogWarning("{ModuleName}: CameraController process exited unexpectedly", ModuleName);
                _controllerRunning = false;
            };

            _controllerProcess.Start();
            _controllerRunning = true;
            _lastRestartTime = DateTime.UtcNow;

            // Wait for the API to become available
            bool apiReady = await WaitForControllerApiAsync();

            if (apiReady)
            {
                Logger.LogInformation("{ModuleName}: CameraController started on port {Port}",
                    ModuleName, _config.ControllerApiPort);

                // Set grace period - don't health check during this time
                _gracePeriodEndTime = DateTime.UtcNow.AddSeconds(_config.StartupGracePeriod);
                Logger.LogDebug("{ModuleName}: Grace period active until {Time}",
                    ModuleName, _gracePeriodEndTime);
            }
            else
            {
                Logger.LogWarning("{ModuleName}: CameraController started but API not responding",
                    ModuleName);
                _controllerRunning = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Failed to start CameraController", ModuleName);
            _controllerRunning = false;
        }
    }

    /// <summary>
    /// Resolve the path to CameraController.exe, trying multiple locations.
    /// </summary>
    private string? ResolveControllerPath()
    {
        var pathsToTry = new List<string>
        {
            _config.ControllerExePath,
            Path.Combine(AppContext.BaseDirectory, _config.ControllerExePath),
            Path.Combine(AppContext.BaseDirectory, "hardware", "huddly", "CameraController.exe"),
            Path.Combine(Environment.CurrentDirectory, _config.ControllerExePath)
        };

        foreach (var path in pathsToTry)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        Logger.LogWarning("{ModuleName}: CameraController.exe not found. Tried paths: {Paths}",
            ModuleName, string.Join(", ", pathsToTry));
        return null;
    }

    /// <summary>
    /// Kill any orphaned CameraController or related processes.
    /// This prevents port conflicts and resource leaks from previous crashes.
    /// </summary>
    private async Task CleanupPhantomProcessesAsync()
    {
        Logger.LogDebug("{ModuleName}: Checking for phantom processes...", ModuleName);

        foreach (var processName in PhantomProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    continue;

                Logger.LogInformation("{ModuleName}: Found {Count} phantom {Name} process(es), cleaning up...",
                    ModuleName, processes.Length, processName);

                foreach (var process in processes)
                {
                    try
                    {
                        // Skip our own managed process if it's still valid
                        if (_controllerProcess != null &&
                            !_controllerProcess.HasExited &&
                            process.Id == _controllerProcess.Id)
                        {
                            continue;
                        }

                        Logger.LogDebug("{ModuleName}: Killing phantom process {Name} (PID: {Pid})",
                            ModuleName, processName, process.Id);

                        process.Kill(entireProcessTree: true);
                        await Task.Run(() => process.WaitForExit(2000));

                        if (!process.HasExited)
                        {
                            Logger.LogWarning("{ModuleName}: Phantom process {Pid} did not exit, force killing",
                                ModuleName, process.Id);
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "{ModuleName}: Failed to kill phantom process {Pid}",
                            ModuleName, process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Error cleaning up {Name} processes",
                    ModuleName, processName);
            }
        }

        // Brief delay to ensure ports are released
        await Task.Delay(500);
    }

    /// <summary>
    /// Wait for the CameraController API to become available.
    /// </summary>
    /// <returns>True if API is responding, false if timeout.</returns>
    private async Task<bool> WaitForControllerApiAsync()
    {
        Logger.LogDebug("{ModuleName}: Waiting for CameraController API...", ModuleName);

        for (int i = 0; i < 30; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_controllerApiUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogDebug("{ModuleName}: CameraController API ready after {Attempts} attempts",
                        ModuleName, i + 1);
                    return true;
                }
            }
            catch
            {
                // API not ready yet
            }
            await Task.Delay(500);
        }

        Logger.LogWarning("{ModuleName}: CameraController API did not respond within timeout", ModuleName);
        return false;
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
                // Skip health checks during grace period after restart
                if (_gracePeriodEndTime.HasValue && DateTime.UtcNow < _gracePeriodEndTime.Value)
                {
                    Logger.LogDebug("{ModuleName}: In grace period, skipping health check", ModuleName);
                    await Task.Delay(interval, cancellationToken);
                    continue;
                }
                _gracePeriodEndTime = null;

                // Check controller health and handle restart if needed
                await CheckControllerHealthAsync(cancellationToken);

                // Only check devices if controller is running
                if (_controllerRunning)
                {
                    var deviceIds = _deviceStates.Keys.ToList();
                    foreach (var deviceId in deviceIds)
                    {
                        await CheckDeviceHealthAsync(deviceId, cancellationToken);
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
    /// Check controller health and trigger restart if needed.
    /// </summary>
    private async Task CheckControllerHealthAsync(CancellationToken cancellationToken)
    {
        // Don't check if we're currently restarting
        if (_isRestarting)
            return;

        bool wasRunning = _controllerRunning;

        try
        {
            var response = await _httpClient.GetAsync($"{_controllerApiUrl}/health", cancellationToken);
            _controllerRunning = response.IsSuccessStatusCode;

            if (_controllerRunning)
            {
                // Reset failure counter on success
                if (_consecutiveHealthFailures > 0)
                {
                    Logger.LogInformation("{ModuleName}: Controller health restored after {Failures} failures",
                        ModuleName, _consecutiveHealthFailures);
                }
                _consecutiveHealthFailures = 0;

                // Reset restart attempts after successful recovery
                // Note: Don't require wasRunning - controller may have been offline, restarted, and now healthy
                if (_restartAttempts > 0)
                {
                    Logger.LogInformation("{ModuleName}: Controller stable after {Attempts} restart attempt(s), resetting counter",
                        ModuleName, _restartAttempts);
                    _restartAttempts = 0;
                }
            }
            else
            {
                await HandleControllerHealthFailureAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Controller health check failed", ModuleName);
            _controllerRunning = false;
            await HandleControllerHealthFailureAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Handle a controller health check failure - increment counter and restart if threshold reached.
    /// </summary>
    private async Task HandleControllerHealthFailureAsync(CancellationToken cancellationToken)
    {
        _consecutiveHealthFailures++;

        Logger.LogWarning("{ModuleName}: Controller health failure #{Failures}/{Max}",
            ModuleName, _consecutiveHealthFailures, _config.MaxHealthFailures);

        if (_consecutiveHealthFailures >= _config.MaxHealthFailures)
        {
            if (_restartAttempts >= _config.MaxRestartAttempts)
            {
                Logger.LogError("{ModuleName}: Max restart attempts ({Max}) reached, controller offline",
                    ModuleName, _config.MaxRestartAttempts);
                return;
            }

            await RestartControllerWithBackoffAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Restart the controller with exponential backoff.
    /// </summary>
    private async Task RestartControllerWithBackoffAsync(CancellationToken cancellationToken)
    {
        if (_isRestarting)
            return;

        _isRestarting = true;
        _restartAttempts++;

        try
        {
            // Calculate backoff delay: 2^(attempt-1) seconds, capped at max
            var backoffSeconds = Math.Min(
                Math.Pow(2, _restartAttempts - 1),
                _config.MaxBackoffSeconds);

            Logger.LogWarning(
                "{ModuleName}: Restarting controller (attempt {Attempt}/{Max}, backoff: {Backoff}s)",
                ModuleName, _restartAttempts, _config.MaxRestartAttempts, backoffSeconds);

            // Determine if we should force kill
            bool forceKill = _restartAttempts > _config.ForceKillAfterAttempts;

            // Stop existing controller
            await StopControllerAsync(forceKill);

            // Wait backoff delay
            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);

            // Reset health failure counter
            _consecutiveHealthFailures = 0;

            // Start controller again
            await StartCameraControllerAsync();

            if (_controllerRunning)
            {
                Logger.LogInformation("{ModuleName}: Controller restart successful", ModuleName);
            }
            else
            {
                Logger.LogWarning("{ModuleName}: Controller restart attempt {Attempt} failed",
                    ModuleName, _restartAttempts);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Error during controller restart", ModuleName);
        }
        finally
        {
            _isRestarting = false;
        }
    }

    /// <summary>
    /// Stop the controller process.
    /// </summary>
    /// <param name="forceKill">If true, immediately kill; otherwise try graceful shutdown first.</param>
    private async Task StopControllerAsync(bool forceKill = false)
    {
        if (_controllerProcess == null)
        {
            // Clean up any orphaned processes
            await CleanupPhantomProcessesAsync();
            return;
        }

        try
        {
            if (_controllerProcess.HasExited)
            {
                Logger.LogDebug("{ModuleName}: Controller process already exited", ModuleName);
                _controllerProcess.Dispose();
                _controllerProcess = null;
                _controllerRunning = false;
                return;
            }

            var pid = _controllerProcess.Id;

            if (forceKill)
            {
                Logger.LogInformation("{ModuleName}: Force killing controller (PID: {Pid})", ModuleName, pid);
                _controllerProcess.Kill(entireProcessTree: true);
            }
            else
            {
                Logger.LogInformation("{ModuleName}: Gracefully stopping controller (PID: {Pid})", ModuleName, pid);

                // Try graceful shutdown first (CTRL+BREAK on Windows)
                try
                {
                    // Send Ctrl+Break signal for graceful shutdown
                    if (!_controllerProcess.CloseMainWindow())
                    {
                        // If no main window, just kill it
                        _controllerProcess.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    _controllerProcess.Kill(entireProcessTree: true);
                }
            }

            // Wait for exit with timeout
            var exitTask = Task.Run(() => _controllerProcess.WaitForExit(5000));
            await exitTask;

            if (!_controllerProcess.HasExited)
            {
                Logger.LogWarning("{ModuleName}: Controller did not exit gracefully, force killing", ModuleName);
                _controllerProcess.Kill(entireProcessTree: true);
                await Task.Run(() => _controllerProcess.WaitForExit(2000));
            }

            Logger.LogInformation("{ModuleName}: Controller stopped", ModuleName);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Error stopping controller", ModuleName);
        }
        finally
        {
            _controllerProcess?.Dispose();
            _controllerProcess = null;
            _controllerRunning = false;
        }

        // Clean up any remaining phantom processes
        await CleanupPhantomProcessesAsync();
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

        // Stop CameraController process (graceful, then force if needed)
        await StopControllerAsync(forceKill: false);

        // Reset restart tracking
        _consecutiveHealthFailures = 0;
        _restartAttempts = 0;
        _lastRestartTime = null;
        _gracePeriodEndTime = null;

        // Clear device states (don't dispose _stateLock or _httpClient - they're reused on re-enable)
        await _stateLock.WaitAsync();
        try
        {
            _deviceStates.Clear();
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Shutdown complete, state cleared", ModuleName);
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
