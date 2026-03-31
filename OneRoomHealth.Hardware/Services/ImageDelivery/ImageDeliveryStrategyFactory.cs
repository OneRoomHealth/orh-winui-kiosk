using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Services.ImageDelivery;

/// <summary>
/// Creates the correct <see cref="IImageDeliveryStrategy"/> based on
/// <see cref="FireflyDownstreamConfig.Method"/>.
/// Supported values (case-insensitive): "multipart", "base64", "raw".
/// </summary>
public static class ImageDeliveryStrategyFactory
{
    /// <summary>
    /// Build a delivery strategy from configuration.
    /// </summary>
    /// <param name="config">Downstream delivery configuration.</param>
    /// <param name="http">
    /// Shared <see cref="HttpClient"/> — caller owns the lifetime.
    /// Timeout should be configured before passing in.
    /// </param>
    /// <param name="logger">Logger for the strategy.</param>
    /// <returns>The configured strategy implementation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="FireflyDownstreamConfig.Method"/> is not a recognised value.
    /// </exception>
    public static IImageDeliveryStrategy Create(
        FireflyDownstreamConfig config,
        HttpClient http,
        ILogger logger)
    {
        return config.Method.ToLowerInvariant() switch
        {
            "multipart" => new MultipartImageDeliveryStrategy(http, config, logger),
            "base64"    => new Base64ImageDeliveryStrategy(http, config, logger),
            "raw"       => new RawImageDeliveryStrategy(http, config, logger),
            _ => throw new ArgumentException(
                $"Unknown image delivery method '{config.Method}'. " +
                "Supported values: multipart, base64, raw.",
                nameof(config))
        };
    }
}
