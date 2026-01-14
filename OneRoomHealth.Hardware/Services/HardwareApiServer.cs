using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Api.Controllers;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OneRoomHealth.Hardware.Services;

/// <summary>
/// ASP.NET Core-based HTTP API server for hardware control.
/// Runs on port 8081 and provides RESTful endpoints for all hardware modules.
/// </summary>
public class HardwareApiServer
{
    private readonly ILogger<HardwareApiServer> _logger;
    private readonly HardwareManager _hardwareManager;
    private readonly int _port;
    private WebApplication? _app;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private Func<string, Task>? _navigateHandler;

    public HardwareApiServer(
        ILogger<HardwareApiServer> logger,
        HardwareManager hardwareManager,
        int port = 8081)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hardwareManager = hardwareManager ?? throw new ArgumentNullException(nameof(hardwareManager));
        _port = port;
    }

    /// <summary>
    /// Provide a host callback to handle navigation requests (e.g., navigate the kiosk WebView).
    /// </summary>
    public void SetNavigationHandler(Func<string, Task> handler)
    {
        _navigateHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger.LogInformation("Navigation handler registered");
    }

    /// <summary>
    /// Start the API server.
    /// </summary>
    public async Task StartAsync()
    {
        _logger.LogInformation("Starting Hardware API Server on port {Port}", _port);

        var builder = WebApplication.CreateBuilder();

        // Configure logging to use existing logger
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ForwardingLoggerProvider(_logger));

        // Add services
        builder.Services.AddSingleton(_hardwareManager);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "OneRoom Health Hardware API",
                Version = "v1",
                Description = "RESTful API for controlling hardware devices (cameras, displays, lighting, audio)",
                Contact = new OpenApiContact
                {
                    Name = "OneRoom Health",
                    Url = new Uri("https://oneroomhealth.com")
                }
            });
        });

        // Configure CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Configure Kestrel
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(_port);
        });

        _app = builder.Build();

        // Configure middleware
        _app.UseCors();
        _app.UseSwagger();
        _app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hardware API v1");
            c.RoutePrefix = "swagger";
        });

        // Map API endpoints
        MapSystemEndpoints(_app);
        MapModuleEndpoints(_app);
        MapNavigationEndpoints(_app);

        // Start the server
        await _app.StartAsync();

        _logger.LogInformation(
            "Hardware API Server started successfully on http://localhost:{Port}", _port);
        _logger.LogInformation(
            "Swagger UI available at http://localhost:{Port}/swagger", _port);
    }

    /// <summary>
    /// Stop the API server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_app != null)
        {
            _logger.LogInformation("Stopping Hardware API Server");
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
            _logger.LogInformation("Hardware API Server stopped");
        }
    }

    /// <summary>
    /// Map system-level endpoints.
    /// </summary>
    private void MapSystemEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("System")
            .WithOpenApi();

        // GET /api/v1/status - System status
        group.MapGet("/status", async (HardwareManager hardwareManager) =>
        {
            _logger.LogDebug("GET /api/v1/status");

            var modules = new Dictionary<string, ModuleStatus>();

            foreach (var module in hardwareManager.GetAllModules())
            {
                var devices = await module.GetDevicesAsync();
                var healthCounts = devices.GroupBy(d => d.Health)
                    .ToDictionary(g => g.Key, g => g.Count());

                modules[module.ModuleName] = new ModuleStatus
                {
                    Name = module.ModuleName,
                    Enabled = module.IsEnabled,
                    Initialized = module.IsInitialized,
                    DeviceCount = devices.Count,
                    HealthyDevices = healthCounts.GetValueOrDefault(DeviceHealth.Healthy, 0),
                    UnhealthyDevices = healthCounts.GetValueOrDefault(DeviceHealth.Unhealthy, 0),
                    OfflineDevices = healthCounts.GetValueOrDefault(DeviceHealth.Offline, 0)
                };
            }

            var status = new SystemStatus
            {
                Name = "OneRoom Health Kiosk",
                Version = "2.0.0",
                ServerTime = DateTime.UtcNow,
                UptimeSeconds = _uptime.Elapsed.TotalSeconds,
                Modules = modules
            };

            return Results.Ok(ApiResponse<SystemStatus>.Ok(status));
        })
        .Produces<ApiResponse<SystemStatus>>(200)
        .WithSummary("Get system status")
        .WithDescription("Returns overall system status including module health and uptime");

        // GET /api/v1/devices - All devices from all modules
        group.MapGet("/devices", async (HardwareManager hardwareManager) =>
        {
            _logger.LogDebug("GET /api/v1/devices");

            try
            {
                var devices = await hardwareManager.GetAllDevicesAsync();
                return Results.Ok(ApiResponse<Dictionary<string, List<DeviceInfo>>>.Ok(devices));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all devices");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<Dictionary<string, List<DeviceInfo>>>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all devices")
        .WithDescription("Returns all devices grouped by module type (cameras, displays, lighting, etc.)");
    }

    /// <summary>
    /// Map hardware module-specific endpoints.
    /// </summary>
    private void MapModuleEndpoints(WebApplication app)
    {
        // Try to get modules from DI container and map their endpoints
        try
        {
            var displayModule = app.Services.GetService<Modules.Display.DisplayModule>();
            if (displayModule != null)
            {
                app.MapDisplayEndpoints(_logger);
                _logger.LogInformation("Display endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Display endpoints");
        }

        try
        {
            var cameraModule = app.Services.GetService<Modules.Camera.CameraModule>();
            if (cameraModule != null)
            {
                app.MapCameraEndpoints(_logger);
                _logger.LogInformation("Camera endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Camera endpoints");
        }

        try
        {
            var lightingModule = app.Services.GetService<Modules.Lighting.LightingModule>();
            if (lightingModule != null)
            {
                app.MapLightingEndpoints(_logger);
                _logger.LogInformation("Lighting endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Lighting endpoints");
        }

        try
        {
            var systemAudioModule = app.Services.GetService<Modules.SystemAudio.SystemAudioModule>();
            if (systemAudioModule != null)
            {
                app.MapSystemAudioEndpoints(_logger);
                _logger.LogInformation("System Audio endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register System Audio endpoints");
        }

        try
        {
            var microphoneModule = app.Services.GetService<Modules.Microphone.MicrophoneModule>();
            if (microphoneModule != null)
            {
                app.MapMicrophoneEndpoints(_logger);
                _logger.LogInformation("Microphone endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Microphone endpoints");
        }

        try
        {
            var speakerModule = app.Services.GetService<Modules.Speaker.SpeakerModule>();
            if (speakerModule != null)
            {
                app.MapSpeakerEndpoints(_logger);
                _logger.LogInformation("Speaker endpoints registered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Speaker endpoints");
        }
    }

    private void MapNavigationEndpoints(WebApplication app)
    {
        // Back-compat endpoint to support existing local automation:
        // POST http://127.0.0.1:{port}/navigate  { "url": "https://example.com" }
        app.MapPost("/navigate", async (HttpRequest request) =>
        {
            try
            {
                _logger.LogDebug("POST /navigate");

                // Read raw body for robust parsing + diagnostics (curl/PowerShell quoting issues are common).
                string rawBody;
                using (var reader = new StreamReader(request.Body))
                {
                    rawBody = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("POST /navigate Content-Type={ContentType} RawBody={RawBody}", request.ContentType, rawBody);

                string? url = null;

                // Try JSON first: {"url":"https://..."}
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(rawBody);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("url", out var urlEl) &&
                            urlEl.ValueKind == JsonValueKind.String)
                        {
                            url = urlEl.GetString();
                        }
                    }
                    catch (JsonException)
                    {
                        // fall through to other parsing options below
                    }
                }

                // Fallback: allow plain text body containing just the URL
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(rawBody) &&
                    Uri.TryCreate(rawBody.Trim().Trim('"', '\''), UriKind.Absolute, out _))
                {
                    url = rawBody.Trim().Trim('"', '\'');
                }

                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "Invalid request body. Send JSON: {\"url\":\"https://example.com\"}",
                        received = rawBody
                    });
                }

                var handler = _navigateHandler;
                if (handler == null)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                await handler(url);
                return Results.Ok(new { success = true, message = $"Navigating to {url}" });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON for /navigate");
                return Results.BadRequest(new { success = false, message = "Invalid JSON. Expected: {\"url\":\"https://example.com\"}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling /navigate");
                return Results.Problem(ex.Message);
            }
        })
        .WithTags("Navigation")
        .WithSummary("Navigate kiosk to a URL")
        .WithDescription("Back-compat endpoint. POST /navigate with JSON body {\"url\":\"https://...\"}");
    }
}

internal sealed class NavigateRequest
{
    public string? Url { get; set; }
}

/// <summary>
/// Simple logger provider that forwards to an existing ILogger instance.
/// </summary>
internal class ForwardingLoggerProvider : ILoggerProvider
{
    private readonly ILogger _logger;

    public ForwardingLoggerProvider(ILogger logger)
    {
        _logger = logger;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _logger;
    }

    public void Dispose() { }
}
