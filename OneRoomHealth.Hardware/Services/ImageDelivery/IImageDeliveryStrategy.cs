namespace OneRoomHealth.Hardware.Services.ImageDelivery;

/// <summary>
/// Defines a strategy for forwarding a captured image to a downstream HTTP endpoint.
/// Implementations are selected at runtime by <see cref="ImageDeliveryStrategyFactory"/>
/// based on the configured delivery method.
/// </summary>
public interface IImageDeliveryStrategy
{
    /// <summary>
    /// Forwards <paramref name="imageBytes"/> to the configured downstream URL.
    /// </summary>
    /// <param name="imageBytes">Raw JPEG image data.</param>
    /// <param name="contentType">MIME type of the image (typically "image/jpeg").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="ImageDeliveryResult"/> indicating success or failure,
    /// including the HTTP status code returned by the downstream service.
    /// </returns>
    Task<ImageDeliveryResult> DeliverAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a downstream image delivery attempt.</summary>
public sealed class ImageDeliveryResult
{
    /// <summary>Whether the downstream service accepted the image (2xx status).</summary>
    public bool Success { get; init; }

    /// <summary>HTTP status code returned by the downstream service, or 0 on network error.</summary>
    public int StatusCode { get; init; }

    /// <summary>Response body or error message from the downstream service.</summary>
    public string? Message { get; init; }

    /// <summary>Duration of the delivery request.</summary>
    public TimeSpan Elapsed { get; init; }

    public static ImageDeliveryResult Succeeded(int statusCode, string? message, TimeSpan elapsed) =>
        new() { Success = true, StatusCode = statusCode, Message = message, Elapsed = elapsed };

    public static ImageDeliveryResult Failed(int statusCode, string? message, TimeSpan elapsed) =>
        new() { Success = false, StatusCode = statusCode, Message = message, Elapsed = elapsed };
}
