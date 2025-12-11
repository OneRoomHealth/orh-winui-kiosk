using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Speaker;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for network speaker control.
/// </summary>
public static class SpeakerController
{
    public static void MapSpeakerEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/speakers")
            .WithTags("Speakers")
            .WithOpenApi();

        // GET /api/v1/speakers - List all speakers
        group.MapGet("/", async (SpeakerModule speakerModule) =>
        {
            logger.LogDebug("GET /api/v1/speakers");

            try
            {
                var devices = await speakerModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting speakers");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all speakers")
        .WithDescription("Returns a list of all configured network speaker devices");

        // GET /api/v1/speakers/{id} - Get speaker status
        group.MapGet("/{id}", async (string id, SpeakerModule speakerModule) =>
        {
            logger.LogDebug("GET /api/v1/speakers/{Id}", id);

            try
            {
                var status = await speakerModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("SPEAKER_NOT_FOUND", $"Speaker '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting speaker {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<SpeakerStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get speaker status")
        .WithDescription("Returns detailed status for a specific speaker device");

        // GET /api/v1/speakers/volume - Get system speaker volume
        group.MapGet("/volume", async (SpeakerModule speakerModule) =>
        {
            logger.LogDebug("GET /api/v1/speakers/volume");

            try
            {
                var status = await speakerModule.GetVolumeStatusAsync();
                return Results.Ok(ApiResponse<SpeakerVolumeStatus>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting speaker volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<SpeakerVolumeStatus>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get speaker volume")
        .WithDescription("Get current volume for all speakers");

        // PUT /api/v1/speakers/volume - Set system speaker volume
        group.MapPut("/volume", async (
            [FromBody] VolumeRequest request,
            SpeakerModule speakerModule) =>
        {
            logger.LogDebug("PUT /api/v1/speakers/volume - {Volume}", request.Volume);

            try
            {
                await speakerModule.SetVolumeAsync(request.Volume);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Volume set to {request.Volume}%"
                }));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("INVALID_VOLUME", ex.Message),
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting speaker volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set speaker volume")
        .WithDescription("Set volume (0-100) for all speakers");
    }
}
