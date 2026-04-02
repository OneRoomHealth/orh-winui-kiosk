using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using OneRoomHealth.Hardware.Services.ImageDelivery;
using Windows.Devices.Enumeration;

namespace OneRoomHealth.Hardware.Modules.Firefly;

/// <summary>
/// Hardware module for Firefly UVC otoscope cameras (DE300/DE400/DE500/GT700/GT800).
///
/// Architecture:
///   1. Manages the FireflyCapture.Bridge.exe child process (32-bit, loads SnapDll.dll).
///   2. Enumerates connected Firefly devices via Windows.Devices.Enumeration using
///      the known USB Vendor ID (0x21CD) found in the SnapDll binary.
///   3. Subscribes to the bridge SSE stream (GET /events) to receive hardware
///      button-press events and route them through the same capture path as API calls.
///   4. Performs still-image capture via Windows.Media.Capture.MediaCapture (WinRT).
///   5. Forwards captured JPEG bytes to a configurable downstream endpoint using
///      the configured IImageDeliveryStrategy (multipart / base64 / raw).
/// </summary>
public sealed class FireflyModule : HardwareModuleBase, IAsyncDisposable
{
    // USB Vendor ID for Firefly Medical cameras (confirmed from SnapDll binary strings).
    private const string FireflyVendorId = "VID_21CD";

    // Known PID → model mapping extracted from SnapDll string table.
    private static readonly Dictionary<string, string> PidToModel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PID_603B"] = "DE300",
        ["PID_603C"] = "DE400",
        ["PID_703A"] = "GT700",
        ["PID_703B"] = "GT800",
    };

    private readonly FireflyConfiguration _config;
    private readonly HttpClient _bridgeClient;
    private readonly HttpClient? _deliveryClient;
    private readonly IImageDeliveryStrategy? _deliveryStrategy;
    private readonly Dictionary<string, FireflyDeviceState> _deviceStates = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private Process? _bridgeProcess;
    private int _restartAttempts;
    private Task? _sseConsumerTask;
    private CancellationTokenSource? _sseCts;
    private readonly List<Task> _pendingCaptures = new();
    private readonly object _pendingCapturesLock = new();

    public override string ModuleName => "Firefly";

    /// <summary>
    /// Event raised when the hardware snap button is pressed.
    /// Subscribers may use this to trigger UI reactions independently of the capture pipeline.
    /// </summary>
    public event EventHandler<string>? SnapButtonPressed;

    public FireflyModule(
        ILogger<FireflyModule> logger,
        FireflyConfiguration config,
        HttpClient httpClient)
        : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _bridgeClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        IsEnabled = config.Enabled;

        if (config.Downstream.Enabled && !string.IsNullOrWhiteSpace(config.Downstream.Url))
        {
            try
            {
                _deliveryClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(config.Downstream.TimeoutSeconds)
                };
                _deliveryStrategy = ImageDeliveryStrategyFactory.Create(
                    config.Downstream, _deliveryClient, logger);

                Logger.LogInformation(
                    "{ModuleName}: Downstream delivery configured — method={Method}, url={Url}",
                    ModuleName, config.Downstream.Method, config.Downstream.Url);
            }
            catch (ArgumentException ex)
            {
                Logger.LogWarning(ex, "{ModuleName}: Invalid downstream method, delivery disabled", ModuleName);
            }
        }
        else
        {
            Logger.LogInformation("{ModuleName}: Downstream delivery disabled", ModuleName);
        }
    }

    // -------------------------------------------------------------------------
    // IHardwareModule lifecycle
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override async Task<bool> InitializeAsync()
    {
        if (!IsEnabled)
        {
            Logger.LogInformation("{ModuleName}: Module is disabled", ModuleName);
            return false;
        }

        Logger.LogInformation("{ModuleName}: Initializing", ModuleName);

        // Start the 32-bit bridge process
        var bridgeStarted = await StartBridgeProcessAsync();
        if (!bridgeStarted)
        {
            Logger.LogWarning(
                "{ModuleName}: Bridge process failed to start — device enumeration and " +
                "button detection will be unavailable. Image capture may still work.",
                ModuleName);
        }

        // Enumerate Firefly devices
        await EnumerateDevicesAsync();

        IsInitialized = true;
        Logger.LogInformation("{ModuleName}: Initialization complete — {Count} device(s) found",
            ModuleName, _deviceStates.Count);
        return true;
    }

    /// <inheritdoc/>
    public override async Task StartMonitoringAsync()
    {
        await base.StartMonitoringAsync();

        // Start SSE consumer for bridge button events
        _sseCts = new CancellationTokenSource();
        _sseConsumerTask = Task.Run(
            () => ConsumeBridgeSseAsync(_sseCts.Token));
    }

    /// <inheritdoc/>
    public override async Task StopMonitoringAsync()
    {
        if (_sseCts != null)
        {
            _sseCts.Cancel();
            try { await (_sseConsumerTask ?? Task.CompletedTask); }
            catch (OperationCanceledException) { }
            _sseCts.Dispose();
            _sseCts = null;
        }

        // The SSE consumer has exited so no new capture tasks can be added.
        // Wait for any already-running fire-and-forget captures to finish before
        // returning — callers may dispose _stateLock immediately after this.
        // A 3-second timeout prevents a hung capture from blocking application shutdown.
        Task[] pending;
        lock (_pendingCapturesLock) { pending = _pendingCaptures.ToArray(); }
        if (pending.Length > 0)
        {
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await Task.WhenAll(pending).WaitAsync(drainCts.Token); } catch { }
        }

        await base.StopMonitoringAsync();
    }

    /// <inheritdoc/>
    public override async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);
        await StopMonitoringAsync();
        await StopBridgeProcessAsync();
        await base.ShutdownAsync();

        await _stateLock.WaitAsync();
        try { _deviceStates.Clear(); }
        finally { _stateLock.Release(); }

        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }

    // -------------------------------------------------------------------------
    // IHardwareModule queries
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override async Task<List<DeviceInfo>> GetDevicesAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            return _deviceStates.Values.Select(s => new DeviceInfo
            {
                Id = s.Id,
                Name = s.FriendlyName,
                Model = s.Model,
                Health = s.Health,
                LastSeen = s.LastSeen,
                DeviceType = "FireflyCamera"
            }).ToList();
        }
        finally { _stateLock.Release(); }
    }

    /// <inheritdoc/>
    public override async Task<object?> GetDeviceStatusAsync(string deviceId)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                return null;

            return new FireflyDeviceStatus
            {
                Id = state.Id,
                FriendlyName = state.FriendlyName,
                Model = state.Model,
                DeviceInterfaceId = state.DeviceInterfaceId,
                Health = state.Health,
                IsConnected = state.IsConnected,
                LastSeen = state.LastSeen,
                CaptureCount = state.CaptureCount,
                LastCaptureAt = state.LastCaptureAt,
                Errors = state.Errors.ToList()
            };
        }
        finally { _stateLock.Release(); }
    }

    // -------------------------------------------------------------------------
    // On-demand enumeration (for debug UI refresh)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces an immediate re-enumeration of connected Firefly devices,
    /// bypassing the periodic monitoring interval. Intended for the debug UI
    /// refresh button so results reflect the current hardware state.
    /// </summary>
    public Task RefreshDevicesAsync() => EnumerateDevicesAsync();

    // -------------------------------------------------------------------------
    // Capture
    // -------------------------------------------------------------------------

    /// <summary>
    /// Triggers a still-image capture from the specified Firefly device,
    /// returns the raw JPEG bytes, and asynchronously forwards to downstream
    /// if configured.
    /// </summary>
    /// <param name="deviceId">Logical device ID (e.g., "firefly-0").</param>
    /// <returns>JPEG image bytes, or throws on failure.</returns>
    public async Task<byte[]> TriggerCaptureAsync(string deviceId)
    {
        string friendlyName;
        string deviceInterfaceId;

        await _stateLock.WaitAsync();
        try
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
                throw new KeyNotFoundException($"Firefly device '{deviceId}' not found");

            if (!state.IsConnected)
                throw new InvalidOperationException($"Firefly device '{deviceId}' is not connected");

            // Snapshot mutable fields while the lock is held so they can't be
            // concurrently modified by EnumerateDevicesAsync outside the lock.
            friendlyName = state.FriendlyName;
            deviceInterfaceId = state.DeviceInterfaceId;
        }
        finally { _stateLock.Release(); }

        Logger.LogInformation("{ModuleName}: Triggering capture on device '{Name}'",
            ModuleName, friendlyName);

        var imageBytes = await CaptureFromDeviceAsync(deviceInterfaceId);

        // Update capture stats — re-fetch state and verify the physical device interface
        // ID still matches the one used for capture. If the device was swapped during
        // the (potentially long) capture operation, skip the update to avoid attributing
        // stats to the wrong physical device.
        await _stateLock.WaitAsync();
        try
        {
            if (_deviceStates.TryGetValue(deviceId, out var currentState)
                && currentState.DeviceInterfaceId == deviceInterfaceId)
            {
                currentState.CaptureCount++;
                currentState.LastCaptureAt = DateTime.UtcNow;
            }
        }
        finally { _stateLock.Release(); }

        Logger.LogInformation(
            "{ModuleName}: Capture complete — {Bytes} bytes from '{Name}'",
            ModuleName, imageBytes.Length, friendlyName);

        // Forward to downstream asynchronously — do not block the API response
        if (_deliveryStrategy != null)
        {
            _ = Task.Run(async () =>
            {
                var result = await _deliveryStrategy.DeliverAsync(imageBytes, "image/jpeg");
                if (!result.Success)
                {
                    Logger.LogWarning(
                        "{ModuleName}: Downstream delivery failed — status={Status}, message={Msg}",
                        ModuleName, result.StatusCode, result.Message);
                }
            });
        }

        return imageBytes;
    }

    // -------------------------------------------------------------------------
    // Private: capture implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Captures a JPEG still from the Firefly UVC camera using WinRT MediaCapture.
    /// Runs on a dedicated STA thread as required by some WinRT camera APIs.
    /// </summary>
    private static async Task<byte[]> CaptureFromDeviceAsync(string deviceInterfaceId)
    {
        // MediaCapture requires STA on some Windows builds; marshal to a dedicated thread.
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staThread = new Thread(() =>
        {
            // All WinRT work runs directly on this STA thread.
            // Each WinRT async operation is blocked with ConfigureAwait(false) +
            // GetAwaiter().GetResult(): ConfigureAwait(false) prevents the runtime
            // from trying to marshal continuations back to this thread's
            // synchronization context (which has no message pump), avoiding deadlock.
            try
            {
                using var capture = new Windows.Media.Capture.MediaCapture();

                var settings = new Windows.Media.Capture.MediaCaptureInitializationSettings
                {
                    VideoDeviceId = deviceInterfaceId,
                    StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video,
                    // ExclusiveControl: prevents other apps from accessing the camera simultaneously
                    SharingMode = Windows.Media.Capture.MediaCaptureSharingMode.ExclusiveControl
                };

                capture.InitializeAsync(settings)
                    .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

                // StartPreviewAsync activates the camera video stream.
                // CapturePhotoToStreamAsync requires the stream to be active;
                // without this call, it throws "Hardware MFT failed to start streaming".
                capture.StartPreviewAsync()
                    .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

                // Capture at the device's native resolution
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                capture.CapturePhotoToStreamAsync(
                        Windows.Media.MediaProperties.ImageEncodingProperties.CreateJpeg(),
                        stream)
                    .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

                try
                {
                    capture.StopPreviewAsync()
                        .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch { /* best-effort cleanup */ }

                stream.Seek(0);
                var bytes = new byte[stream.Size];
                using var reader = new Windows.Storage.Streams.DataReader(stream);
                reader.LoadAsync((uint)stream.Size)
                    .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
                reader.ReadBytes(bytes);

                tcs.SetResult(bytes);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();

        return await tcs.Task;
    }

    // -------------------------------------------------------------------------
    // Private: device enumeration
    // -------------------------------------------------------------------------

    private async Task EnumerateDevicesAsync()
    {
        try
        {
            // AQS selector for video capture devices
            var selector = Windows.Media.Devices.MediaDevice.GetVideoCaptureSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);

            var fireflyDevices = devices
                .Where(d => d.Id.Contains(FireflyVendorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Logger.LogInformation(
                "{ModuleName}: Found {Count} Firefly device(s) out of {Total} video capture device(s)",
                ModuleName, fireflyDevices.Count, devices.Count);

            await _stateLock.WaitAsync();
            try
            {
                // Mark all existing devices as offline before refresh
                foreach (var s in _deviceStates.Values)
                {
                    s.IsConnected = false;
                    s.Health = DeviceHealth.Offline;
                }

                int index = 0;
                foreach (var device in fireflyDevices)
                {
                    var id = $"firefly-{index++}";
                    var model = InferModel(device.Id);

                    if (_deviceStates.TryGetValue(id, out var existing))
                    {
                        // Update existing entry
                        existing.FriendlyName = device.Name;
                        existing.DeviceInterfaceId = device.Id;
                        existing.Model = model;
                        existing.IsConnected = true;
                        existing.Health = DeviceHealth.Healthy;
                        existing.LastSeen = DateTime.UtcNow;
                    }
                    else
                    {
                        _deviceStates[id] = new FireflyDeviceState
                        {
                            Id = id,
                            FriendlyName = device.Name,
                            Model = model,
                            DeviceInterfaceId = device.Id,
                            IsConnected = true,
                            Health = DeviceHealth.Healthy,
                            LastSeen = DateTime.UtcNow
                        };

                        Logger.LogInformation(
                            "{ModuleName}: Registered device '{Name}' (ID: {Id}, Model: {Model})",
                            ModuleName, device.Name, id, model);
                    }
                }
            }
            finally { _stateLock.Release(); }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Device enumeration failed", ModuleName);
        }
    }

    private static string InferModel(string deviceId)
    {
        foreach (var (pid, model) in PidToModel)
        {
            if (deviceId.Contains(pid, StringComparison.OrdinalIgnoreCase))
                return model;
        }
        return "Unknown";
    }

    // -------------------------------------------------------------------------
    // Private: bridge process management
    // -------------------------------------------------------------------------

    private async Task<bool> StartBridgeProcessAsync()
    {
        var exePath = Path.IsPathRooted(_config.BridgeExePath)
            ? _config.BridgeExePath
            : Path.Combine(AppContext.BaseDirectory, _config.BridgeExePath);

        if (!File.Exists(exePath))
        {
            Logger.LogWarning(
                "{ModuleName}: Bridge exe not found at '{Path}' — bridge will not start",
                ModuleName, exePath);
            return false;
        }

        await StopBridgeProcessAsync();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            _bridgeProcess = Process.Start(psi);
            if (_bridgeProcess == null)
            {
                Logger.LogError("{ModuleName}: Process.Start returned null for bridge", ModuleName);
                return false;
            }

            _bridgeProcess.EnableRaisingEvents = true;
            _bridgeProcess.Exited += OnBridgeProcessExited;

            // Redirect output for diagnostics
            _bridgeProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Logger.LogDebug("[Bridge] {Line}", e.Data);
            };
            _bridgeProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Logger.LogWarning("[Bridge] {Line}", e.Data);
            };
            _bridgeProcess.BeginOutputReadLine();
            _bridgeProcess.BeginErrorReadLine();

            Logger.LogInformation(
                "{ModuleName}: Bridge process started (PID {Pid})", ModuleName, _bridgeProcess.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Failed to start bridge process", ModuleName);
            return false;
        }

        // Poll /health until the bridge is ready
        var deadline = DateTime.UtcNow.AddSeconds(_config.StartupGracePeriodSeconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await _bridgeClient.GetAsync(
                    $"http://localhost:{_config.BridgePort}/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("{ModuleName}: Bridge is healthy", ModuleName);
                    _restartAttempts = 0;
                    return true;
                }
            }
            catch { /* not ready yet */ }

            await Task.Delay(500);
        }

        Logger.LogError(
            "{ModuleName}: Bridge did not become healthy within {Seconds}s",
            ModuleName, _config.StartupGracePeriodSeconds);
        return false;
    }

    private async Task StopBridgeProcessAsync()
    {
        if (_bridgeProcess == null) return;

        try
        {
            if (!_bridgeProcess.HasExited)
            {
                _bridgeProcess.Kill(entireProcessTree: true);

                // After Kill the process should exit within milliseconds.
                // Cap the async wait at 500 ms — if it has not exited by then
                // the OS will reap it when our process exits.
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                try
                {
                    await _bridgeProcess.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogWarning(
                        "{ModuleName}: Bridge process did not exit within 500 ms after Kill — continuing shutdown",
                        ModuleName);
                }

                Logger.LogInformation("{ModuleName}: Bridge process stopped", ModuleName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Error stopping bridge process", ModuleName);
        }
        finally
        {
            _bridgeProcess.Dispose();
            _bridgeProcess = null;
        }
    }

    private async void OnBridgeProcessExited(object? sender, EventArgs e)
    {
        try
        {
            await HandleBridgeProcessExitedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Unhandled error in bridge exit handler", ModuleName);
        }
    }

    private async Task HandleBridgeProcessExitedAsync()
    {
        Logger.LogWarning("{ModuleName}: Bridge process exited unexpectedly", ModuleName);

        if (_restartAttempts >= _config.MaxRestartAttempts)
        {
            Logger.LogError(
                "{ModuleName}: Max restart attempts ({Max}) reached — marking module degraded",
                ModuleName, _config.MaxRestartAttempts);

            await _stateLock.WaitAsync();
            try
            {
                foreach (var s in _deviceStates.Values)
                    s.Health = DeviceHealth.Unhealthy;
            }
            finally { _stateLock.Release(); }
            return;
        }

        _restartAttempts++;
        var delay = TimeSpan.FromSeconds(Math.Min(5 * _restartAttempts, 30));
        Logger.LogInformation(
            "{ModuleName}: Restarting bridge (attempt {Attempt}/{Max}) in {Delay}s",
            ModuleName, _restartAttempts, _config.MaxRestartAttempts, delay.TotalSeconds);

        await Task.Delay(delay);
        await StartBridgeProcessAsync();
    }

    // -------------------------------------------------------------------------
    // Private: bridge SSE consumer
    // -------------------------------------------------------------------------

    private async Task ConsumeBridgeSseAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("{ModuleName}: Starting bridge SSE consumer", ModuleName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await _bridgeClient.GetAsync(
                    $"http://localhost:{_config.BridgePort}/events",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line?.StartsWith("data: ") == true)
                    {
                        Logger.LogDebug("{ModuleName}: Bridge button press event received", ModuleName);

                        // Raise the button press event — snapshot the key under the lock
                        // to avoid racing with EnumerateDevicesAsync modifying _deviceStates.
                        string? firstDeviceId;
                        await _stateLock.WaitAsync(cancellationToken);
                        try
                        {
                            firstDeviceId = _deviceStates.Keys.FirstOrDefault();
                        }
                        finally { _stateLock.Release(); }

                        if (firstDeviceId != null)
                            SnapButtonPressed?.Invoke(this, firstDeviceId);
                        // Capture is now handled by the SnapButtonPressed subscriber
                        // (MainWindow) via the JS-side WebView path.  The browser owns
                        // the Firefly device exclusively while WebRTC is active, so the
                        // old native MediaCapture path that used to run here would always
                        // conflict and fail.
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "{ModuleName}: Bridge SSE connection lost, reconnecting in 2s", ModuleName);
                await Task.Delay(2000, cancellationToken);
            }
        }

        Logger.LogInformation("{ModuleName}: Bridge SSE consumer stopped", ModuleName);
    }

    // -------------------------------------------------------------------------
    // Private: periodic monitoring
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
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
                await EnumerateDevicesAsync();
                await CheckBridgeHealthAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ModuleName}: Error in monitoring loop", ModuleName);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task CheckBridgeHealthAsync()
    {
        if (_bridgeProcess == null || _bridgeProcess.HasExited)
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _bridgeClient.GetAsync(
                $"http://localhost:{_config.BridgePort}/health", cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("{ModuleName}: Bridge /health returned {Status}",
                    ModuleName, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ModuleName}: Bridge health check failed", ModuleName);
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        _stateLock.Dispose();
        _deliveryClient?.Dispose();
    }
}
