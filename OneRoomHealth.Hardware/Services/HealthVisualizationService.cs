using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.ViewModels;

namespace OneRoomHealth.Hardware.Services;

/// <summary>
/// Event args for module health changes.
/// </summary>
public class ModuleHealthChangedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
    public required ModuleHealthViewModel ModuleHealth { get; init; }
}

/// <summary>
/// Service for aggregating hardware health data for visualization.
/// Subscribes to DeviceHealthChanged events from all modules and provides
/// aggregated health summaries for the UI.
/// </summary>
public class HealthVisualizationService : IDisposable
{
    private readonly HardwareManager _hardwareManager;
    private readonly ILogger<HealthVisualizationService>? _logger;
    private readonly ConcurrentDictionary<string, ModuleHealthViewModel> _moduleHealth = new();
    private readonly ConcurrentQueue<HealthEventViewModel> _recentEvents = new();
    private readonly Timer _pollingTimer;
    private readonly Timer _uptimeTimer;
    private readonly object _eventLock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private const int MaxEvents = 100;

    public event EventHandler<ModuleHealthChangedEventArgs>? HealthChanged;

    public SystemHealthSummary SystemSummary { get; } = new();

    public HealthVisualizationService(
        HardwareManager hardwareManager,
        ILogger<HealthVisualizationService>? logger = null)
    {
        _hardwareManager = hardwareManager ?? throw new ArgumentNullException(nameof(hardwareManager));
        _logger = logger;

        // Initialize module health entries
        InitializeModuleHealth();

        // Subscribe to health events from all modules
        SubscribeToModuleEvents();

        // Start polling timer (every 5 seconds as backup)
        _pollingTimer = new Timer(
            async _ => await PollHealthAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));

        // Start uptime timer (every second)
        _uptimeTimer = new Timer(
            _ => UpdateUptime(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        _logger?.LogInformation("HealthVisualizationService initialized");
    }

    private void InitializeModuleHealth()
    {
        // Define expected modules with their metadata
        var moduleDefinitions = new Dictionary<string, (string DisplayName, string Icon, string Description)>
        {
            ["Display"] = ("Display", "\uE7F4", "Novastar LED wall controller"),
            ["SystemAudio"] = ("System Audio", "\uE767", "Windows audio devices"),
            ["Microphone"] = ("Microphone", "\uE720", "Network microphones"),
            ["Speaker"] = ("Speaker", "\uE7F5", "Network speakers"),
            ["Lighting"] = ("Lighting", "\uE781", "DMX512 lighting fixtures"),
            ["Camera"] = ("Camera", "\uE714", "Huddly cameras with PTZ"),
            ["Biamp"] = ("Biamp", "\uE8B7", "Parlé VBC 2800 video conferencing")
        };

        foreach (var (moduleName, (displayName, icon, description)) in moduleDefinitions)
        {
            _moduleHealth[moduleName] = new ModuleHealthViewModel
            {
                ModuleName = moduleName,
                DisplayName = displayName,
                IconGlyph = icon,
                Description = description,
                IsEnabled = false,
                IsInitialized = false,
                OverallHealth = ModuleHealthStatus.NotImplemented
            };
        }

        // Update from actual registered modules
        foreach (var module in _hardwareManager.GetAllModules())
        {
            if (_moduleHealth.TryGetValue(module.ModuleName, out var vm))
            {
                vm.IsEnabled = module.IsEnabled;
                vm.IsInitialized = module.IsInitialized;
                vm.OverallHealth = module.IsEnabled
                    ? (module.IsInitialized ? ModuleHealthStatus.Healthy : ModuleHealthStatus.Initializing)
                    : ModuleHealthStatus.Disabled;
            }
        }

        UpdateSystemSummary();
    }

    private void SubscribeToModuleEvents()
    {
        foreach (var module in _hardwareManager.GetAllModules())
        {
            module.DeviceHealthChanged += OnDeviceHealthChanged;
            _logger?.LogDebug("Subscribed to health events for {ModuleName}", module.ModuleName);
        }
    }

    private void OnDeviceHealthChanged(object? sender, DeviceHealthChangedEventArgs e)
    {
        if (sender is not IHardwareModule module)
            return;

        _logger?.LogDebug("Health changed for {DeviceId} in {ModuleName}: {OldHealth} -> {NewHealth}",
            e.DeviceId, module.ModuleName, e.PreviousHealth, e.NewHealth);

        // Add event to history
        var healthEvent = new HealthEventViewModel
        {
            Timestamp = e.Timestamp,
            DeviceId = e.DeviceId,
            DeviceName = e.DeviceId, // Will be updated with actual name
            ModuleName = module.ModuleName,
            PreviousHealth = e.PreviousHealth,
            NewHealth = e.NewHealth,
            Message = e.ErrorMessage
        };

        lock (_eventLock)
        {
            _recentEvents.Enqueue(healthEvent);
            while (_recentEvents.Count > MaxEvents)
                _recentEvents.TryDequeue(out _);
        }

        // Update module health
        if (_moduleHealth.TryGetValue(module.ModuleName, out var moduleVm))
        {
            // Find and update device in the devices collection
            var deviceVm = moduleVm.Devices.FirstOrDefault(d => d.DeviceId == e.DeviceId);
            if (deviceVm != null)
            {
                deviceVm.Health = e.NewHealth;
                deviceVm.LastSeen = e.Timestamp;
                if (!string.IsNullOrEmpty(e.ErrorMessage))
                    deviceVm.LastError = e.ErrorMessage;
            }

            // Add to module's recent events
            moduleVm.RecentEvents.Insert(0, healthEvent);
            while (moduleVm.RecentEvents.Count > 50)
                moduleVm.RecentEvents.RemoveAt(moduleVm.RecentEvents.Count - 1);

            // Update module-level health
            UpdateModuleOverallHealth(moduleVm);
            moduleVm.LastUpdate = DateTime.UtcNow;

            // Notify listeners
            HealthChanged?.Invoke(this, new ModuleHealthChangedEventArgs
            {
                ModuleName = module.ModuleName,
                ModuleHealth = moduleVm
            });
        }

        UpdateSystemSummary();
    }

    private async Task PollHealthAsync()
    {
        try
        {
            foreach (var module in _hardwareManager.GetAllModules())
            {
                if (!_moduleHealth.TryGetValue(module.ModuleName, out var moduleVm))
                    continue;

                moduleVm.IsEnabled = module.IsEnabled;
                moduleVm.IsInitialized = module.IsInitialized;

                if (!module.IsEnabled)
                {
                    moduleVm.OverallHealth = ModuleHealthStatus.Disabled;
                    moduleVm.DeviceCount = 0;
                    moduleVm.HealthyCount = 0;
                    moduleVm.UnhealthyCount = 0;
                    moduleVm.OfflineCount = 0;
                    continue;
                }

                try
                {
                    var devices = await module.GetDevicesAsync();
                    moduleVm.DeviceCount = devices.Count;
                    moduleVm.HealthyCount = devices.Count(d => d.Health == DeviceHealth.Healthy);
                    moduleVm.UnhealthyCount = devices.Count(d => d.Health == DeviceHealth.Unhealthy);
                    moduleVm.OfflineCount = devices.Count(d => d.Health == DeviceHealth.Offline);

                    // Update devices collection
                    UpdateModuleDevices(moduleVm, devices, module.ModuleName);

                    // Determine overall module health
                    UpdateModuleOverallHealth(moduleVm);
                    moduleVm.LastUpdate = DateTime.UtcNow;
                    moduleVm.LastError = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error polling health for {ModuleName}", module.ModuleName);
                    moduleVm.OverallHealth = ModuleHealthStatus.Unhealthy;
                    moduleVm.LastError = ex.Message;
                }
            }

            UpdateSystemSummary();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in health polling loop");
        }
    }

    private void UpdateModuleDevices(ModuleHealthViewModel moduleVm, List<DeviceInfo> devices, string moduleName)
    {
        // Remove devices that no longer exist
        var existingIds = devices.Select(d => d.Id).ToHashSet();
        var toRemove = moduleVm.Devices.Where(d => !existingIds.Contains(d.DeviceId)).ToList();
        foreach (var device in toRemove)
            moduleVm.Devices.Remove(device);

        // Add or update devices
        foreach (var device in devices)
        {
            var existingVm = moduleVm.Devices.FirstOrDefault(d => d.DeviceId == device.Id);
            if (existingVm != null)
            {
                existingVm.Health = device.Health;
                existingVm.LastSeen = device.LastSeen ?? DateTime.UtcNow;
            }
            else
            {
                moduleVm.Devices.Add(new DeviceHealthViewModel
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    ModuleName = moduleName,
                    DeviceType = device.DeviceType,
                    Health = device.Health,
                    LastSeen = device.LastSeen ?? DateTime.UtcNow,
                    IsEnabled = true
                });
            }
        }
    }

    private void UpdateModuleOverallHealth(ModuleHealthViewModel moduleVm)
    {
        if (!moduleVm.IsEnabled)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Disabled;
            return;
        }

        if (!moduleVm.IsInitialized)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Initializing;
            return;
        }

        if (moduleVm.DeviceCount == 0)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Healthy;
            return;
        }

        if (moduleVm.HealthyCount == moduleVm.DeviceCount)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Healthy;
        }
        else if (moduleVm.HealthyCount > 0)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Degraded;
        }
        else if (moduleVm.UnhealthyCount > 0)
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Unhealthy;
        }
        else
        {
            moduleVm.OverallHealth = ModuleHealthStatus.Offline;
        }
    }

    private void UpdateSystemSummary()
    {
        var modules = _moduleHealth.Values.ToList();

        SystemSummary.TotalModules = modules.Count;
        SystemSummary.ActiveModules = modules.Count(m => m.IsEnabled && m.IsInitialized);
        SystemSummary.HealthyModules = modules.Count(m => m.OverallHealth == ModuleHealthStatus.Healthy);
        SystemSummary.TotalDevices = modules.Sum(m => m.DeviceCount);
        SystemSummary.HealthyDevices = modules.Sum(m => m.HealthyCount);
        SystemSummary.LastUpdate = DateTime.UtcNow;
    }

    private void UpdateUptime()
    {
        SystemSummary.Uptime = DateTime.UtcNow - _startTime;
    }

    /// <summary>
    /// Get all module health summaries.
    /// </summary>
    public IReadOnlyList<ModuleHealthViewModel> GetModuleHealthSummaries()
    {
        return _moduleHealth.Values.OrderBy(m => m.DisplayName).ToList();
    }

    /// <summary>
    /// Get detailed health for a specific module.
    /// </summary>
    public ModuleHealthViewModel? GetModuleHealth(string moduleName)
    {
        return _moduleHealth.TryGetValue(moduleName, out var vm) ? vm : null;
    }

    /// <summary>
    /// Get recent health events across all modules.
    /// </summary>
    public IReadOnlyList<HealthEventViewModel> GetRecentEvents(int count = 50)
    {
        lock (_eventLock)
        {
            return _recentEvents.Reverse().Take(count).ToList();
        }
    }

    /// <summary>
    /// Force refresh all module health data.
    /// </summary>
    public async Task RefreshAsync()
    {
        _logger?.LogInformation("Manual health refresh requested");
        await PollHealthAsync();
    }

    /// <summary>
    /// Perform diagnostic action on a module.
    /// </summary>
    public async Task<DiagnosticResult> ExecuteDiagnosticAsync(string moduleName, DiagnosticAction action)
    {
        _logger?.LogInformation("Executing diagnostic action {Action} on {ModuleName}", action, moduleName);

        var module = _hardwareManager.GetAllModules().FirstOrDefault(m => m.ModuleName == moduleName);
        if (module == null)
        {
            return new DiagnosticResult
            {
                Success = false,
                Message = $"Module '{moduleName}' not found"
            };
        }

        try
        {
            switch (action)
            {
                case DiagnosticAction.RestartModule:
                    await module.ShutdownAsync();
                    await Task.Delay(1000);
                    await module.InitializeAsync();
                    await module.StartMonitoringAsync();
                    return new DiagnosticResult
                    {
                        Success = true,
                        Message = $"Module '{moduleName}' restarted successfully"
                    };

                case DiagnosticAction.ForceRefresh:
                    await PollHealthAsync();
                    return new DiagnosticResult
                    {
                        Success = true,
                        Message = "Health data refreshed"
                    };

                case DiagnosticAction.TestConnection:
                    var devices = await module.GetDevicesAsync();
                    var healthyCount = devices.Count(d => d.Health == DeviceHealth.Healthy);
                    return new DiagnosticResult
                    {
                        Success = true,
                        Message = $"{healthyCount}/{devices.Count} devices responding",
                        Data = devices
                    };

                case DiagnosticAction.ExportLogs:
                    var events = GetRecentEvents(100);
                    var moduleEvents = events.Where(e => e.ModuleName == moduleName).ToList();
                    return new DiagnosticResult
                    {
                        Success = true,
                        Message = $"Exported {moduleEvents.Count} events",
                        Data = moduleEvents
                    };

                default:
                    return new DiagnosticResult
                    {
                        Success = false,
                        Message = $"Unknown action: {action}"
                    };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Diagnostic action {Action} failed for {ModuleName}", action, moduleName);
            return new DiagnosticResult
            {
                Success = false,
                Message = $"Action failed: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        _pollingTimer.Dispose();
        _uptimeTimer.Dispose();

        // Unsubscribe from events
        foreach (var module in _hardwareManager.GetAllModules())
        {
            module.DeviceHealthChanged -= OnDeviceHealthChanged;
        }

        _logger?.LogInformation("HealthVisualizationService disposed");
    }
}

/// <summary>
/// Diagnostic actions that can be performed on a module.
/// </summary>
public enum DiagnosticAction
{
    RestartModule,
    ForceRefresh,
    TestConnection,
    ExportLogs
}

/// <summary>
/// Result of a diagnostic action.
/// </summary>
public class DiagnosticResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
}
