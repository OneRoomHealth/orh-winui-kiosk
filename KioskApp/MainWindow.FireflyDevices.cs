using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Modules.Firefly;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Medical Devices (Firefly otoscope) debug tab.
/// </summary>
public sealed partial class MainWindow
{
    #region Fields

    private bool _fireflyPanelVisible = false;

    // Keyed by logical device ID (e.g. "firefly-0").
    // Stores the open MediaCapture and the MediaPlayerElement it is rendering to
    // so StopAllFireflyPreviewsAsync can cleanly stop the preview and
    // clear the element before disposing the capture.
    private readonly Dictionary<string, (MediaCapture Capture, MediaPlayerElement Preview)> _previewCaptures = new();

    // Named handler delegates stored here so they can be explicitly unsubscribed
    // before FireflyDeviceListPanel.Children.Clear() discards the card elements.
    // Anonymous lambdas cannot be unsubscribed; WinUI 3's COM event system would
    // otherwise root the old closures (and all captured UI elements) indefinitely.
    private readonly List<(Button Preview, RoutedEventHandler PreviewHandler,
                            Button Capture, RoutedEventHandler CaptureHandler)> _cardButtonHandlers = new();

    #endregion

    #region Helpers

    // Writes to the legacy file log with the [FIREFLY] prefix so that
    // UnifiedLogger.OnKioskAppLog routes the entry to the Firefly module
    // filter in the debug log viewer.
    private static void LogFirefly(string message) =>
        Logger.Log($"[FIREFLY] {message}");

    #endregion

    #region Refresh

    private async Task RefreshFireflyDevicesAsync()
    {
        try
        {
            var fireflyModule = App.Services?.GetService(typeof(FireflyModule)) as FireflyModule;

            if (fireflyModule == null)
            {
                await StopAllFireflyPreviewsAsync();
                DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable("Firefly module is not registered. Enable Hardware API mode first."));
                return;
            }

            if (!fireflyModule.IsEnabled)
            {
                await StopAllFireflyPreviewsAsync();
                DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable("Firefly module is disabled. Set hardware.firefly.enabled = true in config.json."));
                return;
            }

            // Stop any active previews before rebuilding the card list, since the
            // old card elements will be discarded and new ones created.
            await StopAllFireflyPreviewsAsync();

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
            await StopAllFireflyPreviewsAsync();
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

            // ── Interactive controls (only when we have a device path) ──────────

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

            // Live preview element (hidden until Preview is started).
            // WinUI 3 does not have CaptureElement; use MediaPlayerElement +
            // MediaPlayer backed by MediaSource.CreateFromMediaFrameSource instead.
            var previewElement = new MediaPlayerElement
            {
                Visibility = Visibility.Collapsed,
                Width = 320,
                Height = 240,
                Margin = new Thickness(0, 6, 0, 0),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                AreTransportControlsEnabled = false
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
            var deviceInterfaceId = status.DeviceInterfaceId;

            // Named delegates so they can be unsubscribed when the panel is cleared.
            RoutedEventHandler previewHandler = async (_, _) =>
                await ToggleFireflyPreviewAsync(deviceId, deviceInterfaceId,
                    previewButton, captureButton, previewElement, actionStatus);
            RoutedEventHandler captureHandler = async (_, _) =>
                await CaptureFireflyAsync(deviceId, captureImage, actionStatus);

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
            content.Children.Add(previewElement);
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

    #region Preview

    private async Task ToggleFireflyPreviewAsync(
        string deviceId,
        string deviceInterfaceId,
        Button previewButton,
        Button captureButton,
        MediaPlayerElement previewElement,
        TextBlock actionStatus)
    {
        if (_previewCaptures.ContainsKey(deviceId))
        {
            await StopFireflyPreviewAsync(deviceId, previewButton, captureButton, actionStatus);
        }
        else
        {
            await StartFireflyPreviewAsync(deviceId, deviceInterfaceId,
                previewButton, captureButton, previewElement, actionStatus);
        }
    }

    private async Task StartFireflyPreviewAsync(
        string deviceId,
        string deviceInterfaceId,
        Button previewButton,
        Button captureButton,
        MediaPlayerElement previewElement,
        TextBlock actionStatus)
    {
        previewButton.IsEnabled = false;
        captureButton.IsEnabled = false;
        LogFirefly($"Starting preview for {deviceId}");
        LogFirefly($"DEBUG: device path: {deviceInterfaceId}");

        // Declared outside try so the finally can conditionally dispose it.
        // Once ownership is transferred to _previewCaptures the local is nulled,
        // preventing the finally from disposing a live session.
        MediaCapture? capture = null;
        try
        {
            capture = new MediaCapture();
            LogFirefly($"DEBUG: {deviceId}: calling InitializeAsync (ExclusiveControl, Video)");
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceInterfaceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            });

            LogFirefly($"{deviceId}: MediaCapture initialized — {capture.FrameSources.Count} frame source(s)");

            // Log all available frame sources so we know exactly what the camera exposes.
            foreach (var fs in capture.FrameSources.Values)
            {
                LogFirefly($"DEBUG: {deviceId}: source type={fs.Info.MediaStreamType} " +
                           $"subtype={fs.CurrentFormat?.Subtype} " +
                           $"{fs.CurrentFormat?.VideoFormat?.Width}x{fs.CurrentFormat?.VideoFormat?.Height} " +
                           $"@{fs.CurrentFormat?.FrameRate?.Numerator}/{fs.CurrentFormat?.FrameRate?.Denominator}fps");
            }

            // Start the internal MF preview pipeline (required for CapturePhotoToStreamAsync)
            // then bind a MediaPlayer to a frame source for visual rendering via
            // MediaPlayerElement — WinUI 3 does not have CaptureElement.
            LogFirefly($"DEBUG: {deviceId}: calling StartPreviewAsync");
            await capture.StartPreviewAsync();

            var frameSource = capture.FrameSources.Values.FirstOrDefault();
            if (frameSource != null)
            {
                LogFirefly($"DEBUG: {deviceId}: binding MediaPlayerElement via FrameSource");
                var mediaSource = MediaSource.CreateFromMediaFrameSource(frameSource);
                var player = new MediaPlayer { Source = mediaSource };
                previewElement.SetMediaPlayer(player);
                player.Play();
            }
            else
            {
                LogFirefly($"DEBUG: {deviceId}: no FrameSources — preview running but no visual render");
            }
            previewElement.Visibility = Visibility.Visible;

            LogFirefly($"{deviceId}: preview started");

            _previewCaptures[deviceId] = (capture, previewElement);
            capture = null; // ownership transferred — don't dispose in finally

            previewButton.Content = "Stop Preview";
            previewButton.IsEnabled = true;
            captureButton.IsEnabled = true;
            SetFireflyActionStatus(actionStatus, "Preview active — Capture will use this session.", isError: false);
            LogFirefly($"{deviceId}: preview live ({_previewCaptures.Count} total active)");
        }
        catch (Exception ex)
        {
            var detail = FormatFireflyException(ex);
            var failedPlayer = previewElement.MediaPlayer;
            previewElement.SetMediaPlayer(null);
            failedPlayer?.Dispose();
            previewElement.Visibility = Visibility.Collapsed;
            previewButton.IsEnabled = true;
            captureButton.IsEnabled = true;
            SetFireflyActionStatus(actionStatus, $"Preview failed: {ex.Message}", isError: true);
            LogFirefly($"{deviceId}: preview start failed — {detail}");
        }
        finally
        {
            // Stop and dispose capture on any failure path. No-op on success
            // because ownership was transferred above (capture = null).
            if (capture != null)
            {
                LogFirefly($"DEBUG: {deviceId}: cleanup after failed start");
                try { await capture.StopPreviewAsync(); } catch { }
                capture.Dispose();
            }
        }
    }

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

    private async Task StopFireflyPreviewAsync(
        string deviceId,
        Button previewButton,
        Button captureButton,
        TextBlock actionStatus)
    {
        if (!_previewCaptures.TryGetValue(deviceId, out var session))
            return;

        LogFirefly($"{deviceId}: stopping preview");
        _previewCaptures.Remove(deviceId);

        var player = session.Preview.MediaPlayer;
        session.Preview.SetMediaPlayer(null);
        player?.Dispose();
        try { await session.Capture.StopPreviewAsync(); } catch { /* already stopped */ }
        session.Preview.Visibility = Visibility.Collapsed;
        session.Capture.Dispose();

        previewButton.Content = "Preview";
        previewButton.IsEnabled = true;
        captureButton.IsEnabled = true;
        SetFireflyActionStatus(actionStatus, "Preview stopped.", isError: false);
        LogFirefly($"{deviceId}: preview stopped ({_previewCaptures.Count} remaining)");
    }

    private async Task StopAllFireflyPreviewsAsync()
    {
        // Snapshot the keys before iterating because the loop mutates _previewCaptures.
        var ids = _previewCaptures.Keys.ToList();
        if (ids.Count > 0)
            LogFirefly($"StopAll — stopping {ids.Count} active preview(s): {string.Join(", ", ids)}");

        foreach (var id in ids)
        {
            if (_previewCaptures.TryGetValue(id, out var session))
            {
                _previewCaptures.Remove(id);

                var player = session.Preview.MediaPlayer;
                session.Preview.SetMediaPlayer(null);
                player?.Dispose();
                try { await session.Capture.StopPreviewAsync(); } catch { }
                session.Preview.Visibility = Visibility.Collapsed;
                session.Capture.Dispose();

                LogFirefly($"DEBUG: {id}: stopped and disposed");
            }
        }
    }

    #endregion

    #region Capture

    private async Task CaptureFireflyAsync(
        string deviceId,
        Image captureImage,
        TextBlock actionStatus)
    {
        SetFireflyActionStatus(actionStatus, "Capturing\u2026", isError: false);

        try
        {
            byte[] imageBytes;

            if (_previewCaptures.TryGetValue(deviceId, out var session))
            {
                // Capture a JPEG snapshot via the MF photo sink.
                //
                // CapturePhotoToStreamAsync works here because StartPreviewAsync has
                // already started the video stream; the MF graph is running and the
                // photo sink can share that pipeline.  JPEG encoding is handled
                // natively by the MF graph — no SoftwareBitmap or BitmapEncoder needed.
                LogFirefly($"{deviceId}: capturing via CapturePhotoToStreamAsync (preview active)");

                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await session.Capture.CapturePhotoToStreamAsync(
                    ImageEncodingProperties.CreateJpeg(), stream);

                LogFirefly($"DEBUG: {deviceId}: photo stream size = {stream.Size} bytes");

                if (stream.Size == 0)
                {
                    SetFireflyActionStatus(actionStatus, "Capture produced an empty stream.", isError: true);
                    LogFirefly($"{deviceId}: capture aborted — CapturePhotoToStreamAsync produced 0 bytes");
                    return;
                }

                stream.Seek(0);
                var bytes = new byte[stream.Size];
                using var reader = new Windows.Storage.Streams.DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);

                LogFirefly($"{deviceId}: captured {bytes.Length} bytes JPEG");
                imageBytes = bytes;
            }
            else
            {
                // No active preview — delegate to FireflyModule which opens a new
                // ExclusiveControl session, starts preview, captures, then closes.
                LogFirefly($"{deviceId}: no active preview — delegating to FireflyModule");
                var fireflyModule = App.Services?.GetService(typeof(FireflyModule)) as FireflyModule;
                if (fireflyModule == null)
                {
                    SetFireflyActionStatus(actionStatus, "Firefly module unavailable.", isError: true);
                    LogFirefly($"{deviceId}: capture aborted — FireflyModule not available");
                    return;
                }
                imageBytes = await fireflyModule.TriggerCaptureAsync(deviceId);
                LogFirefly($"DEBUG: {deviceId}: FireflyModule returned {imageBytes.Length} bytes");
            }

            // Decode JPEG and display thumbnail.
            // Both the MemoryStream and the IRandomAccessStream wrapper are scoped
            // to the block so they are disposed as soon as SetSourceAsync returns,
            // not at the end of the wider try block.
            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(imageBytes))
            using (var ras = ms.AsRandomAccessStream())
            {
                await bitmap.SetSourceAsync(ras);
            }
            captureImage.Source = bitmap;
            captureImage.Visibility = Visibility.Visible;

            SetFireflyActionStatus(actionStatus, $"Captured {imageBytes.Length / 1024:N0} KB", isError: false);
            LogFirefly($"{deviceId}: capture OK — {imageBytes.Length / 1024} KB ({imageBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            var detail = FormatFireflyException(ex);
            SetFireflyActionStatus(actionStatus, $"Capture failed: {ex.Message}", isError: true);
            LogFirefly($"{deviceId}: capture failed — {detail}");
        }
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
