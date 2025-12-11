using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Microphone;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for network microphone control.
/// </summary>
public static class MicrophoneController
{
    public static void MapMicrophoneEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/microphones")
            .WithTags("Microphones")
            .WithOpenApi();

        // GET /api/v1/microphones - List all microphones
        group.MapGet("/", async (MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("GET /api/v1/microphones");

            try
            {
                var devices = await microphoneModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting microphones");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all microphones")
        .WithDescription("Returns a list of all configured network microphone devices");

        // GET /api/v1/microphones/{id} - Get microphone status
        group.MapGet("/{id}", async (string id, MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("GET /api/v1/microphones/{Id}", id);

            try
            {
                var status = await microphoneModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("MICROPHONE_NOT_FOUND", $"Microphone '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting microphone {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<MicrophoneStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get microphone status")
        .WithDescription("Returns detailed status for a specific microphone device");

        // GET /api/v1/microphones/{id}/mute - Get mute state
        group.MapGet("/{id}/mute", async (string id, MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("GET /api/v1/microphones/{Id}/mute", id);

            try
            {
                var muted = await microphoneModule.GetMuteAsync(id);
                if (muted == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("MICROPHONE_NOT_FOUND", $"Microphone '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { muted = muted.Value }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting mute state for microphone {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get microphone mute state")
        .WithDescription("Get current mute state for a microphone");

        // PUT /api/v1/microphones/{id}/mute - Set mute state
        group.MapPut("/{id}/mute", async (
            string id,
            [FromBody] MuteRequest request,
            MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("PUT /api/v1/microphones/{Id}/mute - {Muted}", id, request.Muted);

            try
            {
                await microphoneModule.SetMuteAsync(id, request.Muted);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Microphone {(request.Muted ? "muted" : "unmuted")}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("MICROPHONE_NOT_FOUND", $"Microphone '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting mute state for microphone {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set microphone mute state")
        .WithDescription("Mute or unmute a microphone device");

        // GET /api/v1/microphones/{id}/volume - Get volume
        group.MapGet("/{id}/volume", async (string id, MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("GET /api/v1/microphones/{Id}/volume", id);

            try
            {
                var volume = await microphoneModule.GetVolumeAsync(id);
                if (volume == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("MICROPHONE_NOT_FOUND", $"Microphone '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(new { volume = volume.Value }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting volume for microphone {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get microphone volume")
        .WithDescription("Get current volume level for a microphone");

        // PUT /api/v1/microphones/{id}/volume - Set volume
        group.MapPut("/{id}/volume", async (
            string id,
            [FromBody] VolumeRequest request,
            MicrophoneModule microphoneModule) =>
        {
            logger.LogDebug("PUT /api/v1/microphones/{Id}/volume - {Volume}", id, request.Volume);

            try
            {
                await microphoneModule.SetVolumeAsync(id, request.Volume);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Volume set to {request.Volume}%"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("MICROPHONE_NOT_FOUND", $"Microphone '{id}' not found"),
                    statusCode: 404);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("INVALID_VOLUME", ex.Message),
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting volume for microphone {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set microphone volume")
        .WithDescription("Set the volume level (0-100) for a microphone device");
    }
}
