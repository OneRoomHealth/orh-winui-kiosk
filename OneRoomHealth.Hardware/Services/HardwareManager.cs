using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Abstractions;

namespace OneRoomHealth.Hardware.Services;

/// <summary>
/// Central manager for all hardware modules.
/// Provides lifecycle management and module registry.
/// </summary>
public class HardwareManager
{
    private readonly ILogger<HardwareManager> _logger;
    private readonly Dictionary<string, IHardwareModule> _modules = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized = false;

    public HardwareManager(ILogger<HardwareManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Register a hardware module with the manager.
    /// </summary>
    /// <param name="module">The module to register.</param>
    public void RegisterModule(IHardwareModule module)
    {
        if (module == null)
            throw new ArgumentNullException(nameof(module));

        _modules[module.ModuleName] = module;
        _logger.LogInformation("Registered hardware module: {ModuleName}", module.ModuleName);
    }

    /// <summary>
    /// Get a registered module by name.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    /// <param name="moduleName">The module name.</param>
    /// <returns>The module instance, or null if not found.</returns>
    public T? GetModule<T>(string moduleName) where T : class, IHardwareModule
    {
        return _modules.TryGetValue(moduleName, out var module) ? module as T : null;
    }

    /// <summary>
    /// Get all registered modules.
    /// </summary>
    public IEnumerable<IHardwareModule> GetAllModules()
    {
        return _modules.Values;
    }

    /// <summary>
    /// Initialize all registered modules.
    /// </summary>
    public async Task<bool> InitializeAllModulesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                _logger.LogWarning("Hardware modules already initialized");
                return true;
            }

            _logger.LogInformation("Initializing {Count} hardware modules", _modules.Count);

            var initTasks = _modules.Values
                .Select(async module =>
                {
                    try
                    {
                        var success = await module.InitializeAsync();
                        if (success)
                        {
                            _logger.LogInformation("Module {ModuleName} initialized successfully", module.ModuleName);
                        }
                        else
                        {
                            _logger.LogWarning("Module {ModuleName} initialization returned false", module.ModuleName);
                        }
                        return success;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialize module {ModuleName}", module.ModuleName);
                        return false;
                    }
                })
                .ToList();

            var results = await Task.WhenAll(initTasks);
            var successCount = results.Count(r => r);

            _logger.LogInformation(
                "Hardware initialization complete: {SuccessCount}/{TotalCount} modules initialized",
                successCount, _modules.Count);

            _isInitialized = true;
            return successCount > 0; // At least one module must succeed
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Start monitoring on all initialized modules.
    /// </summary>
    public async Task StartAllMonitoringAsync()
    {
        _logger.LogInformation("Starting monitoring on all modules");

        var monitorTasks = _modules.Values
            .Where(m => m.IsInitialized)
            .Select(async module =>
            {
                try
                {
                    await module.StartMonitoringAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start monitoring for module {ModuleName}", module.ModuleName);
                }
            })
            .ToList();

        await Task.WhenAll(monitorTasks);
        _logger.LogInformation("Monitoring started on all modules");
    }

    /// <summary>
    /// Stop monitoring on all modules.
    /// </summary>
    public async Task StopAllMonitoringAsync()
    {
        _logger.LogInformation("Stopping monitoring on all modules");

        var stopTasks = _modules.Values
            .Select(async module =>
            {
                try
                {
                    await module.StopMonitoringAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop monitoring for module {ModuleName}", module.ModuleName);
                }
            })
            .ToList();

        await Task.WhenAll(stopTasks);
        _logger.LogInformation("Monitoring stopped on all modules");
    }

    /// <summary>
    /// Shutdown all modules and cleanup resources.
    /// Call this when switching modes - modules can be re-initialized later.
    /// </summary>
    public async Task ShutdownAllModulesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                _logger.LogWarning("Hardware modules already shut down");
                return;
            }

            _logger.LogInformation("Shutting down all hardware modules");

            var shutdownTasks = _modules.Values
                .Select(async module =>
                {
                    try
                    {
                        await module.ShutdownAsync();
                        _logger.LogInformation("Module {ModuleName} shut down successfully", module.ModuleName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to shutdown module {ModuleName}", module.ModuleName);
                    }
                })
                .ToList();

            await Task.WhenAll(shutdownTasks);

            // Clear modules dictionary so they can be re-registered on next enable
            _modules.Clear();
            _isInitialized = false;
            _logger.LogInformation("All hardware modules shut down and cleared");
        }
        finally
        {
            _lock.Release();
            // NOTE: Don't dispose _lock here - it's needed for re-initialization
        }
    }

    /// <summary>
    /// Performs final cleanup when the application is shutting down.
    /// After calling this, the HardwareManager cannot be reused.
    /// </summary>
    public async Task DisposeAsync()
    {
        await ShutdownAllModulesAsync();
        _lock.Dispose();
        _logger.LogInformation("HardwareManager disposed");
    }

    /// <summary>
    /// Shutdown a specific module by name and remove it from the manager.
    /// </summary>
    public async Task ShutdownModuleAsync(string moduleName)
    {
        await _lock.WaitAsync();
        try
        {
            if (_modules.TryGetValue(moduleName, out var module))
            {
                _logger.LogInformation("Shutting down module {ModuleName}", moduleName);
                try
                {
                    await module.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to shutdown module {ModuleName}", moduleName);
                }
                _modules.Remove(moduleName);
                _logger.LogInformation("Module {ModuleName} removed from manager", moduleName);
            }
            else
            {
                _logger.LogWarning("Module {ModuleName} not found in manager", moduleName);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get aggregated device information from all modules.
    /// </summary>
    public async Task<Dictionary<string, List<DeviceInfo>>> GetAllDevicesAsync()
    {
        var result = new Dictionary<string, List<DeviceInfo>>();

        foreach (var module in _modules.Values.Where(m => m.IsInitialized))
        {
            try
            {
                var devices = await module.GetDevicesAsync();
                result[module.ModuleName] = devices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get devices from module {ModuleName}", module.ModuleName);
                result[module.ModuleName] = new List<DeviceInfo>();
            }
        }

        return result;
    }
}
