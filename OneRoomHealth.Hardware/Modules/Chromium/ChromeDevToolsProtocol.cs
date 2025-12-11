using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace OneRoomHealth.Hardware.Modules.Chromium;

/// <summary>
/// Simple Chrome DevTools Protocol (CDP) client for basic browser control.
/// </summary>
internal class ChromeDevToolsProtocol
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly int _port;

    public ChromeDevToolsProtocol(HttpClient httpClient, ILogger logger, int port = 9222)
    {
        _httpClient = httpClient;
        _logger = logger;
        _port = port;
    }

    /// <summary>
    /// Get list of all browser targets (pages/tabs).
    /// </summary>
    public async Task<List<CdpTarget>?> GetTargetsAsync()
    {
        try
        {
            var url = $"http://localhost:{_port}/json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var targets = JsonSerializer.Deserialize<List<CdpTarget>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return targets;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to get CDP targets: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Navigate to a URL by creating a new tab and closing old ones.
    /// </summary>
    public async Task<bool> NavigateToUrlAsync(string url)
    {
        try
        {
            // Get current targets
            var targets = await GetTargetsAsync();
            if (targets == null)
                return false;

            var pageTargets = targets.Where(t => t.Type == "page").ToList();

            // Create new tab with URL
            var createUrl = $"http://localhost:{_port}/json/new?{Uri.EscapeDataString(url)}";
            var createResponse = await _httpClient.PutAsync(createUrl, null);
            createResponse.EnsureSuccessStatusCode();

            _logger.LogInformation("CDP: Created new tab with URL: {Url}", url);

            // Close old tabs (keep only the new one)
            foreach (var target in pageTargets)
            {
                try
                {
                    var closeUrl = $"http://localhost:{_port}/json/close/{target.Id}";
                    await _httpClient.GetAsync(closeUrl);
                    _logger.LogDebug("CDP: Closed old tab {Id}", target.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("CDP: Failed to close tab {Id}: {Error}", target.Id, ex.Message);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CDP: Failed to navigate to URL: {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Activate (bring to front) a specific tab.
    /// </summary>
    public async Task<bool> ActivateTabAsync(string targetId)
    {
        try
        {
            var url = $"http://localhost:{_port}/json/activate/{targetId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("CDP: Activated tab {Id}", targetId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("CDP: Failed to activate tab {Id}: {Error}", targetId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Check if CDP is available (browser is running with debugging enabled).
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var targets = await GetTargetsAsync();
            return targets != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Represents a CDP target (tab/page).
/// </summary>
public class CdpTarget
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string WebSocketDebuggerUrl { get; set; } = "";
}
