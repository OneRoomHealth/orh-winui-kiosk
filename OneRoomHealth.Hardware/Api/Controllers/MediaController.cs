using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using OneRoomHealth.Hardware.Api.Models;
using OneRoomHealth.Hardware.Configuration;
using System.Net.Mime;

namespace OneRoomHealth.Hardware.Api.Controllers;

/// <summary>
/// API endpoints for serving media files (video, audio).
/// Supports HTTP Range requests for video seeking.
/// </summary>
public static class MediaController
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".mp4", "video/mp4" },
        { ".webm", "video/webm" },
        { ".ogg", "video/ogg" },
        { ".ogv", "video/ogg" },
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".m4a", "audio/mp4" },
        { ".aac", "audio/aac" },
        { ".flac", "audio/flac" }
    };

    public static void MapMediaEndpoints(this WebApplication app, ILogger logger, MediaConfiguration? config)
    {
        if (config == null || !config.Enabled)
        {
            logger.LogInformation("Media endpoints disabled (no configuration or enabled=false)");
            return;
        }

        var searchPaths = BuildSearchPaths(config, logger);
        if (searchPaths.Count == 0)
        {
            logger.LogWarning("Media endpoints disabled: no valid media directories configured");
            return;
        }

        var allowedExtensions = config.AllowedExtensions.Count > 0
            ? config.AllowedExtensions.Select(e => e.StartsWith('.') ? e : $".{e}").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : ContentTypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var group = app.MapGroup("/api/v1/media")
            .WithTags("Media")
            .WithOpenApi();

        // GET /api/v1/media - List available media files
        group.MapGet("/", () =>
        {
            logger.LogDebug("GET /api/v1/media");

            try
            {
                var files = new List<object>();

                foreach (var dir in searchPaths)
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(file);
                        if (allowedExtensions.Contains(ext))
                        {
                            var info = new FileInfo(file);
                            files.Add(new
                            {
                                name = info.Name,
                                path = $"/api/v1/media/{Uri.EscapeDataString(info.Name)}",
                                size = info.Length,
                                contentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream"),
                                lastModified = info.LastWriteTimeUtc
                            });
                        }
                    }
                }

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    files,
                    searchPaths = searchPaths.Where(Directory.Exists).ToList()
                }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing media files");
                return Results.Json(ApiErrorResponse.FromException(ex), statusCode: 500);
            }
        })
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("List available media files")
        .WithDescription("Returns a list of media files available in configured directories");

        // GET /api/v1/media/{filename} - Serve media file with range support
        group.MapGet("/{filename}", async (string filename, HttpContext context) =>
        {
            logger.LogDebug("GET /api/v1/media/{Filename}", filename);

            try
            {
                // Security: prevent directory traversal
                var decodedFilename = Uri.UnescapeDataString(filename);
                if (decodedFilename.Contains("..") || Path.IsPathRooted(decodedFilename))
                {
                    logger.LogWarning("Rejected path traversal attempt: {Filename}", filename);
                    return Results.Json(
                        ApiErrorResponse.FromMessage("INVALID_PATH", "Invalid filename"),
                        statusCode: 400);
                }

                // Check extension is allowed
                var ext = Path.GetExtension(decodedFilename);
                if (!allowedExtensions.Contains(ext))
                {
                    return Results.Json(
                        ApiErrorResponse.FromMessage("INVALID_EXTENSION", $"File type '{ext}' not allowed"),
                        statusCode: 403);
                }

                // Find file in search paths
                string? filePath = null;
                foreach (var dir in searchPaths)
                {
                    var candidate = Path.Combine(dir, decodedFilename);
                    if (File.Exists(candidate))
                    {
                        filePath = candidate;
                        break;
                    }
                }

                if (filePath == null)
                {
                    logger.LogDebug("Media file not found: {Filename} (searched: {Paths})",
                        decodedFilename, string.Join(", ", searchPaths));
                    return Results.Json(
                        ApiErrorResponse.FromMessage("FILE_NOT_FOUND", $"Media file '{decodedFilename}' not found"),
                        statusCode: 404);
                }

                var fileInfo = new FileInfo(filePath);
                var contentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream");

                // Handle range requests for video seeking
                var rangeHeader = context.Request.Headers.Range.ToString();
                if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
                {
                    return await ServeRangeRequestAsync(context, fileInfo, contentType, rangeHeader, logger);
                }

                // Full file response
                context.Response.Headers.AcceptRanges = "bytes";
                context.Response.Headers.ContentLength = fileInfo.Length;
                context.Response.ContentType = contentType;

                logger.LogInformation("Serving media file: {Filename} ({Size} bytes)", decodedFilename, fileInfo.Length);

                return Results.File(filePath, contentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error serving media file {Filename}", filename);
                return Results.Json(ApiErrorResponse.FromException(ex), statusCode: 500);
            }
        })
        .Produces(200, contentType: "video/mp4")
        .Produces(206, contentType: "video/mp4")
        .Produces<ApiErrorResponse>(400)
        .Produces<ApiErrorResponse>(403)
        .Produces<ApiErrorResponse>(404)
        .Produces<ApiErrorResponse>(500)
        .WithSummary("Get media file")
        .WithDescription("Serves a media file with support for HTTP Range requests (video seeking)");

        logger.LogInformation("Media endpoints registered with {PathCount} search path(s)", searchPaths.Count);
    }

    private static List<string> BuildSearchPaths(MediaConfiguration config, ILogger logger)
    {
        var paths = new List<string>();

        // Add base directory
        if (!string.IsNullOrWhiteSpace(config.BaseDirectory))
        {
            var expanded = Environment.ExpandEnvironmentVariables(config.BaseDirectory);
            if (Directory.Exists(expanded))
            {
                paths.Add(expanded);
                logger.LogInformation("Media base directory: {Path}", expanded);
            }
            else
            {
                logger.LogWarning("Media base directory does not exist: {Path}", expanded);
            }
        }

        // Add additional directories
        foreach (var dir in config.AdditionalDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;

            var expanded = Environment.ExpandEnvironmentVariables(dir);
            if (Directory.Exists(expanded))
            {
                paths.Add(expanded);
                logger.LogInformation("Media additional directory: {Path}", expanded);
            }
            else
            {
                logger.LogWarning("Media additional directory does not exist: {Path}", expanded);
            }
        }

        return paths;
    }

    private static async Task<IResult> ServeRangeRequestAsync(
        HttpContext context,
        FileInfo fileInfo,
        string contentType,
        string rangeHeader,
        ILogger logger)
    {
        var totalLength = fileInfo.Length;

        // Parse range header: "bytes=start-end" or "bytes=start-"
        var rangeSpec = rangeHeader.Substring(6); // Remove "bytes="
        var parts = rangeSpec.Split('-');

        long start = 0;
        long end = totalLength - 1;

        if (parts.Length >= 1 && long.TryParse(parts[0], out var parsedStart))
        {
            start = parsedStart;
        }

        if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var parsedEnd))
        {
            end = parsedEnd;
        }

        // Validate range
        if (start < 0 || start >= totalLength || end < start || end >= totalLength)
        {
            context.Response.StatusCode = 416; // Range Not Satisfiable
            context.Response.Headers.ContentRange = $"bytes */{totalLength}";
            return Results.Empty;
        }

        var length = end - start + 1;

        context.Response.StatusCode = 206; // Partial Content
        context.Response.Headers.AcceptRanges = "bytes";
        context.Response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";
        context.Response.Headers.ContentLength = length;
        context.Response.ContentType = contentType;

        logger.LogDebug("Serving range {Start}-{End}/{Total} for {File}",
            start, end, totalLength, fileInfo.Name);

        // Stream the requested range
        using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024]; // 64KB buffer
        var remaining = length;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
            if (read == 0) break;

            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }

        return Results.Empty;
    }
}
