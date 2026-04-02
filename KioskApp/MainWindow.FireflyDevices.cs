using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Modules.Firefly;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Medical Devices (Firefly otoscope) debug tab.
/// </summary>
public sealed partial class MainWindow
{
    #region Fields

    private bool _fireflyPanelVisible = false;

    // Keyed by logical device ID (e.g. "firefly-0").
    // Stores the open MediaCapture, its active MediaFrameReader, and the Image element
    // the reader is feeding — so StopAllFireflyPreviewsAsync can cleanly stop the
    // reader and clear the element before disposing the capture.
    private readonly Dictionary<string, (MediaCapture Capture, MediaFrameReader Reader, Image PreviewImage)> _previewCaptures = new();

    // Named handler delegates stored here so they can be explicitly unsubscribed
    // before FireflyDeviceListPanel.Children.Clear() discards the card elements.
    // Anonymous lambdas cannot be unsubscribed; WinUI 3's COM event system would
    // otherwise root the old closures (and all captured UI elements) indefinitely.
    private readonly List<(Button Preview, RoutedEventHandler PreviewHandler,
                            Button Capture, RoutedEventHandler CaptureHandler)> _cardButtonHandlers = new();

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

            Logger.Log($"[MEDICAL DEVICES] Refreshed — {devices.Count} Firefly device(s)");
        }
        catch (Exception ex)
        {
            Logger.Log($"[MEDICAL DEVICES] Error refreshing Firefly devices: {ex.Message}");
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

            // Live preview image (hidden until Preview is started).
            // Backed by a SoftwareBitmapSource updated by a MediaFrameReader.
            var previewImage = new Image
            {
                Visibility = Visibility.Collapsed,
                MinHeight = 180,
                Margin = new Thickness(0, 6, 0, 0),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
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
                    previewButton, captureButton, previewImage, actionStatus);
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
            content.Children.Add(previewImage);
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
        Image previewImage,
        TextBlock actionStatus)
    {
        if (_previewCaptures.ContainsKey(deviceId))
        {
            await StopFireflyPreviewAsync(deviceId, previewButton, captureButton, actionStatus);
        }
        else
        {
            await StartFireflyPreviewAsync(deviceId, deviceInterfaceId,
                previewButton, captureButton, previewImage, actionStatus);
        }
    }

    private async Task StartFireflyPreviewAsync(
        string deviceId,
        string deviceInterfaceId,
        Button previewButton,
        Button captureButton,
        Image previewImage,
        TextBlock actionStatus)
    {
        previewButton.IsEnabled = false;
        captureButton.IsEnabled = false;
        Logger.Log($"[MEDICAL DEVICES] Starting preview for {deviceId} (path: {deviceInterfaceId})");

        // Declared outside try so the finally can conditionally dispose them.
        // Once ownership is transferred to _previewCaptures both locals are nulled,
        // preventing the finally from disposing a live session.
        MediaCapture? capture = null;
        MediaFrameReader? frameReader = null;
        try
        {
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceInterfaceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            });

            Logger.Log($"[MEDICAL DEVICES] MediaCapture initialized for {deviceId} — {capture.FrameSources.Count} frame source(s)");

            // Prefer VideoPreview, fall back to VideoRecord, then any available source.
            var frameSource = capture.FrameSources.Values
                .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview)
                ?? capture.FrameSources.Values
                    .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoRecord)
                ?? capture.FrameSources.Values.FirstOrDefault();

            if (frameSource == null)
            {
                previewButton.IsEnabled = true;
                captureButton.IsEnabled = true;
                Logger.Log($"[MEDICAL DEVICES] No frame source found for {deviceId}");
                SetFireflyActionStatus(actionStatus, "No video frame source found on this device.", isError: true);
                return; // finally disposes capture
            }

            // Log native format subtype and all supported formats for diagnostics.
            var currentSubtype = frameSource.CurrentFormat?.Subtype ?? "unknown";
            Logger.Log($"[MEDICAL DEVICES] Frame source for {deviceId}: type={frameSource.Info.MediaStreamType}, " +
                       $"format={frameSource.CurrentFormat?.VideoFormat?.Width}x{frameSource.CurrentFormat?.VideoFormat?.Height}, " +
                       $"subtype={currentSubtype}");
            var supportedFormats = frameSource.SupportedFormats.ToList();
            Logger.Log($"[MEDICAL DEVICES] {deviceId}: {supportedFormats.Count} supported format(s)");
            foreach (var fmt in supportedFormats.Take(8))
                Logger.Log($"[MEDICAL DEVICES]   subtype={fmt.Subtype} " +
                           $"{fmt.VideoFormat?.Width}x{fmt.VideoFormat?.Height} " +
                           $"@{fmt.FrameRate?.Numerator}/{fmt.FrameRate?.Denominator}fps");

            // Switch to YUY2 or NV12 before creating the reader so that MediaFrameReader
            // delivers non-null SoftwareBitmap frames.
            //
            // Passing Bgra8 to CreateFrameReaderAsync requests MF to transcode on the fly,
            // but on these cameras the transcoding pipeline fails silently: StartAsync
            // returns Success yet FrameArrived never fires.  Explicitly selecting an
            // uncompressed FOURCC via SetFormatAsync is the reliable alternative.
            var uncompressedFormat =
                supportedFormats.FirstOrDefault(f => string.Equals(f.Subtype, MediaEncodingSubtypes.Yuy2, StringComparison.OrdinalIgnoreCase)) ??
                supportedFormats.FirstOrDefault(f => string.Equals(f.Subtype, MediaEncodingSubtypes.Nv12, StringComparison.OrdinalIgnoreCase));

            if (uncompressedFormat != null &&
                !string.Equals(currentSubtype, uncompressedFormat.Subtype, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await frameSource.SetFormatAsync(uncompressedFormat);
                    Logger.Log($"[MEDICAL DEVICES] {deviceId}: requested {uncompressedFormat.Subtype} — " +
                               $"actual format now: {frameSource.CurrentFormat?.Subtype} " +
                               $"{frameSource.CurrentFormat?.VideoFormat?.Width}x{frameSource.CurrentFormat?.VideoFormat?.Height}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[MEDICAL DEVICES] {deviceId}: format switch failed ({ex.Message}) — " +
                               $"keeping native {currentSubtype}");
                }
            }
            else if (uncompressedFormat == null)
            {
                Logger.Log($"[MEDICAL DEVICES] {deviceId}: no YUY2/NV12 format available — " +
                           $"using native {currentSubtype}");
            }
            else
            {
                // uncompressedFormat != null but camera is already in that format — no switch needed.
                Logger.Log($"[MEDICAL DEVICES] {deviceId}: already in {currentSubtype} — skipping SetFormatAsync");
            }

            // Use MediaFrameReader for continuous live preview.
            // MediaSource.CreateFromMediaFrameSource only surfaces a single static frame;
            // MediaFrameReader fires FrameArrived on every new camera frame.
            var softwareBitmapSource = new SoftwareBitmapSource();
            previewImage.Source = softwareBitmapSource;
            previewImage.Visibility = Visibility.Visible;

            // Create the reader in the camera's current (native) format.
            // No subtype override is passed: requesting transcoding via the overload that
            // takes a subtype causes StartAsync to return Success but deliver zero frames
            // on these cameras when MF cannot negotiate the conversion graph at runtime.
            frameReader = await capture.CreateFrameReaderAsync(frameSource);
            Logger.Log($"[MEDICAL DEVICES] MediaFrameReader created for {deviceId}");

            // Switch from the default Realtime to Buffered acquisition mode.
            // In Realtime mode the reader silently drops frames when the consumer
            // lags; on these cameras (native YUY2) that causes TryAcquireLatestFrame()
            // to return null on every FrameArrived call even when the pipeline is
            // running correctly.  Buffered mode queues frames so they are available
            // when TryAcquireLatestFrame() is called.  The Interlocked rate-limiting
            // flag below already caps display throughput, so buffer growth is bounded.
            frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Buffered;

            // Frame-drop flag: 0 = idle, 1 = a frame is currently queued for rendering.
            // int + Interlocked required because FrameArrived runs on the MediaFrameReader
            // background thread while the reset runs on the UI thread; a plain bool has
            // no visibility or ordering guarantees across threads in the C# memory model.
            int frameUpdatePending  = 0;
            int frameArrivalCount   = 0; // diagnostic: log first arrival and any null bitmaps
            int frameHandlerCalled  = 0; // diagnostic: incremented at handler entry, before any null check
            // async void is required here to support the Direct3DSurface path, which needs
            // to await SoftwareBitmap.CreateCopyFromSurfaceAsync for GPU-backed frames.
            // The outer try/catch ensures no unhandled exception escapes to the app's
            // unhandled-exception handler.  The Interlocked drop flag at the top prevents
            // concurrent invocations from piling up.
            frameReader.FrameArrived += async (sender, _) =>
            {
                // Log the first few calls unconditionally so we can confirm the event is
                // firing even if TryAcquireLatestFrame() consistently returns null.
                var callCount = Interlocked.Increment(ref frameHandlerCalled);
                if (callCount <= 3)
                    Logger.Log($"[MEDICAL DEVICES] FrameArrived #{callCount} for {deviceId}");

                // Atomically claim the slot; if it was already taken, drop this frame.
                if (Interlocked.CompareExchange(ref frameUpdatePending, 1, 0) != 0) return;

                SoftwareBitmap? displayBitmap = null;
                int arrival = 0;
                try
                {
                    using var frame = sender.TryAcquireLatestFrame();
                    if (frame == null)
                    {
                        Interlocked.Exchange(ref frameUpdatePending, 0);
                        return;
                    }

                    var videoFrame = frame.VideoMediaFrame;
                    var bitmap     = videoFrame?.SoftwareBitmap;
                    var surface    = videoFrame?.Direct3DSurface;

                    arrival = Interlocked.Increment(ref frameArrivalCount);
                    if (arrival == 1)
                        Logger.Log($"[MEDICAL DEVICES] First frame for {deviceId}: " +
                                   $"bitmap={bitmap != null}, format={bitmap?.BitmapPixelFormat}, " +
                                   $"alpha={bitmap?.BitmapAlphaMode}, surface={surface != null}, " +
                                   $"bufferFrame={frame.BufferMediaFrame != null}");

                    if (bitmap != null)
                    {
                        // CPU-backed frame (YUY2, NV12, Bgra8, etc.) — convert to Bgra8/Premultiplied.
                        displayBitmap = (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
                                         bitmap.BitmapAlphaMode  == BitmapAlphaMode.Premultiplied)
                            ? SoftwareBitmap.Copy(bitmap)
                            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    }
                    else if (surface != null)
                    {
                        // GPU-backed frame (Direct3D surface) — copy to CPU memory so
                        // SoftwareBitmapSource can render it.  This path is why the handler
                        // is async void: CreateCopyFromSurfaceAsync must be awaited.
                        displayBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                            surface, BitmapAlphaMode.Premultiplied);
                        // Explicit null guard before format check: if CreateCopyFromSurfaceAsync
                        // returns null, displayBitmap?.BitmapPixelFormat evaluates to null,
                        // which compares unequal to Bgra8 — causing the block to enter and
                        // dereference a null with !, throwing NullReferenceException.
                        if (displayBitmap != null && displayBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                        {
                            var converted = SoftwareBitmap.Convert(
                                displayBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                            displayBitmap.Dispose();
                            displayBitmap = converted;
                        }
                    }
                    else if (arrival <= 3)
                    {
                        Logger.Log($"[MEDICAL DEVICES] No bitmap or surface in frame #{arrival} for {deviceId} " +
                                   $"(bufferFrame={frame.BufferMediaFrame != null})");
                    }
                }
                catch (Exception ex)
                {
                    if (arrival <= 3)
                        Logger.Log($"[MEDICAL DEVICES] Frame processing failed for {deviceId}: {ex.Message}");
                    displayBitmap?.Dispose();
                    displayBitmap = null;
                }

                if (displayBitmap == null)
                {
                    Interlocked.Exchange(ref frameUpdatePending, 0);
                    return;
                }

                // If TryEnqueue fails (dispatcher shut down) the callback never runs,
                // so we must release resources and reset the flag here instead.
                if (!DispatcherQueue.TryEnqueue(async () =>
                {
                    try   { await softwareBitmapSource.SetBitmapAsync(displayBitmap); }
                    catch { /* preview may have been stopped */ }
                    finally
                    {
                        displayBitmap.Dispose();
                        Interlocked.Exchange(ref frameUpdatePending, 0);
                    }
                }))
                {
                    displayBitmap.Dispose();
                    Interlocked.Exchange(ref frameUpdatePending, 0);
                }
            };

            var startStatus = await frameReader.StartAsync();
            Logger.Log($"[MEDICAL DEVICES] Frame reader started for {deviceId}: {startStatus}");

            if (startStatus != MediaFrameReaderStartStatus.Success)
            {
                previewImage.Source = null;
                previewImage.Visibility = Visibility.Collapsed;
                previewButton.IsEnabled = true;
                captureButton.IsEnabled = true;
                SetFireflyActionStatus(actionStatus, $"Preview stream failed to start: {startStatus}", isError: true);
                return; // finally stops and disposes frameReader + capture
            }

            _previewCaptures[deviceId] = (capture, frameReader, previewImage);
            capture     = null; // ownership transferred — don't dispose in finally
            frameReader = null; // ownership transferred — don't dispose in finally

            previewButton.Content = "Stop Preview";
            previewButton.IsEnabled = true;
            captureButton.IsEnabled = true;
            SetFireflyActionStatus(actionStatus, "Preview active — Capture will use this session.", isError: false);
            Logger.Log($"[MEDICAL DEVICES] Preview live for {deviceId}");
        }
        catch (Exception ex)
        {
            var detail = FormatFireflyException(ex);
            previewButton.IsEnabled = true;
            captureButton.IsEnabled = true;
            SetFireflyActionStatus(actionStatus, $"Preview failed: {ex.Message}", isError: true);
            Logger.Log($"[MEDICAL DEVICES] Preview start failed for {deviceId}: {detail}");
        }
        finally
        {
            // Stop and dispose reader on any failure path. No-op on success
            // because ownership was transferred above (frameReader = null).
            if (frameReader != null)
            {
                try { await frameReader.StopAsync(); } catch { }
                frameReader.Dispose();
            }
            // Dispose capture on any failure path (no-op on success: capture = null).
            capture?.Dispose();
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

        _previewCaptures.Remove(deviceId);

        // Detach image source before stopping the reader so the UI element is not
        // left referencing a pipeline that is being torn down.
        session.PreviewImage.Source = null;
        session.PreviewImage.Visibility = Visibility.Collapsed;

        try { await session.Reader.StopAsync(); } catch { /* already stopped */ }
        session.Reader.Dispose();
        session.Capture.Dispose();

        previewButton.Content = "Preview";
        previewButton.IsEnabled = true;
        captureButton.IsEnabled = true;
        SetFireflyActionStatus(actionStatus, "Preview stopped.", isError: false);
        Logger.Log($"[MEDICAL DEVICES] Preview stopped for {deviceId}");
    }

    private async Task StopAllFireflyPreviewsAsync()
    {
        // Snapshot the keys before iterating because the loop mutates _previewCaptures.
        var ids = _previewCaptures.Keys.ToList();
        foreach (var id in ids)
        {
            if (_previewCaptures.TryGetValue(id, out var session))
            {
                _previewCaptures.Remove(id);

                // Detach image source before stopping the reader — same ordering as
                // StopFireflyPreviewAsync — so the pipeline is torn down cleanly.
                session.PreviewImage.Source = null;
                session.PreviewImage.Visibility = Visibility.Collapsed;

                try { await session.Reader.StopAsync(); } catch { }
                session.Reader.Dispose();
                session.Capture.Dispose();
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
                // Capture a JPEG snapshot from the active MediaFrameReader.
                //
                // CapturePhotoToStreamAsync is intentionally NOT used here.  That API
                // requires the camera to expose a hardware photo-capture sink (MFT).
                // Firefly cameras only expose a single VideoRecord stream; they have no
                // photo pipeline, so CapturePhotoToStreamAsync fails with 0xC00D3704
                // (MF_E_HARDWARE_MFT_FAILED_START_STREAMING) every time.
                //
                // Instead we grab the latest buffered frame from the already-running
                // frame reader (the same stream powering the live preview) and encode
                // it to JPEG in software using BitmapEncoder.  We copy the SoftwareBitmap
                // out of the MediaFrameReference immediately so we can release the
                // reference before the async encoding step.
                Logger.Log($"[MEDICAL DEVICES] Capturing snapshot from frame reader for {deviceId}");

                SoftwareBitmap? frameBitmap;
                using (var latestFrame = session.Reader.TryAcquireLatestFrame())
                {
                    var videoFrame = latestFrame?.VideoMediaFrame;
                    var raw        = videoFrame?.SoftwareBitmap;
                    if (raw != null)
                    {
                        frameBitmap = SoftwareBitmap.Copy(raw);
                    }
                    else if (videoFrame?.Direct3DSurface is { } surface)
                    {
                        // GPU-backed frame — copy to CPU memory while the frame is still
                        // locked (latestFrame in scope) so MF cannot reclaim the D3D texture.
                        frameBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                            surface, BitmapAlphaMode.Premultiplied);
                        Logger.Log($"[MEDICAL DEVICES] Capture snapshot from Direct3D surface for {deviceId}");
                    }
                    else
                    {
                        frameBitmap = null;
                    }
                }

                if (frameBitmap == null)
                {
                    SetFireflyActionStatus(actionStatus,
                        "No frame available — wait for preview to produce frames, then try again.",
                        isError: true);
                    Logger.Log($"[MEDICAL DEVICES] Capture aborted for {deviceId}: no buffered frame available");
                    return;
                }

                // try/finally makes frameBitmap's lifetime explicit: the finally block
                // is the sole disposal point from here on, covering every exit path —
                // normal completion, SoftwareBitmap.Convert throwing, FlushAsync throwing,
                // or any other exception reaching the outer catch.
                try
                {
                    Logger.Log($"[MEDICAL DEVICES] Encoding frame for {deviceId}: " +
                               $"{frameBitmap.PixelWidth}x{frameBitmap.PixelHeight} {frameBitmap.BitmapPixelFormat}");

                    // BitmapEncoder (JPEG) requires a standard pixel format.
                    // Convert from YUY2, NV12, or any other camera-native format to Bgra8 first.
                    // If Convert throws, frameBitmap still points to the original and the
                    // finally block below will dispose it — no leak on the exception path.
                    if (frameBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                    {
                        var converted = SoftwareBitmap.Convert(
                            frameBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        frameBitmap.Dispose(); // original disposed; reassign before any throw
                        frameBitmap = converted;
                        Logger.Log($"[MEDICAL DEVICES] Converted frame to Bgra8 for encoding ({deviceId})");
                    }

                    using var encodeStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encodeStream);
                    encoder.SetSoftwareBitmap(frameBitmap);
                    await encoder.FlushAsync();
                    // No explicit Dispose here — finally owns it.

                    encodeStream.Seek(0);
                    var bytes = new byte[encodeStream.Size];
                    using var dataReader = new Windows.Storage.Streams.DataReader(encodeStream);
                    await dataReader.LoadAsync((uint)encodeStream.Size);
                    dataReader.ReadBytes(bytes);
                    imageBytes = bytes;
                }
                finally
                {
                    frameBitmap?.Dispose();
                }
            }
            else
            {
                // No active preview — delegate to FireflyModule which opens a new
                // ExclusiveControl session, starts preview, captures, then closes.
                Logger.Log($"[MEDICAL DEVICES] No active preview for {deviceId} — using FireflyModule capture");
                var fireflyModule = App.Services?.GetService(typeof(FireflyModule)) as FireflyModule;
                if (fireflyModule == null)
                {
                    SetFireflyActionStatus(actionStatus, "Firefly module unavailable.", isError: true);
                    return;
                }
                imageBytes = await fireflyModule.TriggerCaptureAsync(deviceId);
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
            Logger.Log($"[MEDICAL DEVICES] Capture OK — {deviceId} — {imageBytes.Length / 1024} KB");
        }
        catch (Exception ex)
        {
            var detail = FormatFireflyException(ex);
            SetFireflyActionStatus(actionStatus, $"Capture failed: {ex.Message}", isError: true);
            Logger.Log($"[MEDICAL DEVICES] Capture failed for {deviceId}: {detail}");
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
