using OneRoomHealth.Hardware.Api.Controllers;

namespace KioskApp;

/// <summary>
/// Service that provides WebView2 navigation capabilities to the Hardware API server.
/// Implements the IWebViewNavigationService interface to allow chromium-compatible
/// endpoints to control the embedded WebView.
/// </summary>
public class WebViewNavigationService : ChromiumController.IWebViewNavigationService
{
    private readonly MainWindow _mainWindow;

    /// <summary>
    /// Creates a new WebViewNavigationService wrapping the specified MainWindow.
    /// </summary>
    /// <param name="mainWindow">The MainWindow containing the WebView2 to control.</param>
    public WebViewNavigationService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    /// <summary>
    /// Navigate the WebView to the specified URL.
    /// </summary>
    public async Task<bool> NavigateAsync(string url)
    {
        try
        {
            return await _mainWindow.NavigateToUrlAsync(url);
        }
        catch (Exception ex)
        {
            Logger.Log($"WebViewNavigationService.NavigateAsync failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get the current URL displayed in the WebView.
    /// </summary>
    public string? GetCurrentUrl()
    {
        return _mainWindow.CurrentUrl;
    }
}
