using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OneRoomHealth.Hardware.Configuration;

namespace OneRoomHealth.Hardware.Services.ImageDelivery;

/// <summary>
/// Delivers a captured image as a JSON body <c>{ "image": "&lt;base64&gt;" }</c>
/// via HTTP POST with Content-Type application/json.
/// </summary>
public sealed class Base64ImageDeliveryStrategy : IImageDeliveryStrategy
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http;
    private readonly FireflyDownstreamConfig _config;
    private readonly ILogger _logger;

    public Base64ImageDeliveryStrategy(
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
            var payload = new { image = Convert.ToBase64String(imageBytes), contentType };
            var json = JsonSerializer.Serialize(payload, _jsonOpts);

            using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddAuthHeader(request);

            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();

            var success = response.IsSuccessStatusCode;
            _logger.LogInformation(
                "Base64 delivery to {Url}: {Status} in {Ms}ms",
                _config.Url, (int)response.StatusCode, sw.ElapsedMilliseconds);

            return success
                ? ImageDeliveryResult.Succeeded((int)response.StatusCode, body, sw.Elapsed)
                : ImageDeliveryResult.Failed((int)response.StatusCode, body, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Base64 delivery to {Url} failed", _config.Url);
            return ImageDeliveryResult.Failed(0, ex.Message, sw.Elapsed);
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.AuthHeader))
            request.Headers.TryAddWithoutValidation("Authorization", _config.AuthHeader);
    }
}
