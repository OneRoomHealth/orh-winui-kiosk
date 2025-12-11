using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Camera;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for camera device control.
/// </summary>
public static class CameraController
{
    public static void MapCameraEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/cameras")
            .WithTags("Cameras")
            .WithOpenApi();

        // GET /api/v1/cameras - List all cameras
        group.MapGet("/", async (CameraModule cameraModule) =>
        {
            logger.LogDebug("GET /api/v1/cameras");

            try
            {
                var devices = await cameraModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting cameras");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all cameras")
        .WithDescription("Returns a list of all configured camera devices");

        // GET /api/v1/cameras/{id} - Get camera status
        group.MapGet("/{id}", async (string id, CameraModule cameraModule) =>
        {
            logger.LogDebug("GET /api/v1/cameras/{Id}", id);

            try
            {
                var status = await cameraModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<CameraStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get camera status")
        .WithDescription("Returns detailed status for a specific camera device");

        // PUT /api/v1/cameras/{id}/enable - Enable/disable camera
        group.MapPut("/{id}/enable", async (
            string id,
            [FromBody] EnableRequest request,
            CameraModule cameraModule) =>
        {
            logger.LogDebug("PUT /api/v1/cameras/{Id}/enable - {Enabled}", id, request.Enabled);

            try
            {
                await cameraModule.SetEnabledAsync(id, request.Enabled);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Camera {(request.Enabled ? "enabled" : "disabled")}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting enabled state for camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Enable or disable camera")
        .WithDescription("Turn a camera device on or off");

        // GET /api/v1/cameras/{id}/ptz - Get PTZ position
        group.MapGet("/{id}/ptz", async (string id, CameraModule cameraModule) =>
        {
            logger.LogDebug("GET /api/v1/cameras/{Id}/ptz", id);

            try
            {
                var ptz = await cameraModule.GetPtzPositionAsync(id);
                if (ptz == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<PtzPosition>.Ok(ptz));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting PTZ for camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<PtzPosition>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get PTZ position")
        .WithDescription("Get current pan/tilt/zoom position for a camera");

        // PUT /api/v1/cameras/{id}/ptz - Set PTZ position
        group.MapPut("/{id}/ptz", async (
            string id,
            [FromBody] PtzRequest request,
            CameraModule cameraModule) =>
        {
            logger.LogDebug("PUT /api/v1/cameras/{Id}/ptz - Pan={Pan}, Tilt={Tilt}, Zoom={Zoom}",
                id, request.Pan, request.Tilt, request.Zoom);

            try
            {
                var result = await cameraModule.SetPtzPositionAsync(id, request.Pan, request.Tilt, request.Zoom);
                if (result == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<PtzPosition>.Ok(result));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting PTZ for camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<PtzPosition>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set PTZ position")
        .WithDescription("Set pan/tilt/zoom position for a camera (-1 to 1 for pan/tilt, 0 to 1 for zoom)");

        // GET /api/v1/cameras/{id}/auto-tracking - Get auto-tracking state
        group.MapGet("/{id}/auto-tracking", async (string id, CameraModule cameraModule) =>
        {
            logger.LogDebug("GET /api/v1/cameras/{Id}/auto-tracking", id);

            try
            {
                var (supported, enabled) = await cameraModule.GetAutoTrackingAsync(id);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    autoTrackingSupported = supported,
                    enabled
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting auto-tracking for camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get auto-tracking state")
        .WithDescription("Get auto-tracking/auto-framing state for a camera");

        // PUT /api/v1/cameras/{id}/auto-tracking - Set auto-tracking state
        group.MapPut("/{id}/auto-tracking", async (
            string id,
            [FromBody] EnableRequest request,
            CameraModule cameraModule) =>
        {
            logger.LogDebug("PUT /api/v1/cameras/{Id}/auto-tracking - {Enabled}", id, request.Enabled);

            try
            {
                await cameraModule.SetAutoTrackingAsync(id, request.Enabled);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Auto-tracking {(request.Enabled ? "enabled" : "disabled")}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("CAMERA_NOT_FOUND", $"Camera '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting auto-tracking for camera {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set auto-tracking state")
        .WithDescription("Enable or disable auto-tracking/auto-framing for a camera");
    }
}

/// <summary>
/// Request model for PTZ control.
/// </summary>
public record PtzRequest
{
    public double? Pan { get; init; }
    public double? Tilt { get; init; }
    public double? Zoom { get; init; }
}
