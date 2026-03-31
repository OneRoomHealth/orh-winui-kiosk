using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Services.ImageDelivery;

/// <summary>
/// Delivers a captured image as <c>multipart/form-data</c> via HTTP POST.
/// The image file is included under the field name configured in
/// <see cref="FireflyDownstreamConfig.MultipartFieldName"/>.
/// </summary>
public sealed class MultipartImageDeliveryStrategy : IImageDeliveryStrategy
{
    private readonly HttpClient _http;
    private readonly FireflyDownstreamConfig _config;
    private readonly ILogger _logger;

    public MultipartImageDeliveryStrategy(
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
            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            content.Add(imageContent, _config.MultipartFieldName, "capture.jpg");

            using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url)
            {
                Content = content
            };
            AddAuthHeader(request);

            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();

            var success = response.IsSuccessStatusCode;
            _logger.LogInformation(
                "Multipart delivery to {Url}: {Status} in {Ms}ms",
                _config.Url, (int)response.StatusCode, sw.ElapsedMilliseconds);

            return success
                ? ImageDeliveryResult.Succeeded((int)response.StatusCode, body, sw.Elapsed)
                : ImageDeliveryResult.Failed((int)response.StatusCode, body, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Multipart delivery to {Url} failed", _config.Url);
            return ImageDeliveryResult.Failed(0, ex.Message, sw.Elapsed);
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.AuthHeader))
            request.Headers.TryAddWithoutValidation("Authorization", _config.AuthHeader);
    }
}
