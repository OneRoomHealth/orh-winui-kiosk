using System.IO;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Modules.Firefly;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Medical Devices (Firefly otoscope) debug tab.
/// </summary>
public sealed partial class MainWindow
{
    #region Fields

    private bool _fireflyPanelVisible = false;

    // Named handler delegates stored here so they can be explicitly unsubscribed
    // before FireflyDeviceListPanel.Children.Clear() discards the card elements.
    // Anonymous lambdas cannot be unsubscribed; WinUI 3's COM event system would
    // otherwise root the old closures (and all captured UI elements) indefinitely.
    private readonly List<(Button Preview, RoutedEventHandler PreviewHandler,
                            Button Capture, RoutedEventHandler CaptureHandler)> _cardButtonHandlers = new();

    // Hardware snap-button subscription — kept so we can unsubscribe if the module
    // instance changes (e.g. after a hardware API mode restart).
    private FireflyModule? _subscribedFireflyModule;
    private EventHandler<string>? _fireflySnapHandler;

    #endregion

    #region Helpers

    // Writes to the legacy file log with the [FIREFLY] prefix so that
    // UnifiedLogger.OnKioskAppLog routes the entry to the Firefly module
    // filter in the debug log viewer.
    private static void LogFirefly(string message) =>
        Logger.Log($"[FIREFLY] {message}");

    /// <summary>
    /// Formats an exception for logging, including HRESULT for COM exceptions
    /// (which frequently have empty or unhelpful Message strings).
    /// </summary>
    private static string FormatFireflyException(Exception ex)
    {
        var hresult = ex is System.Runtime.InteropServices.COMException com
            ? $" (HRESULT: 0x{com.HResult:X8})"
            : string.Empty;
        return $"[{ex.GetType().Name}] {ex.Message}{hresult}";
    }

    #endregion

    #region Refresh

    private async Task RefreshFireflyDevicesAsync()
    {
        try
        {
            var fireflyModule = App.Services?.GetService(typeof(FireflyModule)) as FireflyModule;

            if (fireflyModule == null)
            {
                // No awaits have run yet — we are still on the UI thread.
                // Call RenderFireflyUnavailable directly so handler cleanup is
                // synchronous with the return; a deferred TryEnqueue would leave
                // _cardButtonHandlers non-empty until the UI thread drains its queue,
                // allowing a rapid re-entry to race against stale handlers.
                RenderFireflyUnavailable("Firefly module is not registered. Enable Hardware API mode first.");
                return;
            }

            if (!fireflyModule.IsEnabled)
            {
                RenderFireflyUnavailable("Firefly module is disabled. Set hardware.firefly.enabled = true in config.json.");
                return;
            }

            // Wire the hardware snap button to the JS-side capture path.
            // Idempotent — skips re-subscription if the module instance hasn't changed.
            SubscribeFireflySnapButton(fireflyModule);

            await fireflyModule.RefreshDevicesAsync();
            var devices = await fireflyModule.GetDevicesAsync();

            // Fetch detailed status for each device
            var statuses = new List<FireflyDeviceStatus?>();
            foreach (var device in devices)
            {
                var statusObj = await fireflyModule.GetDeviceStatusAsync(device.Id);
                statuses.Add(statusObj as FireflyDeviceStatus);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                // Unsubscribe all card button handlers before discarding the elements.
                foreach (var (preview, previewH, capture, captureH) in _cardButtonHandlers)
                {
                    preview.Click -= previewH;
                    capture.Click -= captureH;
                }
                _cardButtonHandlers.Clear();

                FireflyDeviceListPanel.Children.Clear();

                if (devices.Count == 0)
                {
                    RenderFireflyEmpty();
                }
                else
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var device = devices[i];
                        var status = statuses[i];
                        FireflyDeviceListPanel.Children.Add(BuildFireflyDeviceCard(device, status));
                    }
                }

                FireflyLastRefreshText.Text = $"Refreshed: {DateTime.Now:HH:mm:ss}";
            });

            LogFirefly($"Refreshed — {devices.Count} Firefly device(s)");
            if (devices.Count > 0)
                LogFirefly($"DEBUG: devices: {string.Join(", ", devices.Select(d => $"{d.Id} ({d.Health})"))}");
        }
        catch (Exception ex)
        {
            LogFirefly($"Error refreshing Firefly devices: {ex.Message}");
            DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable($"Error: {ex.Message}"));
        }
    }

    private async void RefreshFireflyButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshFireflyDevicesAsync();
    }

    #endregion

    #region Rendering

    private void RenderFireflyUnavailable(string message)
    {
        // Unsubscribe card button handlers before clearing the panel so that old
        // closures (and the UI elements they capture) are released immediately.
        // The success path handles this inline in its own DispatcherQueue block;
        // this covers all error/early-return callers that route through here.
        foreach (var (preview, previewH, capture, captureH) in _cardButtonHandlers)
        {
            preview.Click -= previewH;
            capture.Click -= captureH;
        }
        _cardButtonHandlers.Clear();

        FireflyDeviceListPanel.Children.Clear();
        FireflyDeviceListPanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 12,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
        FireflyLastRefreshText.Text = $"Refreshed: {DateTime.Now:HH:mm:ss}";
    }

    private void RenderFireflyEmpty()
    {
        FireflyDeviceListPanel.Children.Add(new TextBlock
        {
            Text = "No Firefly devices detected. Connect a Firefly UVC otoscope camera (VID 0x21CD).",
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 12,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private Border BuildFireflyDeviceCard(OneRoomHealth.Hardware.Abstractions.DeviceInfo device, FireflyDeviceStatus? status)
    {
        var healthColor = device.Health switch
        {
            DeviceHealth.Healthy   => ColorHelper.FromArgb(255, 78, 201, 176),   // teal
            DeviceHealth.Unhealthy => ColorHelper.FromArgb(255, 220, 220, 170),  // yellow
            DeviceHealth.Offline   => ColorHelper.FromArgb(255, 244, 135, 113),  // red
            _                      => ColorHelper.FromArgb(255, 133, 133, 133)   // grey
        };

        var healthLabel = device.Health switch
        {
            DeviceHealth.Healthy   => "Healthy",
            DeviceHealth.Unhealthy => "Unhealthy",
            DeviceHealth.Offline   => "Offline",
            _                      => "Unknown"
        };

        // Card border
        var card = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 37, 38)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var content = new StackPanel { Spacing = 6 };

        // Header row: health dot + device name
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = "\u25CF",
            Foreground = new SolidColorBrush(healthColor),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = status?.FriendlyName ?? device.Name,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 204, 204, 204)),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (status?.Model is { Length: > 0 } model)
        {
            headerRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 60, 60)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = model,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 156, 220, 254)),
                    FontSize = 10,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas")
                }
            });
        }
        content.Children.Add(headerRow);

        // Property rows
        AddFireflyPropertyRow(content, "Status", healthLabel, healthColor);
        AddFireflyPropertyRow(content, "Device ID", device.Id);

        if (status != null)
        {
            AddFireflyPropertyRow(content, "Connected", status.IsConnected ? "Yes" : "No");
            AddFireflyPropertyRow(content, "Captures", status.CaptureCount.ToString());

            if (status.LastCaptureAt.HasValue)
                AddFireflyPropertyRow(content, "Last Capture", status.LastCaptureAt.Value.ToLocalTime().ToString("HH:mm:ss"));

            AddFireflyPropertyRow(content, "Last Seen", status.LastSeen.ToLocalTime().ToString("HH:mm:ss"));
            AddFireflyPropertyRow(content, "Device Path", status.DeviceInterfaceId);

            if (status.Errors.Count > 0)
            {
                AddFireflyPropertyRow(content, "Errors",
                    string.Join('\n', status.Errors),
                    ColorHelper.FromArgb(255, 244, 135, 113));
            }

            // ── Interactive controls ──────────────────────────────────────────────

            // Status feedback text (hidden until an action produces output)
            var actionStatus = new TextBlock
            {
                Visibility = Visibility.Collapsed,
                FontSize = 11,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };

            // Last-captured image (hidden until a capture completes)
            var captureImage = new Image
            {
                Visibility = Visibility.Collapsed,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                MaxHeight = 240,
                Margin = new Thickness(0, 6, 0, 0)
            };

            // Button row
            var previewButton = new Button
            {
                Content = "Preview",
                IsEnabled = status.IsConnected,
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11
            };
            var captureButton = new Button
            {
                Content = "Capture",
                IsEnabled = status.IsConnected,
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11
            };

            var deviceId = device.Id;
            var deviceLabel = status.FriendlyName ?? device.Name;  // captured before lambda

            // Named delegates so they can be unsubscribed when the panel is cleared.
            //
            // Preview is info-only: the browser owns the Firefly during an ACS call;
            // native MediaCapture would conflict with the browser's exclusive access.
            RoutedEventHandler previewHandler = (_, _) =>
                SetFireflyActionStatus(actionStatus,
                    "Live preview is available in the active ACS video call. " +
                    "The Firefly camera streams through the WebView \u2014 no native preview needed.",
                    isError: false);

            RoutedEventHandler captureHandler = async (_, _) =>
                await CaptureFireflyViaWebAsync(deviceId, deviceLabel, captureImage, actionStatus);

            previewButton.Click += previewHandler;
            captureButton.Click += captureHandler;
            _cardButtonHandlers.Add((previewButton, previewHandler, captureButton, captureHandler));

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttonRow.Children.Add(previewButton);
            buttonRow.Children.Add(captureButton);

            content.Children.Add(buttonRow);
            content.Children.Add(actionStatus);
            content.Children.Add(captureImage);
        }

        card.Child = content;
        return card;
    }

    private static void AddFireflyPropertyRow(StackPanel parent, string label, string value,
        Windows.UI.Color? valueColor = null)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 11,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas")
        });

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(valueColor ?? ColorHelper.FromArgb(255, 204, 204, 204)),
            FontSize = 11,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);

        parent.Children.Add(row);
    }

    #endregion

    #region Capture

    /// <summary>
    /// Subscribes to <see cref="FireflyModule.SnapButtonPressed"/> so that pressing
    /// the physical camera button triggers the same JS-side capture flow used by the
    /// debug-panel Capture button.  Idempotent — safe to call on every panel refresh.
    /// </summary>
    private void SubscribeFireflySnapButton(FireflyModule module)
    {
        // Skip if we are already wired to this exact module instance.
        if (ReferenceEquals(_subscribedFireflyModule, module)) return;

        // Unsubscribe from the previous module instance (e.g. after a hardware API restart).
        if (_subscribedFireflyModule != null && _fireflySnapHandler != null)
        {
            _subscribedFireflyModule.SnapButtonPressed -= _fireflySnapHandler;
            _fireflySnapHandler = null;
        }

        _subscribedFireflyModule = module;

        // SnapButtonPressed fires on the SSE consumer's background thread.
        // Marshal all work to the UI thread via EnqueueAsync so WebView calls are safe.
        _fireflySnapHandler = (_, deviceId) =>
        {
            _ = DispatcherQueue.EnqueueAsync(async () =>
            {
                try
                {
                    var statusObj = await module.GetDeviceStatusAsync(deviceId);
                    var label = (statusObj as FireflyDeviceStatus)?.FriendlyName ?? deviceId;

                    LogFirefly($"{deviceId}: physical button press — triggering JS-side capture (label: \"{label}\")");
                    var imageBytes = await CaptureFireflyJpegViaWebAsync(deviceId, label);
                    if (imageBytes != null)
                        LogFirefly($"{deviceId}: physical button capture OK — {imageBytes.Length / 1024} KB");
                }
                catch (Exception ex)
                {
                    LogFirefly($"{deviceId}: physical button capture error — {ex.Message}");
                }
            });
        };

        module.SnapButtonPressed += _fireflySnapHandler;
        LogFirefly($"Subscribed to SnapButtonPressed on FireflyModule");
    }

    /// <summary>
    /// Debug-panel Capture button handler.  Runs the JS-side capture and displays the
    /// resulting thumbnail in the device card.
    /// </summary>
    private async Task CaptureFireflyViaWebAsync(
        string deviceId,
        string deviceLabel,
        Image captureImage,
        TextBlock actionStatus)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            SetFireflyActionStatus(actionStatus, "WebView not available.", isError: true);
            LogFirefly($"{deviceId}: capture aborted — WebView is null");
            return;
        }

        SetFireflyActionStatus(actionStatus, "Capturing\u2026", isError: false);

        var imageBytes = await CaptureFireflyJpegViaWebAsync(deviceId, deviceLabel);

        if (imageBytes == null)
        {
            // CaptureFireflyJpegViaWebAsync already logged the reason.
            SetFireflyActionStatus(actionStatus, "Capture failed — see log for details.", isError: true);
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(imageBytes))
            using (var ras = ms.AsRandomAccessStream())
            {
                await bitmap.SetSourceAsync(ras);
            }
            captureImage.Source = bitmap;
            captureImage.Visibility = Visibility.Visible;

            var kbSize = imageBytes.Length / 1024;
            SetFireflyActionStatus(actionStatus, $"Captured {kbSize:N0} KB", isError: false);
            LogFirefly($"{deviceId}: capture OK — {kbSize} KB ({imageBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            var detail = FormatFireflyException(ex);
            SetFireflyActionStatus(actionStatus, $"Capture failed: {ex.Message}", isError: true);
            LogFirefly($"{deviceId}: capture decode error — {detail}");
        }
    }

    /// <summary>
    /// Core JS-side capture: sends the capture script to the WebView, waits for the
    /// <c>orh.fireflyCapture.result</c> postMessage, and returns the raw JPEG bytes.
    /// Returns <c>null</c> on any failure (timeout, JS error, empty result) after logging.
    /// Called by both the debug-panel Capture button and the physical snap-button handler.
    /// </summary>
    private async Task<byte[]?> CaptureFireflyJpegViaWebAsync(string deviceId, string deviceLabel)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            LogFirefly($"{deviceId}: capture aborted — WebView is null");
            return null;
        }

        LogFirefly($"{deviceId}: requesting JS-side capture (label: \"{deviceLabel}\")");

        var requestId = Guid.NewGuid().ToString("N");
        var js = GetFireflyCaptureScript(requestId, deviceId, deviceLabel);

        // SendWebMessageRequestAsync is in MainWindow.MediaDevices.cs — accessible
        // because both files are the same partial class.
        var json = await SendWebMessageRequestAsync(js, requestId, TimeSpan.FromSeconds(15));

        if (string.IsNullOrWhiteSpace(json))
        {
            LogFirefly($"{deviceId}: capture failed — no response from JS (timeout)");
            return null;
        }

        LogFirefly($"{deviceId}: JS capture returned {json.Length} bytes");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errEl) &&
                errEl.ValueKind != JsonValueKind.Null &&
                errEl.ValueKind != JsonValueKind.Undefined)
            {
                LogFirefly($"{deviceId}: JS capture error — {errEl.GetString() ?? "unknown"}");
                return null;
            }

            if (!root.TryGetProperty("jpeg", out var jpegEl) ||
                jpegEl.ValueKind == JsonValueKind.Null ||
                string.IsNullOrEmpty(jpegEl.GetString()))
            {
                LogFirefly($"{deviceId}: JS capture returned null jpeg");
                return null;
            }

            return Convert.FromBase64String(jpegEl.GetString()!);
        }
        catch (Exception ex)
        {
            LogFirefly($"{deviceId}: capture parse error — {FormatFireflyException(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Returns a self-executing JS script that:
    ///   1. Finds the browser deviceId for the Firefly by label substring match.
    ///   2. Reuses an already-open live track (avoids reopening during an ACS call).
    ///   3. Falls back to opening a temporary getUserMedia stream.
    ///   4. Captures via ImageCapture.takePhoto() or a canvas draw fallback.
    ///   5. Posts { type: 'orh.fireflyCapture.result', requestId, deviceId, jpeg, error }
    ///      via chrome.webview.postMessage.
    /// Uses window.__orhOrigGetUserMedia (stored before the getUserMedia override) so the
    /// override's camera-selection logic does not replace the Firefly deviceId.
    /// </summary>
    private string GetFireflyCaptureScript(string requestId, string deviceId, string deviceLabel)
    {
        var requestIdJson    = JsonSerializer.Serialize(requestId);
        var deviceIdJson     = JsonSerializer.Serialize(deviceId);
        var deviceLabelJson  = JsonSerializer.Serialize(deviceLabel);

        return $@"(async () => {{
    const requestId   = {requestIdJson};
    const nativeDevId = {deviceIdJson};
    const deviceLabel = {deviceLabelJson};

    const post = (payload) => {{
        try {{
            if (window.chrome && chrome.webview && chrome.webview.postMessage)
                chrome.webview.postMessage(payload);
        }} catch (e) {{}}
    }};

    let streamToClose = null;
    try {{
        // Prefer the original (un-overridden) getUserMedia so the device-selection
        // override does not reroute the Firefly deviceId to the main camera.
        const gum = (typeof window.__orhOrigGetUserMedia === 'function')
            ? window.__orhOrigGetUserMedia
            : navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);

        // Locate the browser deviceId that matches our Firefly by label substring.
        let targetDeviceId = null;
        try {{
            const devices = await navigator.mediaDevices.enumerateDevices();
            const lbl = deviceLabel.toLowerCase();
            const match = devices.find(d =>
                d.kind === 'videoinput' && d.label &&
                d.label.toLowerCase().includes(lbl));
            if (match) targetDeviceId = match.deviceId;
        }} catch (e) {{}}

        if (!targetDeviceId) {{
            post({{ type: 'orh.fireflyCapture.result', requestId, deviceId: nativeDevId,
                    jpeg: null, error: 'Device not found in browser: ' + deviceLabel }});
            return;
        }}

        // Reuse an already-open live track (avoids reopening the camera during an ACS call).
        let track = null;
        try {{
            const streams = window.__orhLocalUserMediaStreams || [];
            for (const s of streams) {{
                if (!s || !s.active) continue;
                const vts = s.getVideoTracks ? s.getVideoTracks() : [];
                const vt  = vts.find(t => {{
                    try {{
                        return t.readyState === 'live' && t.getSettings &&
                               t.getSettings().deviceId === targetDeviceId;
                    }} catch (e) {{ return false; }}
                }});
                if (vt) {{ track = vt; break; }}
            }}
        }} catch (e) {{}}

        // No existing track — open a temporary stream and remember to close it.
        if (!track) {{
            streamToClose = await gum({{ video: {{ deviceId: {{ exact: targetDeviceId }} }} }});
            track = streamToClose.getVideoTracks()[0];
        }}

        if (!track) {{
            post({{ type: 'orh.fireflyCapture.result', requestId, deviceId: nativeDevId,
                    jpeg: null, error: 'No video track available' }});
            return;
        }}

        // Capture — prefer ImageCapture API, fall back to canvas draw.
        let blob = null;
        try {{
            if (typeof ImageCapture !== 'undefined') {{
                const ic = new ImageCapture(track);
                blob = await ic.takePhoto();
            }}
        }} catch (e) {{}}

        if (!blob) {{
            const video = document.createElement('video');
            video.srcObject = new MediaStream([track]);
            await new Promise((resolve, reject) => {{
                video.onloadedmetadata = () => resolve();
                video.onerror          = () => reject(new Error('video error'));
                setTimeout(() => reject(new Error('loadedmetadata timeout')), 5000);
            }});
            video.play();
            await new Promise(r => setTimeout(r, 250));
            const canvas  = document.createElement('canvas');
            canvas.width  = video.videoWidth  || 640;
            canvas.height = video.videoHeight || 480;
            const ctx = canvas.getContext('2d');
            if (ctx) ctx.drawImage(video, 0, 0);
            blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
        }}

        if (!blob) {{
            post({{ type: 'orh.fireflyCapture.result', requestId, deviceId: nativeDevId,
                    jpeg: null, error: 'Failed to capture image blob' }});
            return;
        }}

        // Convert blob → base64 in 8 192-byte chunks (avoids call-stack overflow for
        // large frames when using String.fromCharCode.apply).
        const ab    = await blob.arrayBuffer();
        const ua    = new Uint8Array(ab);
        let binary  = '';
        const chunk = 8192;
        for (let i = 0; i < ua.length; i += chunk) {{
            binary += String.fromCharCode.apply(null, ua.subarray(i, Math.min(i + chunk, ua.length)));
        }}
        const base64 = btoa(binary);

        post({{ type: 'orh.fireflyCapture.result', requestId, deviceId: nativeDevId,
                jpeg: base64, error: null }});
    }} catch (e) {{
        post({{ type: 'orh.fireflyCapture.result', requestId, deviceId: nativeDevId,
                jpeg: null, error: String(e) }});
    }} finally {{
        if (streamToClose) {{
            try {{ streamToClose.getTracks().forEach(t => t.stop()); }} catch (e) {{}}
        }}
    }}
}})();";
    }

    private void SetFireflyActionStatus(TextBlock block, string text, bool isError)
    {
        block.Text = text;
        block.Foreground = new SolidColorBrush(isError
            ? ColorHelper.FromArgb(255, 244, 135, 113)   // red
            : ColorHelper.FromArgb(255, 133, 133, 133));  // grey
        block.Visibility = Visibility.Visible;
    }

    #endregion
}
