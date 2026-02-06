using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Device Control tab for debug mode.
/// Provides interactive hardware control via REST API calls to localhost:8081/api/v1.
/// </summary>
public sealed partial class MainWindow
{
    #region Device Control State

    private HttpClient? _dcHttpClient;
    private bool _deviceControlVisible = false;
    private string _dcActiveCategory = "System";
    private bool _dcShowingHistory = false;
    private bool _suppressSliderEvent = false;

    // Cancellation for pending operations when switching away from tab
    private CancellationTokenSource? _dcCancellationTokenSource;

    // Track active debounce timers for cleanup
    private readonly List<DispatcherTimer> _dcActiveDebounceTimers = new();

    // Request history
    private readonly ObservableCollection<ApiRequestRecord> _dcRequestHistory = new();
    private const int MaxHistoryEntries = 200;

    // Discovered device lists per category
    private readonly Dictionary<string, List<DeviceInfo>> _dcDevices = new();
    private readonly Dictionary<string, string?> _dcSelectedDeviceId = new();

    // UI element references for dynamic content
    private StackPanel? _dcControlsPanel;
    private Grid? _dcHistoryPanel;
    private TextBlock? _dcConnectionIndicator;
    private TextBlock? _dcRequestCountText;
    private Button? _dcHistoryToggleButton;

    // Category buttons for highlighting
    private readonly Dictionary<string, Button> _dcCategoryButtons = new();

    private static readonly string[] DcCategories = {
        "System", "Cameras", "Displays", "Lighting", "Sys Audio",
        "Mics", "Speakers", "Biamp", "Browser"
    };

    private static readonly string ApiBaseUrl = "http://localhost:8081/api/v1";

    #endregion

    #region Record Types

    private class ApiRequestRecord
    {
        public DateTime Timestamp { get; init; }
        public string Method { get; init; } = "";
        public string Endpoint { get; init; } = "";
        public int StatusCode { get; init; }
        public long ResponseTimeMs { get; init; }
        public string? ResponseBody { get; init; }
        public string? RequestBody { get; init; }
    }

    private class DeviceInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
    }

    #endregion

    #region Infrastructure

    private void InitializeDeviceControlHttpClient()
    {
        if (_dcHttpClient == null)
        {
            _dcHttpClient = new HttpClient
            {
                BaseAddress = new Uri(ApiBaseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }
    }

    private void CleanupDeviceControl()
    {
        // Cancel any pending HTTP requests and async operations
        _dcCancellationTokenSource?.Cancel();
        _dcCancellationTokenSource?.Dispose();
        _dcCancellationTokenSource = null;

        // Stop and dispose all active debounce timers
        foreach (var timer in _dcActiveDebounceTimers)
        {
            timer.Stop();
        }
        _dcActiveDebounceTimers.Clear();

        _dcHttpClient?.Dispose();
        _dcHttpClient = null;
        _dcRequestHistory.Clear();
        _dcDevices.Clear();
        _dcSelectedDeviceId.Clear();
        _dcCategoryButtons.Clear();
        _deviceControlVisible = false;
    }

    /// <summary>
    /// Makes an API request and records it in history.
    /// Uses cancellation token to abort requests when switching away from tab.
    /// </summary>
    private async Task<(int statusCode, string? body)> DcApiRequest(string method, string endpoint, string? requestBody = null)
    {
        // Get cancellation token - if null, tab is not active, skip request
        var cancellationToken = _dcCancellationTokenSource?.Token ?? default;
        if (cancellationToken.IsCancellationRequested)
        {
            return (0, "Request cancelled - tab no longer active");
        }

        InitializeDeviceControlHttpClient();

        var sw = Stopwatch.StartNew();
        int statusCode = 0;
        string? responseBody = null;

        try
        {
            HttpResponseMessage response;
            var httpMethod = method.ToUpperInvariant() switch
            {
                "GET" => HttpMethod.Get,
                "PUT" => HttpMethod.Put,
                "POST" => HttpMethod.Post,
                "DELETE" => HttpMethod.Delete,
                _ => HttpMethod.Get
            };

            var request = new HttpRequestMessage(httpMethod, endpoint);
            if (requestBody != null && (httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Post))
            {
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            }

            response = await _dcHttpClient!.SendAsync(request, cancellationToken);
            statusCode = (int)response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            // Don't record cancelled requests - user switched away from tab
            return (0, "Request cancelled");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            sw.Stop();
            // Don't record cancelled requests - user switched away from tab
            return (0, "Request cancelled");
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            statusCode = 0;
            responseBody = "Request timed out";
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            statusCode = 0;
            responseBody = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            statusCode = 0;
            responseBody = $"Error: {ex.Message}";
        }

        // Record in history - only if tab is still visible
        var record = new ApiRequestRecord
        {
            Timestamp = DateTime.Now,
            Method = method.ToUpperInvariant(),
            Endpoint = endpoint,
            StatusCode = statusCode,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            ResponseBody = responseBody,
            RequestBody = requestBody
        };

        DispatcherQueue.TryEnqueue(() =>
        {
            // Check if tab is still active before updating UI
            if (!_deviceControlVisible) return;

            _dcRequestHistory.Insert(0, record);
            while (_dcRequestHistory.Count > MaxHistoryEntries)
            {
                _dcRequestHistory.RemoveAt(_dcRequestHistory.Count - 1);
            }
            if (_dcRequestCountText != null)
            {
                _dcRequestCountText.Text = $"{_dcRequestHistory.Count} requests";
            }
        });

        return (statusCode, responseBody);
    }

    /// <summary>
    /// Formats JSON for display.
    /// </summary>
    private static string FormatJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "(empty)";
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    #endregion

    #region Tab Activation

    private void DeviceControlTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(DebugTab.DeviceControl);
    }

    internal void RefreshDeviceControlDisplay()
    {
        if (!_deviceControlVisible) return;

        InitializeDeviceControlHttpClient();

        // Test connection
        _ = TestDcConnection();

        // Rebuild controls for active category
        BuildCategoryControls(_dcActiveCategory);
    }

    private async Task TestDcConnection()
    {
        try
        {
            var (status, _) = await DcApiRequest("GET", "status");
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_dcConnectionIndicator != null)
                {
                    _dcConnectionIndicator.Text = "\u25CF";
                    _dcConnectionIndicator.Foreground = new SolidColorBrush(
                        status >= 200 && status < 300
                            ? ColorHelper.FromArgb(255, 78, 201, 176)   // green
                            : ColorHelper.FromArgb(255, 244, 135, 113)); // red
                }
            });
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_dcConnectionIndicator != null)
                {
                    _dcConnectionIndicator.Text = "\u25CF";
                    _dcConnectionIndicator.Foreground = new SolidColorBrush(
                        ColorHelper.FromArgb(255, 244, 135, 113));
                }
            });
        }
    }

    /// <summary>
    /// One-time initialization: wire XAML elements to fields and build category buttons.
    /// </summary>
    private void InitializeDeviceControlTab()
    {
        // Create new cancellation token source for this tab session
        _dcCancellationTokenSource?.Cancel();
        _dcCancellationTokenSource?.Dispose();
        _dcCancellationTokenSource = new CancellationTokenSource();

        // Clear any leftover debounce timers from previous session
        foreach (var timer in _dcActiveDebounceTimers)
        {
            timer.Stop();
        }
        _dcActiveDebounceTimers.Clear();

        // Wire up XAML-defined elements
        _dcControlsPanel = DcControlsPanel;
        _dcHistoryPanel = DcHistoryPanel;
        _dcConnectionIndicator = DcConnectionIndicator;
        _dcRequestCountText = DcRequestCountText;
        _dcHistoryToggleButton = DcHistoryToggleButton;

        // Build category buttons if not already built
        if (DcCategoryBar.Children.Count == 0)
        {
            _dcCategoryButtons.Clear();
            foreach (var category in DcCategories)
            {
                var btn = new Button
                {
                    Content = category,
                    Tag = category,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 2, 10, 2),
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    FontSize = 11,
                    MinHeight = 24
                };
                btn.Click += DcCategoryButton_Click;
                DcCategoryBar.Children.Add(btn);
                _dcCategoryButtons[category] = btn;
            }

            UpdateDcCategoryStyles();
        }
    }

    #endregion

    #region Category Navigation

    private void DcCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
        {
            _dcActiveCategory = category;
            UpdateDcCategoryStyles();
            _dcShowingHistory = false;
            BuildCategoryControls(category);
        }
    }

    private void UpdateDcCategoryStyles()
    {
        foreach (var kvp in _dcCategoryButtons)
        {
            if (kvp.Key == _dcActiveCategory)
            {
                kvp.Value.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 79, 72)); // #264F48
                kvp.Value.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 78, 201, 176)); // #4EC9B0
                kvp.Value.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 78, 201, 176));
            }
            else
            {
                kvp.Value.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)); // #3C3C3C
                kvp.Value.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60));
                kvp.Value.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)); // #CCCCCC
            }
        }
    }

    #endregion

    #region Dynamic Control Builders

    private void BuildCategoryControls(string category)
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Clear();

        if (_dcHistoryPanel != null)
        {
            _dcHistoryPanel.Visibility = Visibility.Collapsed;
        }

        switch (category)
        {
            case "System":
                _ = BuildSystemControls();
                break;
            case "Cameras":
                _ = BuildCameraControls();
                break;
            case "Displays":
                _ = BuildDisplayControls();
                break;
            case "Lighting":
                _ = BuildLightingControls();
                break;
            case "Sys Audio":
                _ = BuildSystemAudioControls();
                break;
            case "Mics":
                _ = BuildMicrophoneControls();
                break;
            case "Speakers":
                _ = BuildSpeakerControls();
                break;
            case "Biamp":
                _ = BuildBiampControls();
                break;
            case "Browser":
                _ = BuildChromiumControls();
                break;
        }
    }

    // ---- System Controls ----
    private async Task BuildSystemControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("SYSTEM STATUS"));

        var responseArea = CreateResponseArea();

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 8) };
        buttonRow.Children.Add(CreateActionButton("Get Status", "#4EC9B0", async () =>
        {
            var (_, body) = await DcApiRequest("GET", "status");
            SetResponseText(responseArea, body);
        }));
        buttonRow.Children.Add(CreateActionButton("List Devices", "#569CD6", async () =>
        {
            var (_, body) = await DcApiRequest("GET", "devices");
            SetResponseText(responseArea, body);
        }));
        buttonRow.Children.Add(CreateActionButton("System Info", "#569CD6", async () =>
        {
            var (_, body) = await DcApiRequest("GET", "system");
            SetResponseText(responseArea, body);
        }));

        _dcControlsPanel.Children.Add(buttonRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Auto-fetch status
        var (_, statusBody) = await DcApiRequest("GET", "status");
        SetResponseText(responseArea, statusBody);
    }

    // ---- Camera Controls ----
    private async Task BuildCameraControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("CAMERAS"));

        var responseArea = CreateResponseArea();

        // Discover devices
        var (status, body) = await DcApiRequest("GET", "cameras");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Cameras"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No cameras discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        // Device selector
        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Cameras") ?? devices[0].Id;
        _dcSelectedDeviceId["Cameras"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Cameras"] = id;
            BuildCategoryControls("Cameras");
        }));

        // Controls grid
        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        // PTZ sliders
        Slider? panSlider = null, tiltSlider = null, zoomSlider = null;

        panSlider = AddSliderRow(controlsGrid, "Pan", -100, 100, 1, 0, async (val) =>
        {
            var panVal = val / 100.0;
            await DcApiRequest("PUT", $"cameras/{selectedId}/ptz",
                JsonSerializer.Serialize(new { pan = panVal }));
        });

        tiltSlider = AddSliderRow(controlsGrid, "Tilt", -100, 100, 1, 0, async (val) =>
        {
            var tiltVal = val / 100.0;
            await DcApiRequest("PUT", $"cameras/{selectedId}/ptz",
                JsonSerializer.Serialize(new { tilt = tiltVal }));
        });

        zoomSlider = AddSliderRow(controlsGrid, "Zoom", 0, 100, 1, 0, async (val) =>
        {
            var zoomVal = val / 100.0;
            await DcApiRequest("PUT", $"cameras/{selectedId}/ptz",
                JsonSerializer.Serialize(new { zoom = zoomVal }));
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        // Action buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Enable", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"cameras/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Disable", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"cameras/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Auto-Track ON", "#DCDCAA", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"cameras/{selectedId}/auto-tracking",
                JsonSerializer.Serialize(new { enabled = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Auto-Track OFF", "#858585", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"cameras/{selectedId}/auto-tracking",
                JsonSerializer.Serialize(new { enabled = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"cameras/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial PTZ state
        try
        {
            var (ptzStatus, ptzBody) = await DcApiRequest("GET", $"cameras/{selectedId}/ptz");
            if (ptzStatus == 200 && ptzBody != null)
            {
                var ptzDoc = JsonDocument.Parse(ptzBody);
                _suppressSliderEvent = true;
                try
                {
                    if (ptzDoc.RootElement.TryGetProperty("pan", out var panEl))
                        panSlider.Value = panEl.GetDouble() * 100;
                    if (ptzDoc.RootElement.TryGetProperty("tilt", out var tiltEl))
                        tiltSlider.Value = tiltEl.GetDouble() * 100;
                    if (ptzDoc.RootElement.TryGetProperty("zoom", out var zoomEl))
                        zoomSlider.Value = zoomEl.GetDouble() * 100;
                }
                finally
                {
                    _suppressSliderEvent = false;
                }
            }
            SetResponseText(responseArea, ptzBody);
        }
        catch { /* non-fatal */ }
    }

    // ---- Display Controls ----
    private async Task BuildDisplayControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("DISPLAYS"));

        var responseArea = CreateResponseArea();

        var (status, body) = await DcApiRequest("GET", "displays");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Displays"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No displays discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Displays") ?? devices[0].Id;
        _dcSelectedDeviceId["Displays"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Displays"] = id;
            BuildCategoryControls("Displays");
        }));

        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var brightnessSlider = AddSliderRow(controlsGrid, "Brightness", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", $"displays/{selectedId}/brightness",
                JsonSerializer.Serialize(new { brightness = (int)val }));
            SetResponseText(responseArea, r);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Enable", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"displays/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Blackout", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"displays/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"displays/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial brightness
        try
        {
            var (bStatus, bBody) = await DcApiRequest("GET", $"displays/{selectedId}/brightness");
            if (bStatus == 200 && bBody != null)
            {
                var doc = JsonDocument.Parse(bBody);
                _suppressSliderEvent = true;
                try
                {
                    if (doc.RootElement.TryGetProperty("brightness", out var bEl))
                        brightnessSlider.Value = bEl.GetInt32();
                }
                finally { _suppressSliderEvent = false; }
            }
            SetResponseText(responseArea, bBody);
        }
        catch { }
    }

    // ---- Lighting Controls ----
    private async Task BuildLightingControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("LIGHTING"));

        var responseArea = CreateResponseArea();

        var (status, body) = await DcApiRequest("GET", "lighting");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Lighting"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No lights discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Lighting") ?? devices[0].Id;
        _dcSelectedDeviceId["Lighting"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Lighting"] = id;
            BuildCategoryControls("Lighting");
        }));

        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var brightnessSlider = AddSliderRow(controlsGrid, "Brightness", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", $"lighting/{selectedId}/brightness",
                JsonSerializer.Serialize(new { brightness = (int)val }));
            SetResponseText(responseArea, r);
        });

        Slider? redSlider = null, greenSlider = null, blueSlider = null;

        redSlider = AddSliderRow(controlsGrid, "Red", 0, 255, 1, 255, async (val) =>
        {
            await SendLightingColor(selectedId, (int)val,
                (int)(greenSlider?.Value ?? 0), (int)(blueSlider?.Value ?? 0), responseArea);
        });

        greenSlider = AddSliderRow(controlsGrid, "Green", 0, 255, 1, 255, async (val) =>
        {
            await SendLightingColor(selectedId, (int)(redSlider?.Value ?? 0),
                (int)val, (int)(blueSlider?.Value ?? 0), responseArea);
        });

        blueSlider = AddSliderRow(controlsGrid, "Blue", 0, 255, 1, 255, async (val) =>
        {
            await SendLightingColor(selectedId, (int)(redSlider?.Value ?? 0),
                (int)(greenSlider?.Value ?? 0), (int)val, responseArea);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        // Color presets
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        presetRow.Children.Add(CreateActionButton("Red", "#F48771", async () =>
        {
            _suppressSliderEvent = true;
            try { redSlider.Value = 255; greenSlider.Value = 0; blueSlider.Value = 0; }
            finally { _suppressSliderEvent = false; }
            await SendLightingColor(selectedId, 255, 0, 0, responseArea);
        }));
        presetRow.Children.Add(CreateActionButton("Green", "#4EC9B0", async () =>
        {
            _suppressSliderEvent = true;
            try { redSlider.Value = 0; greenSlider.Value = 255; blueSlider.Value = 0; }
            finally { _suppressSliderEvent = false; }
            await SendLightingColor(selectedId, 0, 255, 0, responseArea);
        }));
        presetRow.Children.Add(CreateActionButton("Blue", "#569CD6", async () =>
        {
            _suppressSliderEvent = true;
            try { redSlider.Value = 0; greenSlider.Value = 0; blueSlider.Value = 255; }
            finally { _suppressSliderEvent = false; }
            await SendLightingColor(selectedId, 0, 0, 255, responseArea);
        }));
        presetRow.Children.Add(CreateActionButton("White", "#CCCCCC", async () =>
        {
            _suppressSliderEvent = true;
            try { redSlider.Value = 255; greenSlider.Value = 255; blueSlider.Value = 255; }
            finally { _suppressSliderEvent = false; }
            await SendLightingColor(selectedId, 255, 255, 255, responseArea);
        }));
        _dcControlsPanel.Children.Add(presetRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Enable", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"lighting/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Disable", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"lighting/{selectedId}/enable",
                JsonSerializer.Serialize(new { enabled = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"lighting/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial state
        try
        {
            var (cStatus, cBody) = await DcApiRequest("GET", $"lighting/{selectedId}");
            if (cStatus == 200 && cBody != null)
            {
                var doc = JsonDocument.Parse(cBody);
                _suppressSliderEvent = true;
                try
                {
                    if (doc.RootElement.TryGetProperty("brightness", out var bEl))
                        brightnessSlider.Value = bEl.GetInt32();
                    if (doc.RootElement.TryGetProperty("color", out var colorEl))
                    {
                        if (colorEl.TryGetProperty("red", out var rEl)) redSlider.Value = rEl.GetInt32();
                        if (colorEl.TryGetProperty("green", out var gEl)) greenSlider.Value = gEl.GetInt32();
                        if (colorEl.TryGetProperty("blue", out var bEl2)) blueSlider.Value = bEl2.GetInt32();
                    }
                }
                finally { _suppressSliderEvent = false; }
            }
            SetResponseText(responseArea, cBody);
        }
        catch { }
    }

    private async Task SendLightingColor(string deviceId, int r, int g, int b, ScrollViewer responseArea)
    {
        var (_, resp) = await DcApiRequest("PUT", $"lighting/{deviceId}/color",
            JsonSerializer.Serialize(new { red = r, green = g, blue = b }));
        SetResponseText(responseArea, resp);
    }

    // ---- System Audio Controls ----
    private async Task BuildSystemAudioControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("SYSTEM AUDIO"));

        var responseArea = CreateResponseArea();
        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var speakerVolSlider = AddSliderRow(controlsGrid, "Speaker Vol", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/volume",
                JsonSerializer.Serialize(new { volume = (int)val }));
            SetResponseText(responseArea, r);
        });

        var micVolSlider = AddSliderRow(controlsGrid, "Mic Vol", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/mic-volume",
                JsonSerializer.Serialize(new { volume = (int)val }));
            SetResponseText(responseArea, r);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Mute", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/mute",
                JsonSerializer.Serialize(new { muted = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Unmute", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/mute",
                JsonSerializer.Serialize(new { muted = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Mic Mute", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/mic-mute",
                JsonSerializer.Serialize(new { muted = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Mic Unmute", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", "system/mic-mute",
                JsonSerializer.Serialize(new { muted = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Vol Up", "#DCDCAA", async () =>
        {
            var (_, r) = await DcApiRequest("POST", "system/volume-up",
                JsonSerializer.Serialize(new { step = 5 }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Vol Down", "#DCDCAA", async () =>
        {
            var (_, r) = await DcApiRequest("POST", "system/volume-down",
                JsonSerializer.Serialize(new { step = 5 }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", "system");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial state
        try
        {
            var (sStatus, sBody) = await DcApiRequest("GET", "system");
            if (sStatus == 200 && sBody != null)
            {
                var doc = JsonDocument.Parse(sBody);
                _suppressSliderEvent = true;
                try
                {
                    if (doc.RootElement.TryGetProperty("volume", out var vEl))
                        speakerVolSlider.Value = vEl.GetInt32();
                    if (doc.RootElement.TryGetProperty("mic_volume", out var mvEl))
                        micVolSlider.Value = mvEl.GetInt32();
                }
                finally { _suppressSliderEvent = false; }
            }
            SetResponseText(responseArea, sBody);
        }
        catch { }
    }

    // ---- Microphone Controls ----
    private async Task BuildMicrophoneControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("MICROPHONES"));

        var responseArea = CreateResponseArea();

        var (status, body) = await DcApiRequest("GET", "microphones");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Mics"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No microphones discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Mics") ?? devices[0].Id;
        _dcSelectedDeviceId["Mics"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Mics"] = id;
            BuildCategoryControls("Mics");
        }));

        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var volSlider = AddSliderRow(controlsGrid, "Volume", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", $"microphones/{selectedId}/volume",
                JsonSerializer.Serialize(new { volume = (int)val }));
            SetResponseText(responseArea, r);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Mute", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"microphones/{selectedId}/mute",
                JsonSerializer.Serialize(new { muted = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Unmute", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("PUT", $"microphones/{selectedId}/mute",
                JsonSerializer.Serialize(new { muted = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"microphones/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial state
        try
        {
            var (mStatus, mBody) = await DcApiRequest("GET", $"microphones/{selectedId}");
            if (mStatus == 200 && mBody != null)
            {
                var doc = JsonDocument.Parse(mBody);
                _suppressSliderEvent = true;
                try
                {
                    if (doc.RootElement.TryGetProperty("volume", out var vEl))
                        volSlider.Value = vEl.GetInt32();
                }
                finally { _suppressSliderEvent = false; }
            }
            SetResponseText(responseArea, mBody);
        }
        catch { }
    }

    // ---- Speaker Controls ----
    private async Task BuildSpeakerControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("SPEAKERS"));

        var responseArea = CreateResponseArea();
        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var volSlider = AddSliderRow(controlsGrid, "Volume", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("PUT", "speakers/volume",
                JsonSerializer.Serialize(new { volume = (int)val }));
            SetResponseText(responseArea, r);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Get Volume", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", "speakers/volume");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial state
        try
        {
            var (sStatus, sBody) = await DcApiRequest("GET", "speakers/volume");
            if (sStatus == 200 && sBody != null)
            {
                var doc = JsonDocument.Parse(sBody);
                _suppressSliderEvent = true;
                try
                {
                    if (doc.RootElement.TryGetProperty("volume", out var vEl))
                        volSlider.Value = vEl.GetInt32();
                }
                finally { _suppressSliderEvent = false; }
            }
            SetResponseText(responseArea, sBody);
        }
        catch { }
    }

    // ---- Biamp Controls ----
    private async Task BuildBiampControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("BIAMP CONFERENCE BAR"));

        var responseArea = CreateResponseArea();

        var (status, body) = await DcApiRequest("GET", "biamp");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Biamp"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No Biamp devices discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Biamp") ?? devices[0].Id;
        _dcSelectedDeviceId["Biamp"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Biamp"] = id;
            BuildCategoryControls("Biamp");
        }));

        var controlsGrid = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var panSlider = AddSliderRow(controlsGrid, "Pan", -100, 100, 1, 0, async (val) =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/pan",
                JsonSerializer.Serialize(new { pan = val / 100.0 }));
            SetResponseText(responseArea, r);
        });

        var tiltSlider = AddSliderRow(controlsGrid, "Tilt", -100, 100, 1, 0, async (val) =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/tilt",
                JsonSerializer.Serialize(new { tilt = val / 100.0 }));
            SetResponseText(responseArea, r);
        });

        var zoomSlider = AddSliderRow(controlsGrid, "Zoom", 0, 100, 1, 50, async (val) =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/zoom",
                JsonSerializer.Serialize(new { zoom = val / 100.0 }));
            SetResponseText(responseArea, r);
        });

        _dcControlsPanel.Children.Add(controlsGrid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Autoframe ON", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/autoframing",
                JsonSerializer.Serialize(new { enabled = true }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Autoframe OFF", "#858585", async () =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/autoframing",
                JsonSerializer.Serialize(new { enabled = false }));
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Toggle Autoframe", "#DCDCAA", async () =>
        {
            var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/autoframing/toggle", "{}");
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"biamp/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Reboot", "#F48771", async () =>
        {
            // Show confirmation dialog
            var dialog = new ContentDialog
            {
                Title = "Confirm Reboot",
                Content = $"Are you sure you want to reboot Biamp device {selectedId}?",
                PrimaryButtonText = "Reboot",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var (_, r) = await DcApiRequest("POST", $"biamp/{selectedId}/reboot", "{}");
                SetResponseText(responseArea, r);
            }
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial PTZ
        try
        {
            _suppressSliderEvent = true;
            try
            {
                var (pStatus, pBody) = await DcApiRequest("GET", $"biamp/{selectedId}/pan");
                if (pStatus == 200 && pBody != null)
                {
                    var doc = JsonDocument.Parse(pBody);
                    if (doc.RootElement.TryGetProperty("pan", out var pEl))
                        panSlider.Value = pEl.GetDouble() * 100;
                }

                var (tStatus, tBody) = await DcApiRequest("GET", $"biamp/{selectedId}/tilt");
                if (tStatus == 200 && tBody != null)
                {
                    var doc = JsonDocument.Parse(tBody);
                    if (doc.RootElement.TryGetProperty("tilt", out var tEl))
                        tiltSlider.Value = tEl.GetDouble() * 100;
                }

                var (zStatus, zBody) = await DcApiRequest("GET", $"biamp/{selectedId}/zoom");
                if (zStatus == 200 && zBody != null)
                {
                    var doc = JsonDocument.Parse(zBody);
                    if (doc.RootElement.TryGetProperty("zoom", out var zEl))
                        zoomSlider.Value = zEl.GetDouble() * 100;
                }
            }
            finally { _suppressSliderEvent = false; }

            var (sStatus, sBody) = await DcApiRequest("GET", $"biamp/{selectedId}");
            SetResponseText(responseArea, sBody);
        }
        catch { }
    }

    // ---- Chromium/Browser Controls ----
    private async Task BuildChromiumControls()
    {
        if (_dcControlsPanel == null) return;

        _dcControlsPanel.Children.Add(CreateSectionHeader("BROWSER CONTROL"));

        var responseArea = CreateResponseArea();

        var (status, body) = await DcApiRequest("GET", "chromium");
        var devices = ParseDeviceList(body, "devices", "id", "name");
        _dcDevices["Browser"] = devices;

        if (devices.Count == 0)
        {
            _dcControlsPanel.Children.Add(CreateNoDevicesMessage("No browser instances discovered"));
            _dcControlsPanel.Children.Add(responseArea);
            SetResponseText(responseArea, body);
            return;
        }

        var selectedId = _dcSelectedDeviceId.GetValueOrDefault("Browser") ?? devices[0].Id;
        _dcSelectedDeviceId["Browser"] = selectedId;
        _dcControlsPanel.Children.Add(CreateDeviceSelector(devices, selectedId, id =>
        {
            _dcSelectedDeviceId["Browser"] = id;
            BuildCategoryControls("Browser");
        }));

        // URL input
        var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var urlInput = new TextBox
        {
            PlaceholderText = "Enter URL to navigate...",
            Width = 400,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
            RequestedTheme = ElementTheme.Dark
        };
        urlRow.Children.Add(urlInput);
        urlRow.Children.Add(CreateActionButton("Navigate", "#4EC9B0", async () =>
        {
            if (!string.IsNullOrWhiteSpace(urlInput.Text))
            {
                var (_, r) = await DcApiRequest("PUT", $"chromium/{selectedId}/url",
                    JsonSerializer.Serialize(new { url = urlInput.Text }));
                SetResponseText(responseArea, r);
            }
        }));
        _dcControlsPanel.Children.Add(urlRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(CreateActionButton("Get Status", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"chromium/{selectedId}");
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Get URL", "#569CD6", async () =>
        {
            var (_, r) = await DcApiRequest("GET", $"chromium/{selectedId}/url");
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Open", "#4EC9B0", async () =>
        {
            var (_, r) = await DcApiRequest("POST", $"chromium/{selectedId}/open", "{}");
            SetResponseText(responseArea, r);
        }));
        btnRow.Children.Add(CreateActionButton("Close", "#F48771", async () =>
        {
            var (_, r) = await DcApiRequest("POST", $"chromium/{selectedId}/close", "{}");
            SetResponseText(responseArea, r);
        }));
        _dcControlsPanel.Children.Add(btnRow);
        _dcControlsPanel.Children.Add(responseArea);

        // Fetch initial state
        var (sStatus, sBody) = await DcApiRequest("GET", $"chromium/{selectedId}");
        SetResponseText(responseArea, sBody);
        if (sStatus == 200 && sBody != null)
        {
            try
            {
                var doc = JsonDocument.Parse(sBody);
                if (doc.RootElement.TryGetProperty("url", out var urlEl))
                    urlInput.Text = urlEl.GetString() ?? "";
            }
            catch { }
        }
    }

    #endregion

    #region Request History Panel

    private void DcHistoryToggle_Click(object sender, RoutedEventArgs e)
    {
        _dcShowingHistory = !_dcShowingHistory;

        if (_dcShowingHistory)
        {
            ShowHistoryPanel();
        }
        else
        {
            _dcHistoryPanel!.Visibility = Visibility.Collapsed;
            BuildCategoryControls(_dcActiveCategory);
        }

        if (_dcHistoryToggleButton != null)
        {
            _dcHistoryToggleButton.Foreground = new SolidColorBrush(
                _dcShowingHistory
                    ? Windows.UI.Color.FromArgb(255, 78, 201, 176)
                    : Windows.UI.Color.FromArgb(255, 204, 204, 204));
        }
    }

    private void ShowHistoryPanel()
    {
        if (_dcControlsPanel == null || _dcHistoryPanel == null) return;

        _dcControlsPanel.Children.Clear();
        _dcHistoryPanel.Visibility = Visibility.Visible;

        // Rebuild history list
        var historyStack = _dcHistoryPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
        if (historyStack == null) return;

        var panel = historyStack.Content as StackPanel;
        if (panel == null) return;

        panel.Children.Clear();

        if (_dcRequestHistory.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No requests yet",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
                Margin = new Thickness(12)
            });
            return;
        }

        foreach (var record in _dcRequestHistory)
        {
            panel.Children.Add(CreateHistoryEntry(record));
        }
    }

    private Border CreateHistoryEntry(ApiRequestRecord record)
    {
        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6, 8, 6)
        };

        var stack = new StackPanel { Spacing = 2 };

        // Top row: time + method badge + endpoint + status + timing
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        topRow.Children.Add(new TextBlock
        {
            Text = record.Timestamp.ToString("HH:mm:ss"),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Method badge
        var methodColor = record.Method switch
        {
            "GET" => Windows.UI.Color.FromArgb(255, 86, 156, 214),   // blue
            "PUT" => Windows.UI.Color.FromArgb(255, 220, 220, 170),  // orange/yellow
            "POST" => Windows.UI.Color.FromArgb(255, 78, 201, 176),  // green
            "DELETE" => Windows.UI.Color.FromArgb(255, 244, 135, 113), // red
            _ => Windows.UI.Color.FromArgb(255, 204, 204, 204)
        };
        var methodBg = Windows.UI.Color.FromArgb(40, methodColor.R, methodColor.G, methodColor.B);

        var methodBadge = new Border
        {
            Background = new SolidColorBrush(methodBg),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1)
        };
        methodBadge.Child = new TextBlock
        {
            Text = record.Method,
            Foreground = new SolidColorBrush(methodColor),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        };
        topRow.Children.Add(methodBadge);

        topRow.Children.Add(new TextBlock
        {
            Text = $"/{record.Endpoint}",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        topRow.Children.Add(new TextBlock
        {
            Text = "\u2192",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Status code color
        var statusColor = record.StatusCode switch
        {
            >= 200 and < 300 => Windows.UI.Color.FromArgb(255, 78, 201, 176),   // green
            >= 400 and < 500 => Windows.UI.Color.FromArgb(255, 220, 220, 170),  // yellow
            >= 500 => Windows.UI.Color.FromArgb(255, 244, 135, 113),            // red
            _ => Windows.UI.Color.FromArgb(255, 244, 135, 113)                  // red (0/error)
        };

        topRow.Children.Add(new TextBlock
        {
            Text = record.StatusCode == 0 ? "ERR" : record.StatusCode.ToString(),
            Foreground = new SolidColorBrush(statusColor),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        topRow.Children.Add(new TextBlock
        {
            Text = $"({record.ResponseTimeMs}ms)",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        stack.Children.Add(topRow);

        // Expandable response body (collapsed by default)
        var responseText = new TextBlock
        {
            Text = FormatJson(record.ResponseBody),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // Click to expand/collapse
        container.PointerPressed += (s, e) =>
        {
            if (responseText.Visibility == Visibility.Collapsed)
            {
                responseText.Visibility = Visibility.Visible;
                responseText.MaxLines = 0; // unlimited
                responseText.TextTrimming = TextTrimming.None;
            }
            else
            {
                responseText.Visibility = Visibility.Collapsed;
                responseText.MaxLines = 1;
                responseText.TextTrimming = TextTrimming.CharacterEllipsis;
            }
        };

        stack.Children.Add(responseText);
        container.Child = stack;
        return container;
    }

    private void DcClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _dcRequestHistory.Clear();
        if (_dcShowingHistory)
        {
            ShowHistoryPanel();
        }
        if (_dcRequestCountText != null)
        {
            _dcRequestCountText.Text = "0 requests";
        }
    }

    #endregion

    #region Reusable UI Helpers

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 50,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private Slider AddSliderRow(StackPanel parent, string label, double min, double max, double step, double value, Func<double, Task> onChanged)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value.ToString("F0"),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 78, 201, 176)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(valueBlock, 2);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = value,
            MinWidth = 150,
            RequestedTheme = ElementTheme.Dark,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(slider, 1);

        // Debounce timer for slider - tracked for cleanup
        DispatcherTimer? debounceTimer = null;

        slider.ValueChanged += (s, e) =>
        {
            valueBlock.Text = e.NewValue.ToString("F0");

            if (_suppressSliderEvent) return;

            // Check if tab is still active before setting up debounce
            if (!_deviceControlVisible) return;

            // Debounce: wait 150ms after last change before sending API call
            if (debounceTimer != null)
            {
                debounceTimer.Stop();
                _dcActiveDebounceTimers.Remove(debounceTimer);
            }

            debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _dcActiveDebounceTimers.Add(debounceTimer);

            debounceTimer.Tick += (_, _) =>
            {
                debounceTimer.Stop();
                _dcActiveDebounceTimers.Remove(debounceTimer);

                // Final check before executing - tab might have been closed during debounce
                if (!_deviceControlVisible) return;

                _ = onChanged(e.NewValue);
            };
            debounceTimer.Start();
        };

        row.Children.Add(labelBlock);
        row.Children.Add(slider);
        row.Children.Add(valueBlock);
        parent.Children.Add(row);

        return slider;
    }

    private Button CreateActionButton(string text, string colorHex, Func<Task> onClick)
    {
        var color = ParseHexColor(colorHex);
        var btn = new Button
        {
            Content = text,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, color.R, color.G, color.B)),
            Foreground = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(4),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            MinHeight = 28
        };

        btn.Click += async (s, e) =>
        {
            btn.IsEnabled = false;
            try { await onClick(); }
            catch (Exception ex) { Logger.Log($"Device Control action error: {ex.Message}"); }
            finally { btn.IsEnabled = true; }
        };

        return btn;
    }

    private StackPanel CreateDeviceSelector(List<DeviceInfo> devices, string currentId, Action<string> onSelect)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 4)
        };

        row.Children.Add(new TextBlock
        {
            Text = "Device:",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });

        foreach (var device in devices)
        {
            var isActive = device.Id == currentId;
            var chip = new Button
            {
                Content = string.IsNullOrEmpty(device.Name) ? $"ID: {device.Id}" : device.Name,
                Tag = device.Id,
                Background = new SolidColorBrush(isActive
                    ? Windows.UI.Color.FromArgb(255, 38, 79, 72)    // #264F48
                    : Windows.UI.Color.FromArgb(255, 60, 60, 60)),  // #3C3C3C
                Foreground = new SolidColorBrush(isActive
                    ? Windows.UI.Color.FromArgb(255, 78, 201, 176)  // #4EC9B0
                    : Windows.UI.Color.FromArgb(255, 204, 204, 204)),
                BorderBrush = new SolidColorBrush(isActive
                    ? Windows.UI.Color.FromArgb(255, 78, 201, 176)
                    : Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 2, 10, 2),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 11,
                MinHeight = 24
            };

            chip.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is string id)
                {
                    onSelect(id);
                }
            };

            row.Children.Add(chip);
        }

        return row;
    }

    private ScrollViewer CreateResponseArea()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 120,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0)
        };

        scrollViewer.Content = new TextBlock
        {
            Text = "Waiting for response...",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        return scrollViewer;
    }

    private static void SetResponseText(ScrollViewer responseArea, string? body)
    {
        if (responseArea.Content is TextBlock tb)
        {
            tb.Text = FormatJson(body);
            tb.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204));
        }
    }

    private static TextBlock CreateNoDevicesMessage(string message)
    {
        return new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 170)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    #endregion

    #region Parsing Helpers

    private static List<DeviceInfo> ParseDeviceList(string? json, string arrayProp, string idField, string nameField)
    {
        var devices = new List<DeviceInfo>();
        if (string.IsNullOrWhiteSpace(json)) return devices;

        try
        {
            var doc = JsonDocument.Parse(json);

            // Try to find devices array in root or nested property
            JsonElement devicesArray;
            if (doc.RootElement.TryGetProperty(arrayProp, out devicesArray) && devicesArray.ValueKind == JsonValueKind.Array)
            {
                // Found under arrayProp
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                devicesArray = doc.RootElement;
            }
            else
            {
                return devices;
            }

            foreach (var item in devicesArray.EnumerateArray())
            {
                var id = "";
                var name = "";

                if (item.TryGetProperty(idField, out var idEl))
                {
                    id = idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32().ToString()
                        : idEl.GetString() ?? "";
                }

                if (item.TryGetProperty(nameField, out var nameEl))
                {
                    name = nameEl.GetString() ?? "";
                }

                if (!string.IsNullOrEmpty(id))
                {
                    devices.Add(new DeviceInfo { Id = id, Name = name });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error parsing device list: {ex.Message}");
        }

        return devices;
    }

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        return Windows.UI.Color.FromArgb(255, 204, 204, 204); // default gray
    }

    #endregion
}
