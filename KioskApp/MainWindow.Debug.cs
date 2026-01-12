using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Debug mode, exit handling, and video/screensaver mode switching.
/// </summary>
public sealed partial class MainWindow
{
    #region Debug Mode

    /// <summary>
    /// Toggles between debug mode and kiosk mode.
    /// </summary>
    private async Task ToggleDebugMode()
    {
        if (_isDebugMode)
        {
            await ExitDebugMode();
        }
        else
        {
            await EnterDebugMode();
        }
    }

    /// <summary>
    /// Enters debug mode: windows the application and enables developer tools.
    /// </summary>
    private async Task EnterDebugMode()
    {
        Logger.LogSecurityEvent("EnterDebugMode", "Entering debug mode");
        ShowStatus("DEBUG MODE", "Initializing debug mode...");

        try
        {
            // In video mode, stop video and show WebView
            if (_isVideoMode && _videoController != null)
            {
                await _videoController.StopAsync();
                var tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(() =>
                {
                    KioskWebView.Visibility = Visibility.Visible;
                    tcs.SetResult(true);
                });
                await tcs.Task;
            }

            // Ensure WebView2 is initialized before showing debug panel
            if (KioskWebView == null)
            {
                Logger.Log("KioskWebView is null, cannot enter debug mode");
                ShowStatus("Error", "WebView2 control is not available");
                return;
            }

            if (KioskWebView.CoreWebView2 == null)
            {
                Logger.Log("WebView2 not initialized, initializing now...");
                ShowStatus("DEBUG MODE", "Initializing WebView2...");

                try
                {
                    await KioskWebView.EnsureCoreWebView2Async();
                    await SetupWebViewAsync();

                    // Navigate to default URL if we don't have a current URL
                    if (string.IsNullOrEmpty(_currentUrl))
                    {
                        _currentUrl = _config.Kiosk.DefaultUrl;
                        KioskWebView.Source = new Uri(_currentUrl);
                    }

                    Logger.Log("WebView2 initialized successfully");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to initialize WebView2: {ex.Message}");
                    ShowStatus("Error", $"Failed to initialize WebView2: {ex.Message}");
                    return;
                }
            }

            // Now WebView2 is guaranteed to be ready - show debug panel and configure window
            var uiTcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Show debug navigation panel
                    DebugPanel.Visibility = Visibility.Visible;

                    // Adjust WebView margin to make room for navigation panel
                    if (KioskWebView != null)
                        KioskWebView.Margin = new Thickness(0, 80, 0, 0);

                    // Update URL textbox with current URL
                    if (!string.IsNullOrEmpty(_currentUrl))
                    {
                        UrlTextBox.Text = _currentUrl;
                    }
                    else if (KioskWebView?.Source != null)
                    {
                        UrlTextBox.Text = KioskWebView.Source.ToString();
                    }

                    // Enable WebView2 developer features
                    if (KioskWebView?.CoreWebView2?.Settings != null)
                    {
                        var settings = KioskWebView.CoreWebView2.Settings;
                        settings.AreDevToolsEnabled = true;
                        settings.AreDefaultContextMenusEnabled = true;
                        settings.AreBrowserAcceleratorKeysEnabled = true;

                        // Open developer tools
                        KioskWebView.CoreWebView2.OpenDevToolsWindow();
                    }

                    // Clear stale camera/microphone dropdown data
                    _suppressMediaSelectionEvents = true;
                    try
                    {
                        var oldCameraCount = CameraSelector.Items.Count;
                        var oldMicCount = MicrophoneSelector.Items.Count;
                        CameraSelector.Items.Clear();
                        MicrophoneSelector.Items.Clear();
                        CameraSelector.SelectedIndex = -1;
                        MicrophoneSelector.SelectedIndex = -1;
                        Logger.Log($"[DEBUG MODE] Cleared stale dropdown data (was: {oldCameraCount} cameras, {oldMicCount} mics)");
                    }
                    finally
                    {
                        _suppressMediaSelectionEvents = false;
                    }

                    // Enumerate devices for dropdowns
                    _ = LoadAllMediaDevicesAsync();

                    // Window the application - remove fullscreen presenter
                    if (_appWindow?.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                    {
                        _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    }

                    // Restore window frame
                    var style = Win32Native.GetWindowLong(_hwnd, Win32Native.GWL_STYLE);
                    style |= Win32Native.WS_CAPTION | Win32Native.WS_THICKFRAME | Win32Native.WS_MINIMIZEBOX | Win32Native.WS_MAXIMIZEBOX | Win32Native.WS_SYSMENU;
                    Win32Native.SetWindowLong(_hwnd, Win32Native.GWL_STYLE, style);

                    // Remove topmost
                    var exStyle = Win32Native.GetWindowLong(_hwnd, Win32Native.GWL_EXSTYLE);
                    exStyle &= ~Win32Native.WS_EX_TOPMOST;
                    Win32Native.SetWindowLong(_hwnd, Win32Native.GWL_EXSTYLE, exStyle);

                    // Apply changes
                    Win32Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOMOVE);

                    // Update window title
                    this.Title = "[DEBUG MODE] OneRoom Health Kiosk";

                    _isDebugMode = true;
                    Logger.Log("Debug mode enabled");

                    ShowStatus("DEBUG MODE", "Developer tools enabled. Press Ctrl+Shift+I to exit.");
                    _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));

                    uiTcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error configuring debug mode UI: {ex.Message}");
                    ShowStatus("Debug Mode Error", ex.Message);
                    uiTcs.SetException(ex);
                }
            });
            await uiTcs.Task;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error entering debug mode: {ex.Message}");
            ShowStatus("Error", $"Failed to enter debug mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Exits debug mode: returns to fullscreen kiosk mode.
    /// </summary>
    private async Task ExitDebugMode()
    {
        Logger.LogSecurityEvent("ExitDebugMode", "Exiting debug mode");

        await DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                // Disable developer features
                if (KioskWebView?.CoreWebView2?.Settings != null)
                {
                    var settings = KioskWebView.CoreWebView2.Settings;
                    settings.AreDevToolsEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreBrowserAcceleratorKeysEnabled = false;
                }

                // Hide log viewer if visible
                if (_logsVisible)
                {
                    LogViewerPanel.Visibility = Visibility.Collapsed;
                    _logsVisible = false;
                }

                // Hide debug panel and reset margin
                DebugPanel.Visibility = Visibility.Collapsed;
                if (KioskWebView != null)
                    KioskWebView.Margin = new Thickness(0);

                // Update window title
                this.Title = "OneRoom Health Kiosk";

                // Set presenter to Overlapped
                if (_appWindow != null)
                {
                    _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    Logger.Log("Set presenter to Overlapped before configuration");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in ExitDebugMode UI cleanup: {ex.Message}");
            }
        });

        // Wait for presenter change to take effect
        await Task.Delay(300);

        // Now restore kiosk window configuration
        await DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                ConfigureAsKioskWindow();

                // Reset WebView to fill window
                if (_appWindow != null && KioskWebView != null)
                {
                    KioskWebView.Width = double.NaN;
                    KioskWebView.Height = double.NaN;
                    KioskWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
                    KioskWebView.VerticalAlignment = VerticalAlignment.Stretch;
                    KioskWebView.Margin = new Thickness(0);
                    Logger.Log("Reset WebView size to Auto/Stretch");
                }

                // Force layout update
                if (this.Content is UIElement content)
                {
                    content.UpdateLayout();
                }
                if (KioskWebView != null)
                {
                    KioskWebView.UpdateLayout();
                    Logger.Log("Forced WebView layout update");
                }

                // Re-run ConfigureAsKioskWindow after delay to ensure proper sizing
                _ = Task.Delay(200).ContinueWith(async _ =>
                {
                    await DispatcherQueue.EnqueueAsync(() =>
                    {
                        ConfigureAsKioskWindow();
                        Logger.Log("Re-ran ConfigureAsKioskWindow after delay to ensure proper sizing");

                        if (this.Content is UIElement content2)
                        {
                            content2.UpdateLayout();
                        }
                        if (KioskWebView != null)
                        {
                            KioskWebView.UpdateLayout();
                            Logger.Log("Forced final layout update");
                        }
                    });
                });

                // In video mode, hide WebView and restart video
                if (_isVideoMode && _videoController != null)
                {
                    if (KioskWebView != null)
                        KioskWebView.Visibility = Visibility.Collapsed;
                    _ = _videoController.InitializeAsync();
                }
                else
                {
                    // Trigger a resize event so the content adapts to the new window size
                    _ = Task.Delay(300).ContinueWith(_ =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (KioskWebView?.CoreWebView2 != null)
                            {
                                _ = KioskWebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('resize'));").AsTask();
                                Logger.Log("[DEBUG EXIT] Triggered resize event in web content (no navigation/reload)");
                            }
                        });
                    });
                }

                _isDebugMode = false;
                Logger.Log("Debug mode disabled, returned to kiosk mode");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error exiting debug mode: {ex.Message}");
            }
        });
    }

    #endregion

    #region Video/Screensaver Mode

    /// <summary>
    /// Switches from screensaver mode to video mode or toggles videos if already in video mode.
    /// </summary>
    private async Task SwitchToVideoMode()
    {
        if (_isVideoMode)
        {
            // Already in video mode - toggle between videos
            Logger.Log("Already in video mode - toggling video source");
            if (_videoController != null)
            {
                await _videoController.HandleFlicButtonPressAsync();
            }
            return;
        }

        Logger.Log("========== SWITCHING TO VIDEO MODE ==========");
        _isVideoMode = true;

        try
        {
            // Hide the WebView
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (KioskWebView != null)
                    {
                        KioskWebView.Visibility = Visibility.Collapsed;
                        Logger.Log("WebView hidden for video mode");
                    }
                });
            });

            // Start playing the carescape video
            if (_videoController != null)
            {
                await _videoController.HandleFlicButtonPressAsync();
                Logger.Log("Video playback started");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching to video mode: {ex.Message}");
            _isVideoMode = false;
        }
    }

    /// <summary>
    /// Switches from video mode to screensaver mode.
    /// </summary>
    private async Task SwitchToScreensaverMode()
    {
        if (!_isVideoMode)
        {
            Logger.Log("Already in screensaver mode");
            return;
        }

        Logger.Log("========== SWITCHING TO SCREENSAVER MODE ==========");
        _isVideoMode = false;

        try
        {
            // Stop any playing video
            if (_videoController != null)
            {
                await _videoController.StopAsync();
                Logger.Log("Video stopped");
            }

            // Set presenter to Overlapped before configuring
            if (_appWindow != null)
            {
                if (_appWindow.Presenter.Kind != Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped)
                {
                    _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    Logger.Log("Set presenter to Overlapped mode");
                }
                await Task.Delay(100);
            }

            // Configure the window
            ConfigureAsKioskWindow();
            Logger.Log("Window configured for fullscreen kiosk mode");

            // Reset WebView dimensions to auto/stretch
            if (_appWindow != null && KioskWebView != null)
            {
                KioskWebView.Width = double.NaN;
                KioskWebView.Height = double.NaN;
                KioskWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
                KioskWebView.VerticalAlignment = VerticalAlignment.Stretch;
                Logger.Log("Set WebView sizing to Auto/Stretch");
            }

            // Show the WebView and navigate to screensaver URL
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (KioskWebView != null)
                    {
                        KioskWebView.Visibility = Visibility.Visible;
                        KioskWebView.UpdateLayout();
                        Logger.Log("WebView shown for screensaver mode");

                        // Navigate to the screensaver URL
                        if (!string.IsNullOrEmpty(_currentUrl))
                        {
                            KioskWebView.Source = new Uri(_currentUrl);
                            Logger.Log($"Navigating back to: {_currentUrl}");
                        }
                        else if (!string.IsNullOrEmpty(_config.Kiosk.DefaultUrl))
                        {
                            _currentUrl = _config.Kiosk.DefaultUrl;
                            KioskWebView.Source = new Uri(_currentUrl);
                            Logger.Log($"Navigating to default URL: {_currentUrl}");
                        }

                        // Re-run ConfigureAsKioskWindow after delay
                        _ = Task.Delay(200).ContinueWith(async _ =>
                        {
                            await DispatcherQueue.EnqueueAsync(() =>
                            {
                                ConfigureAsKioskWindow();
                                Logger.Log("Re-ran ConfigureAsKioskWindow after delay (screensaver transition)");
                                if (KioskWebView != null)
                                {
                                    KioskWebView.UpdateLayout();
                                }
                            });
                        });

                        // Force a resize event
                        _ = Task.Delay(500).ContinueWith(_ =>
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (KioskWebView?.CoreWebView2 != null)
                                {
                                    _ = KioskWebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('resize'));").AsTask();
                                    Logger.Log("Triggered resize event in screensaver content");
                                }
                            });
                        });
                    }
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching to screensaver mode: {ex.Message}");
        }
    }

    #endregion

    #region Exit Handling

    /// <summary>
    /// Handles the exit request with password protection.
    /// </summary>
    private async Task HandleExitRequest()
    {
        Logger.LogSecurityEvent("ExitRequested", "User initiated exit request");

        var dialog = new ContentDialog
        {
            Title = "Exit Kiosk Mode",
            Content = new PasswordBox
            {
                PlaceholderText = "Enter exit password",
                Width = 300
            },
            PrimaryButtonText = "Exit",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var passwordBox = (PasswordBox)dialog.Content;
            var enteredPassword = passwordBox.Password;

            bool isValid = false;

            // Support both plain text passwords and SHA256 hashes for backward compatibility
            if (string.IsNullOrEmpty(_config.Exit.PasswordHash))
            {
                // Default to admin123 if no password is set
                Logger.Log("Password hash is empty, using default password");
                isValid = enteredPassword == "admin123";
            }
            else if (_config.Exit.PasswordHash.Length < 64)  // SHA256 hashes are 64 hex characters
            {
                // Treat as plain text password
                Logger.Log("Treating password as plain text");
                isValid = enteredPassword == _config.Exit.PasswordHash;
            }
            else
            {
                // Treat as SHA256 hash
                Logger.Log("Treating password as SHA256 hash");
                isValid = SecurityHelper.ValidatePassword(enteredPassword, _config.Exit.PasswordHash);
            }

            if (isValid)
            {
                Logger.LogSecurityEvent("ExitAuthorized", "Correct password provided, exiting application");
                await CleanupAndExit();
            }
            else
            {
                Logger.LogSecurityEvent("ExitDenied", "Incorrect password provided");
                ShowStatus("Access Denied", "Incorrect password");
                await Task.Delay(2000);
                HideStatus();
            }
        }
    }

    /// <summary>
    /// Performs cleanup and exits the application.
    /// </summary>
    private async Task CleanupAndExit()
    {
        try
        {
            Logger.Log("========== APPLICATION EXIT START ==========");

            // Clean up ACS call and media tracks before closing WebView
            if (KioskWebView?.CoreWebView2 != null)
            {
                try
                {
                    // First, try to hang up any active ACS call
                    Logger.Log("Attempting to hang up ACS call...");
                    await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
                        (async () => {
                            try {
                                if (window.call && typeof window.call.hangUp === 'function') {
                                    console.log('[ORH EXIT] Hanging up ACS call...');
                                    await window.call.hangUp();
                                    console.log('[ORH EXIT] ACS call hung up successfully');
                                } else if (window.currentCall && typeof window.currentCall.hangUp === 'function') {
                                    console.log('[ORH EXIT] Hanging up currentCall...');
                                    await window.currentCall.hangUp();
                                    console.log('[ORH EXIT] currentCall hung up successfully');
                                } else {
                                    console.log('[ORH EXIT] No ACS call object found to hang up');
                                }
                            } catch (e) {
                                console.log('[ORH EXIT] Error hanging up ACS call: ' + e);
                            }
                        })();
                    ");
                    Logger.Log("ACS hangup attempted");
                    await Task.Delay(300);

                    // Stop local media tracks
                    Logger.Log("Stopping local media tracks...");
                    await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
                        (() => {
                            try {
                                if (typeof window.__orhStopLocalTracks === 'function') {
                                    const stopped = window.__orhStopLocalTracks();
                                    console.log('[ORH EXIT] Stopped ' + stopped + ' local media track(s)');
                                }
                                if (window.__orhPeerConnections) {
                                    window.__orhPeerConnections.forEach(pc => {
                                        try {
                                            if (pc && pc.getSenders) {
                                                pc.getSenders().forEach(s => {
                                                    try {
                                                        if (s && s.track) s.track.stop();
                                                    } catch (e) {}
                                                });
                                            }
                                        } catch (e) {}
                                    });
                                    console.log('[ORH EXIT] Stopped tracks from peer connections');
                                }
                                document.querySelectorAll('video').forEach(v => {
                                    try {
                                        if (v.srcObject && v.srcObject.getTracks) {
                                            v.srcObject.getTracks().forEach(t => t.stop());
                                            console.log('[ORH EXIT] Stopped tracks from video element');
                                        }
                                        v.srcObject = null;
                                    } catch (e) {}
                                });
                            } catch (e) {
                                console.log('[ORH EXIT] Error stopping tracks: ' + e);
                            }
                        })();
                    ");
                    Logger.Log("Local media tracks stopped");

                    // Navigate to screensaver URL to destroy the ACS page context
                    var screensaverUrl = _config.Kiosk.DefaultUrl;
                    Logger.Log($"Navigating to screensaver to destroy ACS context: {screensaverUrl}");
                    KioskWebView.Source = new Uri(screensaverUrl);
                    await Task.Delay(500);
                    Logger.Log("ACS context destroyed, media resources released");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to cleanup media (non-fatal): {ex.Message}");
                }
            }

            // Stop media preference sync timer
            if (_mediaPreferenceSyncTimer != null)
            {
                Logger.Log("Stopping media preference sync timer...");
                _mediaPreferenceSyncTimer.Tick -= MediaPreferenceSyncTimer_Tick;
                _mediaPreferenceSyncTimer.Stop();
                _mediaPreferenceSyncTimer = null;
                Logger.Log("Media preference sync timer stopped");
            }

            // Stop video controller if active
            if (_videoController != null)
            {
                Logger.Log("Stopping video controller...");
                await _videoController.StopAsync();
                _videoController.Dispose();
                Logger.Log("Video controller stopped");
            }

            // Stop command server if running
            Logger.Log("Stopping command server...");
            LocalCommandServer.Stop();

            // Unhook keyboard hook
            RemoveKeyboardHook();

            Logger.Log("Closing application window...");

            // Use dispatcher to ensure we're on the UI thread
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    this.Close();
                    Logger.Log("Exiting application process...");
                    Environment.Exit(0);
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during cleanup: {ex.Message}");
            Environment.Exit(1);
        }
    }

    #endregion
}
