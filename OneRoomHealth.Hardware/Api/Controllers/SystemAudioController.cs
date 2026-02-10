using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.SystemAudio;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for system audio control (Windows speaker/microphone).
/// </summary>
public static class SystemAudioController
{
    public static void MapSystemAudioEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/system")
            .WithTags("System Audio")
            .WithOpenApi();

        // GET /api/v1/system/volume - Get speaker volume
        group.MapGet("/volume", async (SystemAudioModule audioModule) =>
        {
            logger.LogDebug("GET /api/v1/system/volume");

            try
            {
                var (volume, muted) = await audioModule.GetSpeakerVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume, muted }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting system volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get speaker volume")
        .WithDescription("Returns current system speaker volume and mute state");

        // PUT /api/v1/system/volume - Set speaker volume
        group.MapPut("/volume", async (
            [FromBody] VolumeRequest request,
            SystemAudioModule audioModule) =>
        {
            logger.LogDebug("PUT /api/v1/system/volume - {Volume}", request.Volume);

            try
            {
                await audioModule.SetSpeakerVolumeAsync(request.Volume);
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
                logger.LogError(ex, "Error setting system volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set speaker volume")
        .WithDescription("Set the system speaker volume (0-100)");

        // POST /api/v1/system/volume-up - Increase volume
        group.MapPost("/volume-up", async ([FromBody] VolumeStepRequest? request, SystemAudioModule audioModule) =>
        {
            logger.LogDebug("POST /api/v1/system/volume-up");

            try
            {
                var step = request?.Step ?? 5;
                await audioModule.SpeakerVolumeUpAsync(step);
                var (volume, _) = await audioModule.GetSpeakerVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error increasing volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Increase volume")
        .WithDescription("Increase system speaker volume by step (default 5%)");

        // POST /api/v1/system/volume-down - Decrease volume
        group.MapPost("/volume-down", async ([FromBody] VolumeStepRequest? request, SystemAudioModule audioModule) =>
        {
            logger.LogDebug("POST /api/v1/system/volume-down");

            try
            {
                var step = request?.Step ?? 5;
                await audioModule.SpeakerVolumeDownAsync(step);
                var (volume, _) = await audioModule.GetSpeakerVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error decreasing volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Decrease volume")
        .WithDescription("Decrease system speaker volume by step (default 5%)");

        // GET /api/v1/system/mute - Get mute state
        group.MapGet("/mute", async (SystemAudioModule audioModule) =>
        {
            logger.LogDebug("GET /api/v1/system/mute");

            try
            {
                var (_, muted) = await audioModule.GetSpeakerVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { muted }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting mute state");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get mute state")
        .WithDescription("Returns current speaker mute state");

        // PUT /api/v1/system/mute - Set mute state
        group.MapPut("/mute", async (
            [FromBody] MuteRequest request,
            SystemAudioModule audioModule) =>
        {
            logger.LogDebug("PUT /api/v1/system/mute - {Muted}", request.Muted);

            try
            {
                await audioModule.SetSpeakerMuteAsync(request.Muted);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Speaker {(request.Muted ? "muted" : "unmuted")}"
                }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting mute state");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set mute state")
        .WithDescription("Mute or unmute system speakers");

        // GET /api/v1/system/mic-volume - Get microphone volume
        group.MapGet("/mic-volume", async (SystemAudioModule audioModule) =>
        {
            logger.LogDebug("GET /api/v1/system/mic-volume");

            try
            {
                var (volume, muted) = await audioModule.GetMicrophoneVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume, muted }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting microphone volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get microphone volume")
        .WithDescription("Returns current microphone volume and mute state");

        // PUT /api/v1/system/mic-volume - Set microphone volume
        group.MapPut("/mic-volume", async (
            [FromBody] VolumeRequest request,
            SystemAudioModule audioModule) =>
        {
            logger.LogDebug("PUT /api/v1/system/mic-volume - {Volume}", request.Volume);

            try
            {
                await audioModule.SetMicrophoneVolumeAsync(request.Volume);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Microphone volume set to {request.Volume}%"
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
                logger.LogError(ex, "Error setting microphone volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set microphone volume")
        .WithDescription("Set the microphone volume (0-100)");

        // POST /api/v1/system/mic-volume-up - Increase mic volume
        group.MapPost("/mic-volume-up", async ([FromBody] VolumeStepRequest? request, SystemAudioModule audioModule) =>
        {
            logger.LogDebug("POST /api/v1/system/mic-volume-up");

            try
            {
                var step = request?.Step ?? 5;
                await audioModule.MicrophoneVolumeUpAsync(step);
                var (volume, _) = await audioModule.GetMicrophoneVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error increasing microphone volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Increase microphone volume")
        .WithDescription("Increase microphone volume by step (default 5%)");

        // POST /api/v1/system/mic-volume-down - Decrease mic volume
        group.MapPost("/mic-volume-down", async ([FromBody] VolumeStepRequest? request, SystemAudioModule audioModule) =>
        {
            logger.LogDebug("POST /api/v1/system/mic-volume-down");

            try
            {
                var step = request?.Step ?? 5;
                await audioModule.MicrophoneVolumeDownAsync(step);
                var (volume, _) = await audioModule.GetMicrophoneVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { volume }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error decreasing microphone volume");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Decrease microphone volume")
        .WithDescription("Decrease microphone volume by step (default 5%)");

        // GET /api/v1/system/mic-mute - Get microphone mute state
        group.MapGet("/mic-mute", async (SystemAudioModule audioModule) =>
        {
            logger.LogDebug("GET /api/v1/system/mic-mute");

            try
            {
                var (_, muted) = await audioModule.GetMicrophoneVolumeAsync();
                return Results.Ok(ApiResponse<object>.Ok(new { muted }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting microphone mute state");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get microphone mute state")
        .WithDescription("Returns current microphone mute state");

        // PUT /api/v1/system/mic-mute - Set microphone mute state
        group.MapPut("/mic-mute", async (
            [FromBody] MuteRequest request,
            SystemAudioModule audioModule) =>
        {
            logger.LogDebug("PUT /api/v1/system/mic-mute - {Muted}", request.Muted);

            try
            {
                await audioModule.SetMicrophoneMuteAsync(request.Muted);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Microphone {(request.Muted ? "muted" : "unmuted")}"
                }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error setting microphone mute state");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Set microphone mute state")
        .WithDescription("Mute or unmute the microphone");
    }
}

/// <summary>
/// Request model for volume control.
/// </summary>
public record VolumeRequest
{
    public int Volume { get; init; }
}

/// <summary>
/// Request model for volume step control.
/// </summary>
public record VolumeStepRequest
{
    public int Step { get; init; } = 5;
}

/// <summary>
/// Request model for mute control.
/// </summary>
public record MuteRequest
{
    public bool Muted { get; init; }
}
