using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Display;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for display device control.
/// </summary>
public static class DisplayController
{
    public static void MapDisplayEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/displays")
            .WithTags("Displays")
            .WithOpenApi();

        // GET /api/v1/displays - List all displays
        group.MapGet("/", async (DisplayModule displayModule) =>
        {
            logger.LogDebug("GET /api/v1/displays");

            try
            {
                var devices = await displayModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting displays");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all displays")
        .WithDescription("Returns a list of all configured display devices");

        // GET /api/v1/displays/{id} - Get display status
        group.MapGet("/{id}", async (string id, DisplayModule displayModule) =>
        {
            logger.LogDebug("GET /api/v1/displays/{Id}", id);

            try
            {
                var status = await displayModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("DISPLAY_NOT_FOUND", $"Display '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting display {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<DisplayStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get display status")
        .WithDescription("Returns detailed status for a specific display device");

        // PUT /api/v1/displays/{id}/brightness - Set brightness
        group.MapPut("/{id}/brightness", async (
            string id,
            [FromBody] BrightnessRequest request,
            DisplayModule displayModule) =>
        {
            logger.LogDebug("PUT /api/v1/displays/{Id}/brightness - {Brightness}", id, request.Brightness);

            try
            {
                await displayModule.SetBrightnessAsync(id, request.Brightness);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Brightness set to {request.Brightness}%"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("DISPLAY_NOT_FOUND", $"Display '{id}' not found"),
                    statusCode: 404);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("INVALID_BRIGHTNESS", ex.Message),
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting brightness for display {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set display brightness")
        .WithDescription("Set the brightness level (0-100) for a display device");

        // PUT /api/v1/displays/{id}/enable - Enable/disable display
        group.MapPut("/{id}/enable", async (
            string id,
            [FromBody] EnableRequest request,
            DisplayModule displayModule) =>
        {
            logger.LogDebug("PUT /api/v1/displays/{Id}/enable - {Enabled}", id, request.Enabled);

            try
            {
                await displayModule.SetEnabledAsync(id, request.Enabled);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Display {(request.Enabled ? "enabled" : "disabled")}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("DISPLAY_NOT_FOUND", $"Display '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting enabled state for display {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Enable or disable display")
        .WithDescription("Turn a display device on or off");
    }
}

/// <summary>
/// Request model for setting brightness.
/// </summary>
public record BrightnessRequest
{
    public int Brightness { get; init; }
}

/// <summary>
/// Request model for enabling/disabling devices.
/// </summary>
public record EnableRequest
{
    public bool Enabled { get; init; }
}
