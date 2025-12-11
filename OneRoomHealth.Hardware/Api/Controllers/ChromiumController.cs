using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Modules.Chromium;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for Chromium browser control.
/// </summary>
public static class ChromiumController
{
    public static void MapChromiumEndpoints(this WebApplication app, ILogger logger)
    {
        var group = app.MapGroup("/api/v1/chromium")
            .WithTags("Chromium")
            .WithOpenApi();

        // GET /api/v1/chromium - List all browser instances
        group.MapGet("/", async (ChromiumModule chromiumModule) =>
        {
            logger.LogDebug("GET /api/v1/chromium");

            try
            {
                var devices = await chromiumModule.GetDevicesAsync();
                return Results.Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting chromium instances");
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get all Chromium instances")
        .WithDescription("Returns a list of all configured Chromium browser instances");

        // GET /api/v1/chromium/{id} - Get browser status
        group.MapGet("/{id}", async (string id, ChromiumModule chromiumModule) =>
        {
            logger.LogDebug("GET /api/v1/chromium/{Id}", id);

            try
            {
                var status = await chromiumModule.GetDeviceStatusAsync(id);
                if (status == null)
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("BROWSER_NOT_FOUND", $"Chromium instance '{id}' not found"),
                        statusCode: 404);
                }

                return Results.Ok(ApiResponse<object>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting chromium instance {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<ChromiumStatus>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get Chromium instance status")
        .WithDescription("Returns detailed status for a specific Chromium browser instance");

        // POST /api/v1/chromium/{id}/open - Open/start browser
        group.MapPost("/{id}/open", async (string id, ChromiumModule chromiumModule) =>
        {
            logger.LogDebug("POST /api/v1/chromium/{Id}/open", id);

            try
            {
                await chromiumModule.OpenBrowserAsync(id);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = "Browser opened successfully"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BROWSER_NOT_FOUND", $"Chromium instance '{id}' not found"),
                    statusCode: 404);
            }
            catch (FileNotFoundException ex)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("CHROMIUM_NOT_FOUND", ex.Message),
                    statusCode: 500);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error opening chromium instance {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Open Chromium browser")
        .WithDescription("Start a Chromium browser instance if not already running");

        // POST /api/v1/chromium/{id}/close - Close/stop browser
        group.MapPost("/{id}/close", async (string id, ChromiumModule chromiumModule) =>
        {
            logger.LogDebug("POST /api/v1/chromium/{Id}/close", id);

            try
            {
                await chromiumModule.CloseBrowserAsync(id);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = "Browser closed successfully"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BROWSER_NOT_FOUND", $"Chromium instance '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error closing chromium instance {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Close Chromium browser")
        .WithDescription("Stop a running Chromium browser instance");

        // PUT /api/v1/chromium/{id}/url - Navigate to URL
        group.MapPut("/{id}/url", async (
            string id,
            [FromBody] NavigateRequest request,
            ChromiumModule chromiumModule) =>
        {
            logger.LogDebug("PUT /api/v1/chromium/{Id}/url - {Url}", id, request.Url);

            try
            {
                await chromiumModule.NavigateToUrlAsync(id, request.Url);
                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    message = $"Navigated to {request.Url}"
                }));
            }
            catch (KeyNotFoundException)
            {
                return Results.Json(
                    ApiErrorResponse.FromMessage("BROWSER_NOT_FOUND", $"Chromium instance '{id}' not found"),
                    statusCode: 404);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error navigating chromium instance {Id}", id);
                return Results.Json(
                    ApiErrorResponse.FromException(ex),
                    statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Navigate to URL")
        .WithDescription("Navigate a Chromium browser instance to a specific URL");
    }
}

/// <summary>
/// Request model for URL navigation.
/// </summary>
public record NavigateRequest
{
    public required string Url { get; init; }
}
