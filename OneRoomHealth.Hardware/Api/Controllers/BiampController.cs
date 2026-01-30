using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Biamp;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for Biamp Parlé VBC 2800 video conferencing codec control.
/// </summary>
public static class BiampController
{
    public static void MapBiampEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/biamp")
            .WithTags("Biamp")
            .WithOpenApi();

        // GET /api/v1/biamp - List all Biamp devices
        group.MapGet("/", async (BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp");

            try
            {
                var devices = await biampModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting Biamp devices");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all Biamp devices")
        .WithDescription("Returns a list of all configured Biamp video conferencing devices");

        // GET /api/v1/biamp/{id} - Get device status
        group.MapGet("/{id}", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp/{Id}", id);

            try
            {
                var status = await biampModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<BiampStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get Biamp device status")
        .WithDescription("Returns detailed status for a specific Biamp device");

        // GET /api/v1/biamp/{id}/pan - Get pan position
        group.MapGet("/{id}/pan", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp/{Id}/pan", id);

            try
            {
                var pan = await biampModule.GetPanAsync(id);
                if (pan == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to get pan position"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { pan = pan.Value }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting pan for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get pan position")
        .WithDescription("Get current pan position for a Biamp device (-100 to +100)");

        // PUT /api/v1/biamp/{id}/pan - Set pan position
        group.MapPut("/{id}/pan", async (
            string id,
            [FromBody] BiampValueRequest request,
            BiampModule biampModule) =>
        {
            logger.LogDebug("PUT /api/v1/biamp/{Id}/pan - {Value}", id, request.Value);

            try
            {
                var success = await biampModule.SetPanAsync(id, request.Value);
                if (!success)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to set pan position"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = "Pan position updated",
                    pan = request.Value
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting pan for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set pan position")
        .WithDescription("Set pan position for a Biamp device (-100 to +100)");

        // GET /api/v1/biamp/{id}/tilt - Get tilt position
        group.MapGet("/{id}/tilt", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp/{Id}/tilt", id);

            try
            {
                var tilt = await biampModule.GetTiltAsync(id);
                if (tilt == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to get tilt position"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { tilt = tilt.Value }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting tilt for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get tilt position")
        .WithDescription("Get current tilt position for a Biamp device (-100 to +100)");

        // PUT /api/v1/biamp/{id}/tilt - Set tilt position
        group.MapPut("/{id}/tilt", async (
            string id,
            [FromBody] BiampValueRequest request,
            BiampModule biampModule) =>
        {
            logger.LogDebug("PUT /api/v1/biamp/{Id}/tilt - {Value}", id, request.Value);

            try
            {
                var success = await biampModule.SetTiltAsync(id, request.Value);
                if (!success)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to set tilt position"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = "Tilt position updated",
                    tilt = request.Value
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting tilt for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set tilt position")
        .WithDescription("Set tilt position for a Biamp device (-100 to +100)");

        // GET /api/v1/biamp/{id}/zoom - Get zoom level
        group.MapGet("/{id}/zoom", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp/{Id}/zoom", id);

            try
            {
                var zoom = await biampModule.GetZoomAsync(id);
                if (zoom == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to get zoom level"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { zoom = zoom.Value }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting zoom for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get zoom level")
        .WithDescription("Get current zoom level for a Biamp device (1.0 to 5.0)");

        // PUT /api/v1/biamp/{id}/zoom - Set zoom level
        group.MapPut("/{id}/zoom", async (
            string id,
            [FromBody] BiampValueRequest request,
            BiampModule biampModule) =>
        {
            logger.LogDebug("PUT /api/v1/biamp/{Id}/zoom - {Value}", id, request.Value);

            try
            {
                var success = await biampModule.SetZoomAsync(id, request.Value);
                if (!success)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to set zoom level"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = "Zoom level updated",
                    zoom = request.Value
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting zoom for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set zoom level")
        .WithDescription("Set zoom level for a Biamp device (1.0 to 5.0)");

        // GET /api/v1/biamp/{id}/autoframing - Get autoframing state
        group.MapGet("/{id}/autoframing", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("GET /api/v1/biamp/{Id}/autoframing", id);

            try
            {
                var enabled = await biampModule.GetAutoframingAsync(id);
                if (enabled == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to get autoframing state"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { enabled = enabled.Value }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting autoframing for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get autoframing state")
        .WithDescription("Get current autoframing state for a Biamp device");

        // PUT /api/v1/biamp/{id}/autoframing - Set autoframing state
        group.MapPut("/{id}/autoframing", async (
            string id,
            [FromBody] BiampAutoframingRequest request,
            BiampModule biampModule) =>
        {
            logger.LogDebug("PUT /api/v1/biamp/{Id}/autoframing - {Enabled}", id, request.Enabled);

            try
            {
                var success = await biampModule.SetAutoframingAsync(id, request.Enabled);
                if (!success)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to set autoframing state"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Autoframing {(request.Enabled ? "enabled" : "disabled")}",
                    enabled = request.Enabled
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting autoframing for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set autoframing state")
        .WithDescription("Enable or disable autoframing for a Biamp device");

        // POST /api/v1/biamp/{id}/autoframing/toggle - Toggle autoframing
        group.MapPost("/{id}/autoframing/toggle", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("POST /api/v1/biamp/{Id}/autoframing/toggle", id);

            try
            {
                var newState = await biampModule.ToggleAutoframingAsync(id);
                if (newState == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to toggle autoframing"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Autoframing toggled to {(newState.Value ? "enabled" : "disabled")}",
                    enabled = newState.Value
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error toggling autoframing for Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Toggle autoframing")
        .WithDescription("Toggle autoframing state for a Biamp device");

        // POST /api/v1/biamp/{id}/reboot - Reboot device
        group.MapPost("/{id}/reboot", async (string id, BiampModule biampModule) =>
        {
            logger.LogDebug("POST /api/v1/biamp/{Id}/reboot", id);

            try
            {
                var success = await biampModule.RebootAsync(id);
                if (!success)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("COMMAND_FAILED", "Failed to send reboot command"),
                        statusCode: 500);
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Reboot command sent to device {id}. Device will restart."
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BIAMP_NOT_FOUND", $"Biamp device '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error rebooting Biamp device {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Reboot device")
        .WithDescription("Send a reboot command to a Biamp device. Device will be offline for approximately 30 seconds.");
    }
}

/// <summary>
/// Request model for Biamp numeric value control (pan, tilt, zoom).
/// </summary>
public record BiampValueRequest
{
    public double Value { get; init; }
}

/// <summary>
/// Request model for Biamp autoframing control.
/// </summary>
public record BiampAutoframingRequest
{
    public bool Enabled { get; init; }
}
