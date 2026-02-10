using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Lighting;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for lighting device control.
/// </summary>
public static class LightingController
{
    public static void MapLightingEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/lighting")
            .WithTags("Lighting")
            .WithOpenApi();

        // GET /api/v1/lighting - List all lights
        group.MapGet("/", async (LightingModule lightingModule) =>
        {
            logger.LogDebug("GET /api/v1/lighting");

            try
            {
                var devices = await lightingModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting lights");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all lights")
        .WithDescription("Returns a list of all configured lighting devices");

        // GET /api/v1/lighting/{id} - Get light status
        group.MapGet("/{id}", async (string id, LightingModule lightingModule) =>
        {
            logger.LogDebug("GET /api/v1/lighting/{Id}", id);

            try
            {
                var status = await lightingModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<LightingStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get light status")
        .WithDescription("Returns detailed status for a specific lighting device");

        // GET /api/v1/lighting/{id}/enable - Get light enabled state
        group.MapGet("/{id}/enable", async (string id, LightingModule lightingModule) =>
        {
            logger.LogDebug("GET /api/v1/lighting/{Id}/enable", id);

            try
            {
                var status = await lightingModule.GetDeviceStatusAsync(id);
                if (status is not LightingStatus lightingStatus)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { enabled = lightingStatus.Enabled }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting enabled state for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get light enabled state")
        .WithDescription("Returns whether a lighting device is currently enabled");

        // PUT /api/v1/lighting/{id}/enable - Enable/disable light
        group.MapPut("/{id}/enable", async (
            string id,
            [FromBody] EnableRequest request,
            LightingModule lightingModule) =>
        {
            logger.LogDebug("PUT /api/v1/lighting/{Id}/enable - {Enabled}", id, request.Enabled);

            try
            {
                await lightingModule.SetEnabledAsync(id, request.Enabled);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Light {(request.Enabled ? "enabled" : "disabled")}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting enabled state for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Enable or disable light")
        .WithDescription("Turn a lighting device on or off");

        // GET /api/v1/lighting/{id}/brightness - Get brightness
        group.MapGet("/{id}/brightness", async (string id, LightingModule lightingModule) =>
        {
            logger.LogDebug("GET /api/v1/lighting/{Id}/brightness", id);

            try
            {
                var brightness = await lightingModule.GetBrightnessAsync(id);
                if (brightness == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { brightness = brightness.Value }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting brightness for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get light brightness")
        .WithDescription("Get current brightness level for a lighting device");

        // PUT /api/v1/lighting/{id}/brightness - Set brightness
        group.MapPut("/{id}/brightness", async (
            string id,
            [FromBody] BrightnessRequest request,
            LightingModule lightingModule) =>
        {
            logger.LogDebug("PUT /api/v1/lighting/{Id}/brightness - {Brightness}", id, request.Brightness);

            try
            {
                await lightingModule.SetBrightnessAsync(id, request.Brightness);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Brightness set to {request.Brightness}%"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
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
                logger.LogError(ex, "Error setting brightness for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set light brightness")
        .WithDescription("Set the brightness level (0-100) for a lighting device");

        // GET /api/v1/lighting/{id}/color - Get color
        group.MapGet("/{id}/color", async (string id, LightingModule lightingModule) =>
        {
            logger.LogDebug("GET /api/v1/lighting/{Id}/color", id);

            try
            {
                var color = await lightingModule.GetColorAsync(id);
                if (color == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<RgbwColor>.Ok(color));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting color for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<RgbwColor>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get light color")
        .WithDescription("Get current RGBW color for a lighting device");

        // PUT /api/v1/lighting/{id}/color - Set color
        group.MapPut("/{id}/color", async (
            string id,
            [FromBody] ColorRequest request,
            LightingModule lightingModule) =>
        {
            logger.LogDebug("PUT /api/v1/lighting/{Id}/color - R={R}, G={G}, B={B}, W={W}",
                id, request.Red, request.Green, request.Blue, request.White);

            try
            {
                await lightingModule.SetColorAsync(id, request.Red, request.Green, request.Blue, request.White);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Color set to RGBW({request.Red}, {request.Green}, {request.Blue}, {request.White})"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("LIGHT_NOT_FOUND", $"Light '{id}' not found"),
                    statusCode: 404);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("INVALID_COLOR", ex.Message),
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting color for light {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set light color")
        .WithDescription("Set the RGBW color (0-255 per channel) for a lighting device");
    }
}

/// <summary>
/// Request model for color control.
/// </summary>
public record ColorRequest
{
    public int Red { get; init; }
    public int Green { get; init; }
    public int Blue { get; init; }
    public int White { get; init; } = 0;
}
