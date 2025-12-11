using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OneRoomHealth.Hardware.Services;

/// <summary>
/// Background service that monitors hardware module health.
/// Starts monitoring on all registered modules when the service starts.
/// </summary>
public class HealthMonitorService : BackgroundService
{
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly HardwareManager _hardwareManager;

    public HealthMonitorService(
        ILogger<HealthMonitorService> logger,
        HardwareManager hardwareManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hardwareManager = hardwareManager ?? throw new ArgumentNullException(nameof(hardwareManager));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health Monitor Service starting");

        try
        {
            // Wait a bit for modules to initialize
            await Task.Delay(1000, stoppingToken);

            // Start monitoring on all initialized modules
            await _hardwareManager.StartAllMonitoringAsync();

            _logger.LogInformation("Health Monitor Service started successfully");

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
            _logger.LogInformation("Health Monitor Service is stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Health Monitor Service");
            throw;
        }
        finally
        {
            // Stop all monitoring when service is stopping
            await _hardwareManager.StopAllMonitoringAsync();
            _logger.LogInformation("Health Monitor Service stopped");
        }
    }
}
