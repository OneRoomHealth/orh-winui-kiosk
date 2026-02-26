using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Api.Models;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for chromium/browser control.
/// Provides workstation-api compatible endpoints that delegate to the WinUI WebView2.
/// Accepts device IDs "0" and "1" — both route to the single WebView2 instance.
/// </summary>
public static class ChromiumController
{
    private static readonly HashSet<string> ValidDeviceIds = new() { "0", "1" };

    /// <summary>
    /// Service interface for WebView navigation control.
    /// </summary>
    public interface IWebViewNavigationService
    {
        /// <summary>
        /// Navigate the WebView to the specified URL.
        /// </summary>
        Task<bool> NavigateAsync(string url);

        /// <summary>
        /// Get the current URL displayed in the WebView.
        /// </summary>
        string? GetCurrentUrl();
    }

    /// <summary>
    /// Maps chromium-compatible endpoints that delegate to WebView2.
    /// Both device IDs "0" (CareWall) and "1" (Provider Hub) are accepted
    /// and route to the same underlying WebView2 instance.
    /// </summary>
    public static void MapChromiumEndpoints(this WebApplication app, ILogger logger, IWebViewNavigationService? navigationService)
    {
        var group = app.MapGroup("/api/v1/chromium")
            .WithTags("Chromium")
            .WithOpenApi();

        // GET /api/v1/chromium - List all browser instances
        group.MapGet("/", () =>
        {
            logger.LogInformation("GET /api/v1/chromium — listing browser instances");

            var browsers = new[]
            {
                new
                {
                    id = "0",
                    name = "Kiosk WebView",
                    health = "healthy",
                    running = true,
                    url = navigationService?.GetCurrentUrl() ?? "",
                    display_mode = "kiosk"
                }
            };

            return Results.Ok(browsers);
        })
        .Produces<object[]>(200)
        .WithSummary("List all browser instances")
        .WithDescription("Returns all browser instances (single WebView2 instance; device IDs '0' and '1' are both accepted on individual endpoints)");

        // GET /api/v1/chromium/{id} - Get browser status
        group.MapGet("/{id}", (string id) =>
        {
            logger.LogInformation("GET /api/v1/chromium/{Id} — status request", id);

            if (!ValidDeviceIds.Contains(id))
            {
                logger.LogWarning("GET /api/v1/chromium/{Id} — unknown device ID", id);
                return Results.Json(
                    new { error = new { code = "NOT_FOUND", message = $"Browser instance {id} not found" } },
                    statusCode: 404);
            }

            var status = new
            {
                id,
                name = "Kiosk WebView",
                running = true,
                health = "healthy",
                url = navigationService?.GetCurrentUrl() ?? "",
                display_mode = "kiosk",
                target_display = "primary"
            };

            return Results.Ok(status);
        })
        .Produces<object>(200)
        .Produces<object>(404)
        .WithSummary("Get browser status")
        .WithDescription("Returns status of a specific browser instance");

        // GET /api/v1/chromium/{id}/url - Get current URL
        group.MapGet("/{id}/url", (string id) =>
        {
            logger.LogInformation("GET /api/v1/chromium/{Id}/url — get current URL", id);

            if (!ValidDeviceIds.Contains(id))
            {
                logger.LogWarning("GET /api/v1/chromium/{Id}/url — unknown device ID", id);
                return Results.Json(
                    new { error = new { code = "NOT_FOUND", message = $"Browser instance {id} not found" } },
                    statusCode: 404);
            }

            var currentUrl = navigationService?.GetCurrentUrl() ?? "";
            logger.LogInformation("GET /api/v1/chromium/{Id}/url — current URL: {Url}", id, currentUrl);

            return Results.Ok(new
            {
                id,
                url = currentUrl,
                success = true
            });
        })
        .Produces<object>(200)
        .Produces<object>(404)
        .WithSummary("Get current URL")
        .WithDescription("Returns the current URL displayed in the browser");

        // Shared handler for setting browser URL (accepts both PUT and POST)
        async Task<IResult> HandleSetUrl(string id, string method, UrlRequest request)
        {
            logger.LogInformation("{Method} /api/v1/chromium/{Id}/url — navigate to: {Url}", method, id, request.Url);

            if (!ValidDeviceIds.Contains(id))
            {
                logger.LogWarning("{Method} /api/v1/chromium/{Id}/url — unknown device ID", method, id);
                return Results.Json(
                    new { error = new { code = "NOT_FOUND", message = $"Browser instance {id} not found" } },
                    statusCode: 404);
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return Results.Json(
                    new { error = new { code = "BAD_REQUEST", message = "Field 'url' is required" } },
                    statusCode: 400);
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            {
                return Results.Json(
                    new { error = new { code = "BAD_REQUEST", message = "Invalid URL format" } },
                    statusCode: 400);
            }

            if (navigationService == null)
            {
                logger.LogWarning("Navigation service not available");
                return Results.Json(
                    new { error = new { code = "SERVICE_UNAVAILABLE", message = "Navigation service not initialized" } },
                    statusCode: 503);
            }

            try
            {
                var success = await navigationService.NavigateAsync(request.Url);

                if (success)
                {
                    logger.LogInformation("Browser {DeviceId} URL set to: {Url}", id, request.Url);
                    return Results.Ok(new
                    {
                        id,
                        url = request.Url,
                        success = true
                    });
                }
                else
                {
                    return Results.Json(
                        new { error = new { code = "INTERNAL_ERROR", message = "Failed to navigate to URL" } },
                        statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error navigating to URL: {Url}", request.Url);
                return Results.Json(
                    new { error = new { code = "INTERNAL_ERROR", message = ex.Message } },
                    statusCode: 500);
            }
        }

        // PUT /api/v1/chromium/{id}/url - Set browser URL
        group.MapPut("/{id}/url", async (string id, [FromBody] UrlRequest request) =>
            await HandleSetUrl(id, "PUT", request))
        .Produces<object>(200)
        .Produces<object>(400)
        .Produces<object>(404)
        .Produces<object>(500)
        .WithSummary("Set browser URL (PUT)")
        .WithDescription("Navigate the browser to a new URL");

        // POST /api/v1/chromium/{id}/url - Set browser URL (alternate method)
        // group.MapPost("/{id}/url", async (string id, [FromBody] UrlRequest request) =>
        //     await HandleSetUrl(id, "POST", request))
        // .Produces<object>(200)
        // .Produces<object>(400)
        // .Produces<object>(404)
        // .Produces<object>(500)
        // .WithSummary("Set browser URL (POST)")
        // .WithDescription("Navigate the browser to a new URL");

        // POST /api/v1/chromium/{id}/open - Open browser (no-op for WebView)
        group.MapPost("/{id}/open", (string id) =>
        {
            logger.LogInformation("POST /api/v1/chromium/{Id}/open — open request", id);

            if (!ValidDeviceIds.Contains(id))
            {
                logger.LogWarning("POST /api/v1/chromium/{Id}/open — unknown device ID", id);
                return Results.Json(
                    new { error = new { code = "NOT_FOUND", message = $"Browser instance {id} not found" } },
                    statusCode: 404);
            }

            return Results.Ok(new
            {
                id,
                success = true,
                message = "Browser is already running (WebView2 is always active)"
            });
        })
        .Produces<object>(200)
        .Produces<object>(404)
        .WithSummary("Open browser")
        .WithDescription("Open the browser instance (no-op for embedded WebView2)");

        // POST /api/v1/chromium/{id}/close - Close browser (no-op for WebView)
        group.MapPost("/{id}/close", (string id) =>
        {
            logger.LogInformation("POST /api/v1/chromium/{Id}/close — close request", id);

            if (!ValidDeviceIds.Contains(id))
            {
                logger.LogWarning("POST /api/v1/chromium/{Id}/close — unknown device ID", id);
                return Results.Json(
                    new { error = new { code = "NOT_FOUND", message = $"Browser instance {id} not found" } },
                    statusCode: 404);
            }

            return Results.Ok(new
            {
                id,
                success = true,
                message = "Close is not supported for embedded WebView2"
            });
        })
        .Produces<object>(200)
        .Produces<object>(404)
        .WithSummary("Close browser")
        .WithDescription("Close the browser instance (no-op for embedded WebView2)");
    }
}

/// <summary>
/// Request model for URL navigation.
/// </summary>
public record UrlRequest
{
    public string Url { get; init; } = "";
}
