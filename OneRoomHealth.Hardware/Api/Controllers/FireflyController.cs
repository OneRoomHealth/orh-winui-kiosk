using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Firefly;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for Firefly UVC otoscope camera control.
/// Registered on the existing HardwareApiServer (port 8081).
/// </summary>
public static class FireflyController
{
    /// <summary>
    /// Register Firefly endpoints on the web application.
    /// </summary>
    public static void MapFireflyEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/firefly")
            .WithTags("Firefly")
            .WithOpenApi();

        // GET /api/v1/firefly
        group.MapGet("/", async (FireflyModule fireflyModule) =>
        {
            logger.LogDebug("GET /api/v1/firefly");
            try
            {
                var devices = await fireflyModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting Firefly devices");
                return Results.Json(ApiErrorResponse.FromException(ex), statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("List Firefly devices")
        .WithDescription("Returns all connected Firefly UVC otoscope cameras");

        // GET /api/v1/firefly/capture
        // Registered BEFORE /{id} so that the literal segment wins over the route
        // parameter regardless of how the Minimal API routing engine resolves priority.
        // Returns { imageBase64: string, contentType: string } — the format expected by
        // VITE_WINUI_CAPTURE_URL fetch in the MA SessionView.  Captures from the first
        // available Firefly device.  Uses the JS-side WebView delegate when registered
        // (required while ACS is streaming — browser owns the UVC device exclusively);
        // falls back to native TriggerCaptureAsync when no ACS call is active.
        group.MapGet("/capture", async (FireflyModule fireflyModule) =>
        {
            logger.LogInformation("GET /api/v1/firefly/capture");
            try
            {
                var devices = await fireflyModule.GetDevicesAsync();
                var device = devices.FirstOrDefault();
                if (device == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("FIREFLY_NOT_FOUND", "No Firefly device found"),
                        statusCode: 404);
                }

                byte[]? imageBytes = null;

                if (fireflyModule.WebCaptureDelegate != null)
                {
                    var statusObj = await fireflyModule.GetDeviceStatusAsync(device.Id);
                    var label = (statusObj as FireflyDeviceStatus)?.FriendlyName ?? device.Id;
                    imageBytes = await fireflyModule.WebCaptureDelegate(device.Id, label);
                }

                // Native fallback — used when WebView is unavailable or no ACS session active
                if (imageBytes == null || imageBytes.Length == 0)
                    imageBytes = await fireflyModule.TriggerCaptureAsync(device.Id);

                var base64 = Convert.ToBase64String(imageBytes);
                return Results.Ok(new { imageBase64 = base64, contentType = "image/jpeg" });
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("FIREFLY_NOT_FOUND", "Firefly device not found"),
                    statusCode: 404);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("FIREFLY_NOT_CONNECTED", ex.Message),
                    statusCode: 409);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Capture failed");
                return Results.Json(
                    ApiErrorResponse.FromMessage("CAPTURE_FAILED", "Image capture failed", ex.Message),
                    statusCode: 502);
            }
        })
        .Produces<object>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(409)
        .Produces<ApiErrorResponse>(502)
        .WithSummary("Capture from first available Firefly device")
        .WithDescription(
            "Returns { imageBase64, contentType } JSON. " +
            "Uses JS-side WebView capture during active ACS sessions; " +
            "falls back to native MediaCapture otherwise. " +
            "Target of VITE_WINUI_CAPTURE_URL in the frontend.");

        // GET /api/v1/firefly/{id}
        group.MapGet("/{id}", async (string id, FireflyModule fireflyModule) =>
        {
            logger.LogDebug("GET /api/v1/firefly/{Id}", id);
            try
            {
                var status = await fireflyModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage(
                            "FIREFLY_NOT_FOUND",
                            $"Firefly device '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting Firefly device {Id}", id);
                return Results.Json(ApiErrorResponse.FromException(ex), statusCode: 500);
            }
        })
        .Produces<ApiResponse<FireflyDeviceStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get Firefly device status")
        .WithDescription("Returns detailed status for a specific Firefly camera, including health, capture count, and last capture timestamp");

        // POST /api/v1/firefly/{id}/capture
        group.MapPost("/{id}/capture", async (string id, FireflyModule fireflyModule) =>
        {
            logger.LogInformation("POST /api/v1/firefly/{Id}/capture", id);
            try
            {
                var imageBytes = await fireflyModule.TriggerCaptureAsync(id);

                // Return the raw JPEG directly; downstream delivery happens asynchronously
                return Results.File(imageBytes, "image/jpeg", $"firefly-{id}-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage(
                        "FIREFLY_NOT_FOUND",
                        $"Firefly device '{id}' not found"),
                    statusCode: 404);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("FIREFLY_NOT_CONNECTED", ex.Message),
                    statusCode: 409);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Capture failed for Firefly device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromMessage(
                        "CAPTURE_FAILED",
                        "Image capture failed",
                        ex.Message),
                    statusCode: 502);
            }
        })
        .Produces(200, contentType: "image/jpeg")
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(409)
        .Produces<ApiErrorResponse>(502)
        .WithSummary("Trigger still capture")
        .WithDescription(
            "Triggers a 4K still capture from the specified Firefly device. " +
            "Returns the raw JPEG image. " +
            "If downstream delivery is configured the image is also forwarded asynchronously.");
    }
}
