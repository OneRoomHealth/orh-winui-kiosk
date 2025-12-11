using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using System.Diagnostics;

namespace OneRoomHealth.Hardware.Modules.Chromium;

/// <summary>
/// Hardware module for managing Chromium/Chrome browser instances.
/// Supports process lifecycle, multi-monitor, and remote navigation via CDP.
/// </summary>
public class ChromiumModule : HardwareModuleBase
{
    private readonly HttpClient _httpClient;
    private readonly ChromiumConfiguration _config;
    private readonly Dictionary<string, ChromiumDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public override string ModuleName => "Chromium";

    public ChromiumModule(
        ILogger<ChromiumModule> logger,
        ChromiumConfiguration config,
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

        Logger.LogInformation("{ModuleName}: Initializing with {Count} browser instances", ModuleName, _config.Devices.Count);

        await _stateLock.WaitAsync();
        try
        {
            foreach (var device in _config.Devices)
            {
                var chromiumPath = FindChromiumExecutable(device.ChromiumPath);

                _deviceStates[device.Id] = new ChromiumDeviceState
                {
                    Config = device,
                    Health = DeviceHealth.Offline,
                    CdpPort = 9222 + int.Parse(device.Id) // Unique port per instance
                };

                Logger.LogInformation(
                    "{ModuleName}: Registered browser '{Name}' (ID: {Id}), Chromium path: {Path}",
                    ModuleName, device.Name, device.Id, chromiumPath ?? "auto-detect");

                // Auto-start if configured
                if (device.AutoStart)
                {
                    Logger.LogInformation("{ModuleName}: Auto-starting browser '{Name}'", ModuleName, device.Name);
                    await OpenBrowserAsync(device.Id);
                }
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
                Model = "Chromium Browser",
                Health = state.Health,
                LastSeen = state.LastSeen,
                DeviceType = "Chromium"
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

            return new ChromiumStatus
            {
                Id = state.Config.Id,
                Name = state.Config.Name,
                Health = state.Health,
                LastSeen = state.LastSeen,
                IsRunning = state.IsRunning,
                ProcessId = state.ProcessId,
                CurrentUrl = state.CurrentUrl,
                ChromiumPath = state.Config.ChromiumPath,
                UserDataDir = state.Config.UserDataDir,
                DisplayMode = state.Config.DisplayMode,
                Errors = state.Errors.ToList()
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Open/start a browser instance.
    /// </summary>
    public async Task OpenBrowserAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        ChromiumDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Chromium instance '{deviceId}' not found");

            // Check if already running
            if (state.IsRunning)
            {
                Logger.LogInformation("{ModuleName}: Browser '{Name}' is already running", ModuleName, state.Config.Name);
                return;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Starting browser '{Name}'", ModuleName, state.Config.Name);

        var chromiumPath = FindChromiumExecutable(state.Config.ChromiumPath);
        if (chromiumPath == null)
            throw new FileNotFoundException("Chromium executable not found");

        var userDataDir = Environment.ExpandEnvironmentVariables(
            state.Config.UserDataDir ?? Path.Combine(Path.GetTempPath(), $"chromium-{deviceId}"));

        var args = BuildChromiumArguments(state.Config, state.CdpPort, userDataDir);

        var psi = new ProcessStartInfo
        {
            FileName = chromiumPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start Chromium process");

        await _stateLock.WaitAsync();
        try
        {
            state.Process = process;
            state.Health = DeviceHealth.Healthy;
            state.LastSeen = DateTime.UtcNow;
            state.CurrentUrl = state.Config.DefaultUrl;
            state.Errors.Clear();

            // Subscribe to exit event
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => OnProcessExited(deviceId);
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation(
            "{ModuleName}: Browser '{Name}' started (PID: {Pid})",
            ModuleName, state.Config.Name, process.Id);
    }

    /// <summary>
    /// Close/stop a browser instance.
    /// </summary>
    public async Task CloseBrowserAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        ChromiumDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Chromium instance '{deviceId}' not found");

            if (!state.IsRunning)
            {
                Logger.LogInformation("{ModuleName}: Browser '{Name}' is not running", ModuleName, state.Config.Name);
                return;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Closing browser '{Name}'", ModuleName, state.Config.Name);

        try
        {
            state.Process?.Kill(entireProcessTree: true);
            state.Process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("{ModuleName}: Error killing process: {Error}", ModuleName, ex.Message);
        }

        await _stateLock.WaitAsync();
        try
        {
            state.Process = null;
            state.Health = DeviceHealth.Offline;
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Browser '{Name}' closed", ModuleName, state.Config.Name);
    }

    /// <summary>
    /// Navigate browser to a URL (via CDP or restart with URL).
    /// </summary>
    public async Task NavigateToUrlAsync(string deviceId, string url)
    {
        await _stateLock.WaitAsync();
        ChromiumDeviceState? state;
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out state))
                throw new KeyNotFoundException($"Chromium instance '{deviceId}' not found");
        }
        finally
        {
            _stateLock.Release();
        }

        Logger.LogInformation("{ModuleName}: Navigating browser '{Name}' to {Url}", ModuleName, state.Config.Name, url);

        // Try CDP first if browser is running
        if (state.IsRunning)
        {
            var cdp = new ChromeDevToolsProtocol(_httpClient, Logger, state.CdpPort);
            var success = await cdp.NavigateToUrlAsync(url);

            if (success)
            {
                await _stateLock.WaitAsync();
                try
                {
                    state.CurrentUrl = url;
                }
                finally
                {
                    _stateLock.Release();
                }
                Logger.LogInformation("{ModuleName}: Navigation successful via CDP", ModuleName);
                return;
            }
        }

        // Fallback: close and reopen with URL
        Logger.LogInformation("{ModuleName}: CDP navigation failed, restarting browser with URL", ModuleName);
        await CloseBrowserAsync(deviceId);

        await _stateLock.WaitAsync();
        try
        {
            state.Config.DefaultUrl = url; // Temporarily update default URL
        }
        finally
        {
            _stateLock.Release();
        }

        await OpenBrowserAsync(deviceId);
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
                foreach (var id in deviceIds)
                {
                    await CheckDeviceHealthAsync(id, cancellationToken);
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
        ChromiumDeviceState? state;
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
        var isRunning = state.IsRunning;

        if (isRunning)
        {
            // Check if CDP is available
            var cdp = new ChromeDevToolsProtocol(_httpClient, Logger, state.CdpPort);
            var cdpAvailable = await cdp.IsAvailableAsync();

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                state.Health = cdpAvailable ? DeviceHealth.Healthy : DeviceHealth.Unhealthy;
                state.LastSeen = DateTime.UtcNow;

                if (!cdpAvailable)
                {
                    state.Errors = new List<string> { "CDP not responding" };
                }
                else
                {
                    state.Errors.Clear();
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
        else
        {
            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                state.Health = DeviceHealth.Offline;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        // Raise event if health changed
        if (previousHealth != state.Health)
        {
            OnDeviceHealthChanged(new DeviceHealthChangedEventArgs
            {
                DeviceId = deviceId,
                NewHealth = state.Health,
                PreviousHealth = previousHealth
            });
        }
    }

    private void OnProcessExited(string deviceId)
    {
        Logger.LogWarning("{ModuleName}: Browser process exited for device {Id}", ModuleName, deviceId);

        _ = Task.Run(async () =>
        {
            await _stateLock.WaitAsync();
            try
            {
                if (_deviceStates.TryGetValue(deviceId, out var state))
                {
                    state.Process = null;
                    state.Health = DeviceHealth.Offline;
                }
            }
            finally
            {
                _stateLock.Release();
            }
        });
    }

    private string? FindChromiumExecutable(string? configuredPath)
    {
        if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        // Common Chromium/Chrome locations on Windows
        var searchPaths = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe"),
            @"C:\Program Files\Chromium\Application\chrome.exe"
        };

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private string BuildChromiumArguments(ChromiumInstanceConfig config, int cdpPort, string userDataDir)
    {
        var args = new List<string>
        {
            $"--remote-debugging-port={cdpPort}",
            $"--user-data-dir=\"{userDataDir}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-sync",
            "--metrics-recording-only",
            "--disable-default-apps",
            "--no-pings",
            "--password-store=basic",
            "--use-mock-keychain",
            "--disable-features=TranslateUI",
            "--disable-popup-blocking",
            "--disable-prompt-on-repost",
            "--disable-hang-monitor",
            "--disable-client-side-phishing-detection",
            "--autoplay-policy=no-user-gesture-required"
        };

        // Audio mute
        if (config.MuteAudio)
        {
            args.Add("--mute-audio");
        }

        // Display mode
        switch (config.DisplayMode.ToLowerInvariant())
        {
            case "kiosk":
                args.Add("--kiosk");
                args.Add("--kiosk-printing");
                break;
            case "fullscreen":
                args.Add("--start-fullscreen");
                break;
        }

        // Window size (for non-kiosk/fullscreen modes)
        if (config.WindowSize != null && config.WindowSize.Length == 2)
        {
            args.Add($"--window-size={config.WindowSize[0]},{config.WindowSize[1]}");
        }

        // Default URL
        if (!string.IsNullOrEmpty(config.DefaultUrl))
        {
            args.Add($"\"{config.DefaultUrl}\"");
        }

        return string.Join(" ", args);
    }

    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down all browser instances", ModuleName);

        await _stateLock.WaitAsync();
        var deviceIds = _deviceStates.Keys.ToList();
        _stateLock.Release();

        foreach (var deviceId in deviceIds)
        {
            try
            {
                await CloseBrowserAsync(deviceId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error closing browser {Id}", ModuleName, deviceId);
            }
        }

        await base.ShutdownAsync();
    }
}
