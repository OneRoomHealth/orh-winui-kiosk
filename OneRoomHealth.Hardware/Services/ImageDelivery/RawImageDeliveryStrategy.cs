using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Services.ImageDelivery;

/// <summary>
/// Delivers a captured image as raw bytes via HTTP POST with
/// Content-Type matching the image MIME type (typically <c>image/jpeg</c>).
/// </summary>
public sealed class RawImageDeliveryStrategy : IImageDeliveryStrategy
{
    private readonly HttpClient _http;
    private readonly FireflyDownstreamConfig _config;
    private readonly ILogger _logger;

    public RawImageDeliveryStrategy(
        HttpClient http,
        FireflyDownstreamConfig config,
        ILogger logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ImageDeliveryResult> DeliverAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url)
            {
                Content = new ByteArrayContent(imageBytes)
            };
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            AddAuthHeader(request);

            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();

            var success = response.IsSuccessStatusCode;
            _logger.LogInformation(
                "Raw delivery to {Url}: {Status} in {Ms}ms ({Bytes} bytes)",
                _config.Url, (int)response.StatusCode, sw.ElapsedMilliseconds, imageBytes.Length);

            return success
                ? ImageDeliveryResult.Succeeded((int)response.StatusCode, body, sw.Elapsed)
                : ImageDeliveryResult.Failed((int)response.StatusCode, body, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Raw delivery to {Url} failed", _config.Url);
            return ImageDeliveryResult.Failed(0, ex.Message, sw.Elapsed);
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.AuthHeader))
            request.Headers.TryAddWithoutValidation("Authorization", _config.AuthHeader);
    }
}
