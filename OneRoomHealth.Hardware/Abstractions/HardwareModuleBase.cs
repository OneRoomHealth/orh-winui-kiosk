using Microsoft.Extensions.Logging;

namespace OneRoomHealth.Hardware.Abstractions;

/// <summary>
/// Base class for all hardware modules providing common functionality.
/// </summary>
public abstract class HardwareModuleBase : IHardwareModule
{
    protected readonly ILogger Logger;
    protected CancellationTokenSource? MonitoringCts;
    protected Task? MonitoringTask;

    public abstract string ModuleName { get; }
    public bool IsEnabled { get; protected set; }
    public bool IsInitialized { get; protected set; }

    public event EventHandler<DeviceHealthChangedEventArgs>? DeviceHealthChanged;

    protected HardwareModuleBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task<bool> InitializeAsync();
    public abstract Task<List<DeviceInfo>> GetDevicesAsync();
    public abstract Task<object?> GetDeviceStatusAsync(string deviceId);

    public virtual async Task StartMonitoringAsync()
    {
        if (!IsInitialized)
        {
            Logger.LogWarning("{ModuleName}: Cannot start monitoring - module not initialized", ModuleName);
            return;
        }

        if (MonitoringTask != null)
        {
            Logger.LogWarning("{ModuleName}: Monitoring already running", ModuleName);
            return;
        }

        MonitoringCts = new CancellationTokenSource();
        MonitoringTask = Task.Run(() => MonitorDevicesAsync(MonitoringCts.Token), MonitoringCts.Token);
        Logger.LogInformation("{ModuleName}: Background monitoring started", ModuleName);
    }

    public virtual async Task StopMonitoringAsync()
    {
        if (MonitoringTask == null)
        {
            return;
        }

        Logger.LogInformation("{ModuleName}: Stopping background monitoring", ModuleName);
        MonitoringCts?.Cancel();

        try
        {
            await MonitoringTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ModuleName}: Error while stopping monitoring", ModuleName);
        }
        finally
        {
            MonitoringCts?.Dispose();
            MonitoringCts = null;
            MonitoringTask = null;
        }

        Logger.LogInformation("{ModuleName}: Background monitoring stopped", ModuleName);
    }

    public virtual async Task ShutdownAsync()
    {
        Logger.LogInformation("{ModuleName}: Shutting down", ModuleName);
        await StopMonitoringAsync();
        IsInitialized = false;
        Logger.LogInformation("{ModuleName}: Shutdown complete", ModuleName);
    }

    /// <summary>
    /// Override this method to implement device monitoring logic.
    /// </summary>
    protected abstract Task MonitorDevicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raise the DeviceHealthChanged event.
    /// </summary>
    protected virtual void OnDeviceHealthChanged(DeviceHealthChangedEventArgs args)
    {
        Logger.LogInformation(
            "{ModuleName}: Device {DeviceId} health changed from {PreviousHealth} to {NewHealth}",
            ModuleName, args.DeviceId, args.PreviousHealth, args.NewHealth);

        DeviceHealthChanged?.Invoke(this, args);
    }
}
