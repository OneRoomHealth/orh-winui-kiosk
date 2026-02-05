using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - WebView2 initialization, setup, scripts, and message handling.
/// </summary>
public sealed partial class MainWindow
{
    #region WebView Fields

    // WebView->Host message bridge for async media enumeration (ExecuteScriptAsync does not reliably await Promises)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingWebMessages = new();
    private bool _webMessageBridgeInitialized = false;
    private bool _webProcessFailedLoggingInitialized = false;
    private bool _webViewNavigationCompletedInitialized = false;
    private bool _coreEventHandlersInitialized = false;
    private Microsoft.Web.WebView2.Core.CoreWebView2? _wiredCoreWebView2 = null;
    private int _wiredCoreWebView2Id = 0;
    private bool _isRecoveringWebViewProcessFailure = false;
    private bool _mediaOverrideDocCreatedScriptAdded = false;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _mediaPreferenceSyncTimer = null;

    #endregion

    #region WebView Initialization

    /// <summary>
    /// Initializes the WebView2 control and navigates to the default URL.
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        Logger.Log("========== InitializeWebViewAsync START ==========");
        try
        {
            // Always start in screensaver mode (WebView visible)
            Logger.Log("Starting in SCREENSAVER MODE (default)");
            KioskWebView.Visibility = Visibility.Visible;

            // Initialize WebView2
            Logger.Log("Initializing WebView2");
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Logger.Log($"WebView2 Runtime version: {version}");
                ShowStatus("Initializing", "Loading WebView2...");

                Logger.Log("Creating WebView2 environment...");
                var environment = await CoreWebView2Environment.CreateAsync();

                Logger.Log("Ensuring CoreWebView2 is ready...");
                await KioskWebView.EnsureCoreWebView2Async(environment);
                Logger.Log("CoreWebView2 is ready, setting up WebView...");

                await SetupWebViewAsync();

                // Navigate to the configured URL
                _currentUrl = _config.Kiosk.DefaultUrl;
                Logger.Log($"Setting WebView source to: {_config.Kiosk.DefaultUrl}");
                KioskWebView.Source = new Uri(_config.Kiosk.DefaultUrl);
                Logger.Log($"Navigation initiated to: {_config.Kiosk.DefaultUrl}");

                // Add a fallback to hide status after a timeout in case navigation doesn't complete
                Logger.Log("Starting 3-second timeout fallback for status overlay");
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        Logger.Log($"Timeout reached. Current status title: '{StatusTitle.Text}'");
                        if (StatusTitle.Text == "Initializing")
                        {
                            Logger.Log("Forcing status overlay to hide after timeout");
                            HideStatus();
                        }
                        else
                        {
                            Logger.Log($"Status already changed to '{StatusTitle.Text}', not forcing hide");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"WebView2 initialization error: {ex.Message}");
                Logger.Log($"Stack trace: {ex.StackTrace}");
                ShowStatus("WebView2 Error",
                    "WebView2 Runtime is not installed.\n\n" +
                    "Please install from:\n" +
                    "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            }

            // Initialize video controller if available (but don't start video)
            if (_videoController != null)
            {
                await _videoController.InitializeAsync();
                Logger.Log("Video controller ready (can be activated with Ctrl+Alt+D)");
            }

            // Start in the user's preferred API mode (persisted from last session)
            var prefs = Helpers.UserPreferences.Instance;
            if (prefs.UseHardwareApiMode && App.Instance != null)
            {
                Logger.Log("Starting in Hardware API mode (user preference)");
                try
                {
                    await App.Instance.EnableHardwareApiModeAsync(this);
                    Logger.Log("Hardware API mode enabled on startup");
                }
                catch (Exception apiEx)
                {
                    Logger.Log($"Failed to enable Hardware API mode on startup: {apiEx.Message}");
                    Logger.Log("Falling back to Navigate mode");
                    App.Instance?.StartLocalCommandServer(this);
                }
            }
            else
            {
                Logger.Log("Starting in Navigate mode (user preference)");
                App.Instance?.StartLocalCommandServer(this);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"InitializeWebViewAsync error: {ex.Message}");
            Logger.Log($"Stack trace: {ex.StackTrace}");
            ShowStatus("Initialization Error", ex.Message);
        }
        Logger.Log("========== InitializeWebViewAsync COMPLETE ==========");
    }

    /// <summary>
    /// Configures WebView2 settings, including developer tools restrictions.
    /// Must be awaited before first navigation to avoid races with AddScriptToExecuteOnDocumentCreatedAsync.
    /// </summary>
    private async Task SetupWebViewAsync()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("[WEBVIEW] SetupWebViewAsync called but CoreWebView2 is null");
            return;
        }

        var core = KioskWebView.CoreWebView2;
        var coreId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(core);

        // If the underlying CoreWebView2 instance has changed (common after BrowserProcessExited),
        // we must rewire all core-level event handlers and re-install document-created scripts.
        if (!ReferenceEquals(_wiredCoreWebView2, core))
        {
            var oldId = _wiredCoreWebView2Id;
            _wiredCoreWebView2 = core;
            _wiredCoreWebView2Id = coreId;
            Logger.Log($"[WEBVIEW] CoreWebView2 instance changed: {oldId} -> {_wiredCoreWebView2Id}");

            // Cancel any in-flight message waits (they belong to the old core / old document context)
            var pending = _pendingWebMessages.Count;
            if (pending > 0)
            {
                Logger.Log($"[WEBVIEW] Cancelling {pending} pending web message request(s) due to CoreWebView2 instance change");
                foreach (var kvp in _pendingWebMessages)
                {
                    kvp.Value.TrySetCanceled();
                }
                _pendingWebMessages.Clear();
            }

            // Reset per-core wiring flags
            _webMessageBridgeInitialized = false;
            _webProcessFailedLoggingInitialized = false;
            _coreEventHandlersInitialized = false;
            _mediaOverrideDocCreatedScriptAdded = false;
        }

        var settings = KioskWebView.CoreWebView2.Settings;

        // Kiosk mode settings
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;

        // Developer tools are initially disabled (unless debug mode is active)
        settings.AreDevToolsEnabled = _isDebugMode;
        settings.AreDefaultContextMenusEnabled = _isDebugMode;
        settings.AreDefaultScriptDialogsEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = false; // Disable F5, Ctrl+R, etc.

        // Navigation event handlers (control-level; only wire once)
        if (!_webViewNavigationCompletedInitialized)
        {
            _webViewNavigationCompletedInitialized = true;
            KioskWebView.NavigationCompleted += OnNavigationCompleted;
        }

        // Initialize WebMessage bridge once (used for async media device enumeration)
        if (!_webMessageBridgeInitialized)
        {
            _webMessageBridgeInitialized = true;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            Logger.Log($"[WEBVIEW] Wired WebMessageReceived (core={coreId})");
        }

        // Capture WebView process failures (renderer/browser crashes) which can present as timeouts.
        if (!_webProcessFailedLoggingInitialized)
        {
            _webProcessFailedLoggingInitialized = true;
            core.ProcessFailed += OnWebViewProcessFailed;
            Logger.Log($"[WEBVIEW] Wired ProcessFailed (core={coreId})");
        }

        // Core-level event handlers that must be (re)attached per CoreWebView2 instance
        if (!_coreEventHandlersInitialized)
        {
            _coreEventHandlersInitialized = true;
            Logger.Log($"[WEBVIEW] Wiring core event handlers (core={coreId})");

            // Auto-allow all permissions for kiosk mode (camera, microphone, autoplay, etc.)
            core.PermissionRequested += (sender, args) =>
            {
                args.State = CoreWebView2PermissionState.Allow;
                args.SavesInProfile = true;
                Logger.Log($"Auto-allowed permission: {args.PermissionKind} for {args.Uri}");
            };

            // Disable new window requests
            core.NewWindowRequested += (sender, args) =>
            {
                args.Handled = true; // Block popups and new windows
            };

            // Prevent WebView from capturing all keyboard input
            core.DocumentTitleChanged += (sender, args) =>
            {
                _ = EnsureFocusHandling();
            };

            // Additional WebView keyboard handling
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);

            // DOMContentLoaded injection (hotkeys + autoplay + console forwarding)
            core.DOMContentLoaded += OnDOMContentLoaded;
        }

        // Install getUserMedia override as early as possible (before page scripts run).
        await InstallMediaOverrideOnDocumentCreatedAsync();

        // Ensure status overlay is hidden when WebView is ready
        Logger.Log("WebView2 setup complete, ensuring status overlay is hidden");
        DispatcherQueue.TryEnqueue(() => HideStatus());

        Logger.Log("WebView2 setup completed");

        // Start periodic sync timer to ensure localStorage stays in sync with app prefs
        StartMediaPreferenceSyncTimer();
    }

    /// <summary>
    /// Handles WebView process failures for recovery.
    /// </summary>
    private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        try
        {
            var currentCore = KioskWebView?.CoreWebView2;
            var currentId = currentCore != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(currentCore) : 0;
            Logger.Log($"[WEBVIEW PROCESS FAILED] Kind={args.ProcessFailedKind}, core={currentId}");

            // Mark core wiring as stale; on next SetupWebViewAsync we will rewire.
            _wiredCoreWebView2 = null;
            _wiredCoreWebView2Id = 0;
            _webMessageBridgeInitialized = false;
            _webProcessFailedLoggingInitialized = false;
            _coreEventHandlersInitialized = false;
            _mediaOverrideDocCreatedScriptAdded = false;

            // Best-effort recovery: re-ensure CoreWebView2 and re-run setup on UI thread.
            if (!_isRecoveringWebViewProcessFailure)
            {
                _isRecoveringWebViewProcessFailure = true;
                _ = DispatcherQueue.EnqueueAsync(async () =>
                {
                    try
                    {
                        Logger.Log($"[WEBVIEW] Attempting recovery after ProcessFailed ({args.ProcessFailedKind})...");
                        await KioskWebView.EnsureCoreWebView2Async();
                        await SetupWebViewAsync();

                        // Reload current URL if available
                        if (!string.IsNullOrWhiteSpace(_currentUrl))
                        {
                            Logger.Log($"[WEBVIEW] Recovery reload navigating to: {_currentUrl}");
                            KioskWebView.Source = new Uri(_currentUrl);
                        }
                        else
                        {
                            Logger.Log("[WEBVIEW] Recovery reload: calling WebView.Reload()");
                            KioskWebView.Reload();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[WEBVIEW] Recovery failed: {ex.Message}");
                    }
                    finally
                    {
                        _isRecoveringWebViewProcessFailure = false;
                    }
                });
            }
        }
        catch
        {
            // ignore logging failures
        }
    }

    /// <summary>
    /// Handles DOMContentLoaded to inject kiosk scripts.
    /// </summary>
    private async void OnDOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        try
        {
            var core = KioskWebView?.CoreWebView2;
            if (core == null) return;

            // Inject JavaScript to:
            // 1. Prevent WebView from consuming our hotkeys
            // 2. Enable autoplay for all media elements
            // 3. Forward console messages to host
            string script = GetKioskInjectionScript();
            await core.ExecuteScriptAsync(script);
            Logger.Log("Injected kiosk scripts (hotkeys + autoplay)");

            // Re-apply persisted camera/mic overrides on every page load
            if (!string.IsNullOrWhiteSpace(_selectedCameraId) || !string.IsNullOrWhiteSpace(_selectedMicrophoneId))
            {
                await ApplyMediaDeviceOverrideAsync(showStatus: false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error injecting kiosk scripts: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the JavaScript to inject on DOM content loaded.
    /// </summary>
    private string GetKioskInjectionScript()
    {
        return @"
            // Forward page console + runtime errors to host logs (WebView2 SDK compatibility)
            (function () {
                try {
                    if (window.__orhConsoleForwardInstalled) return;
                    window.__orhConsoleForwardInstalled = true;

                    function post(level, message, extra) {
                        try {
                            if (window.chrome && chrome.webview && chrome.webview.postMessage) {
                                chrome.webview.postMessage({
                                    type: 'orh.console',
                                    requestId: 'console-' + Date.now() + '-' + Math.random().toString(16).slice(2),
                                    level: level,
                                    message: String(message || ''),
                                    extra: extra || null
                                });
                            }
                        } catch (e) { }
                    }

                    var orig = {
                        log: console.log,
                        info: console.info,
                        warn: console.warn,
                        error: console.error
                    };
                    ['log', 'info', 'warn', 'error'].forEach(function (k) {
                        try {
                            console[k] = function () {
                                try {
                                    var args = Array.prototype.slice.call(arguments);
                                    post(k, args.map(function (a) {
                                        try { return typeof a === 'string' ? a : JSON.stringify(a); } catch (e) { return String(a); }
                                    }).join(' '), null);
                                } catch (e) { }
                                return orig[k].apply(console, arguments);
                            };
                        } catch (e) { }
                    });

                    window.addEventListener('error', function (evt) {
                        try {
                            post('error', 'window.onerror: ' + (evt && evt.message ? evt.message : ''), {
                                source: evt && evt.filename ? evt.filename : null,
                                line: evt && evt.lineno ? evt.lineno : null,
                                col: evt && evt.colno ? evt.colno : null
                            });
                        } catch (e) { }
                    });

                    window.addEventListener('unhandledrejection', function (evt) {
                        try {
                            var reason = evt && evt.reason ? evt.reason : null;
                            post('error', 'unhandledrejection', { reason: reason ? String(reason) : null });
                        } catch (e) { }
                    });
                } catch (e) { }
            })();

            // Keyboard hotkey handling
            document.addEventListener('keydown', function(e) {
                // Check if this is one of our hotkeys
                if ((e.ctrlKey && e.shiftKey && (e.key === 'i' || e.key === 'I' || e.key === 'q' || e.key === 'Q')) ||
                    (e.ctrlKey && e.altKey && (e.key === 'd' || e.key === 'D' || e.key === 'e' || e.key === 'E' || e.key === 'r' || e.key === 'R'))) {
                    // Prevent the webpage from handling these keys
                    e.preventDefault();
                    console.log('Kiosk hotkey detected:', e.key);
                }
            }, true);

            // Enable autoplay for all video and audio elements
            function enableAutoplay() {
                document.querySelectorAll('video, audio').forEach(function(media) {
                    media.autoplay = true;
                    media.muted = false;
                    if (media.paused) {
                        media.play().catch(function(e) {
                            // If unmuted autoplay fails, try muted
                            media.muted = true;
                            media.play().catch(function() {});
                        });
                    }
                });
            }

            // Run immediately and watch for new media elements
            enableAutoplay();

            // MutationObserver to handle dynamically added media
            var observer = new MutationObserver(function(mutations) {
                enableAutoplay();
            });
            observer.observe(document.body, { childList: true, subtree: true });
            ";
    }

    #endregion

    #region Media Preference Sync Timer

    private void StartMediaPreferenceSyncTimer()
    {
        // Stop any existing timer
        if (_mediaPreferenceSyncTimer != null)
        {
            _mediaPreferenceSyncTimer.Stop();
            _mediaPreferenceSyncTimer.Tick -= MediaPreferenceSyncTimer_Tick;
            _mediaPreferenceSyncTimer = null;
        }

        _mediaPreferenceSyncTimer = DispatcherQueue.CreateTimer();
        _mediaPreferenceSyncTimer.Interval = TimeSpan.FromSeconds(30);
        _mediaPreferenceSyncTimer.IsRepeating = true;
        _mediaPreferenceSyncTimer.Tick += MediaPreferenceSyncTimer_Tick;
        _mediaPreferenceSyncTimer.Start();

        Logger.Log("Media preference sync timer started (DispatcherQueueTimer, 30s interval)");
    }

    private async void MediaPreferenceSyncTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (KioskWebView?.CoreWebView2 == null) return;
            if (string.IsNullOrWhiteSpace(_selectedCameraId) && string.IsNullOrWhiteSpace(_selectedMicrophoneId)) return;

            await ApplyMediaDeviceOverrideAsync(showStatus: false);
            Logger.Log("[MEDIA SYNC] Periodic localStorage sync completed");
        }
        catch (Exception ex)
        {
            Logger.Log($"[MEDIA SYNC] Timer callback error: {ex.Message}");
        }
    }

    #endregion

    #region WebView Message Handling

    /// <summary>
    /// Handles messages received from the WebView via chrome.webview.postMessage.
    /// </summary>
    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json))
            {
                Logger.Log("[WEBMSG] Received empty WebMessage");
                return;
            }

            // Log first 200 chars of every received message for debugging
            var preview = json.Length > 200 ? json.Substring(0, 200) + "..." : json;
            var coreId = KioskWebView?.CoreWebView2 != null
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(KioskWebView.CoreWebView2)
                : 0;
            var source = string.IsNullOrWhiteSpace(e.Source) ? "unknown" : e.Source;
            Logger.Log($"[WEBMSG] (core={coreId}) Received from {source}: {preview}");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                Logger.Log("[WEBMSG] No 'type' property in message");
                return;
            }
            if (!doc.RootElement.TryGetProperty("requestId", out var reqEl))
            {
                Logger.Log("[WEBMSG] No 'requestId' property in message");
                return;
            }

            var type = typeEl.GetString();
            var requestId = reqEl.GetString();
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(requestId))
            {
                Logger.Log("[WEBMSG] Empty type or requestId");
                return;
            }

            // Only handle our internal messages
            if (type != "orh.mediaDevices.result" && type != "orh.webrtc.diag" && type != "orh.media.override" && type != "orh.console")
            {
                Logger.Log($"[WEBMSG] Unknown type: {type}");
                return;
            }

            // Log-only message type (no request correlation needed)
            if (type == "orh.media.override")
            {
                Logger.Log($"[MEDIA OVERRIDE] {json}");
                return;
            }
            if (type == "orh.console")
            {
                try
                {
                    var level = doc.RootElement.TryGetProperty("level", out var lvl) ? lvl.GetString() : "log";
                    var message = doc.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() : "";
                    Logger.Log($"[WEB CONSOLE] {level}: {message}");
                }
                catch
                {
                    Logger.Log($"[WEB CONSOLE] {json}");
                }
                return;
            }

            if (_pendingWebMessages.TryRemove(requestId, out var tcs))
            {
                Logger.Log($"[WEBMSG] Matched requestId={requestId}, completing TCS");
                tcs.TrySetResult(json);
            }
            else
            {
                Logger.Log($"[WEBMSG] No pending request for requestId={requestId} (pending count: {_pendingWebMessages.Count})");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"WebMessageReceived parse error: {ex.Message}");
        }
    }

    #endregion

    #region Navigation Event Handling

    /// <summary>
    /// Handles WebView navigation completion.
    /// </summary>
    private void OnNavigationCompleted(Microsoft.UI.Xaml.Controls.WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        Logger.Log($"========== NAVIGATION COMPLETED ==========");
        Logger.Log($"Success: {args.IsSuccess}, Error: {args.WebErrorStatus}");

        DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsSuccess)
            {
                var uri = sender.Source.ToString();
                Logger.Log($"Navigation successful to: {uri}");

                // Update current URL tracking (but don't store about:blank)
                if (!uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    _currentUrl = uri;
                    Logger.Log($"Current URL updated to: {_currentUrl}");

                    // Update URL textbox if in debug mode
                    if (_isDebugMode && UrlTextBox != null)
                    {
                        UrlTextBox.Text = _currentUrl;
                    }

                    // If debug mode is active, refresh the media device lists after navigation.
                    if (_isDebugMode)
                    {
                        var timeSinceReload = (DateTime.UtcNow - _lastWebViewReloadTime).TotalMilliseconds;
                        if (timeSinceReload < SkipEnumerationAfterReloadMs)
                        {
                            Logger.Log($"[NAV COMPLETE] Skipping media enumeration - reload was {timeSinceReload:F0}ms ago (threshold: {SkipEnumerationAfterReloadMs}ms)");
                        }
                        else
                        {
                            _ = DispatcherQueue.EnqueueAsync(async () =>
                            {
                                await Task.Delay(250);
                                await LoadAllMediaDevicesAsync();
                            });
                        }
                    }
                }
                else
                {
                    Logger.Log($"Navigation to about:blank detected, keeping previous URL: {_currentUrl ?? "none"}");
                }

                // Update title
                var title = sender.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrEmpty(title))
                {
                    this.Title = _isDebugMode ? $"[DEBUG] {title}" : "OneRoom Health Kiosk";
                    Logger.Log($"Window title updated to: {this.Title}");
                }

                Logger.Log("Hiding status overlay after successful navigation");
                HideStatus();
            }
            else
            {
                Logger.Log($"Navigation FAILED: {args.WebErrorStatus}");
                ShowStatus("Navigation Failed", $"Error: {args.WebErrorStatus}");
            }
        });
    }

    /// <summary>
    /// Ensures proper focus handling for keyboard input.
    /// </summary>
    private async Task EnsureFocusHandling()
    {
        await Task.Delay(100);

        if (!_isVideoMode)
        {
            Logger.Log("Focus handling check completed");
        }
    }

    #endregion

    #region Status Overlay

    /// <summary>
    /// Shows the status overlay with the specified title and detail.
    /// </summary>
    private void ShowStatus(string title, string? detail = null)
    {
        Logger.Log($"[STATUS] SHOWING: {title} - {detail}");
        Debug.WriteLine($"[STATUS] SHOWING: {title} - {detail}");
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusTitle.Text = title;
            StatusDetail.Text = detail ?? string.Empty;
            StatusOverlay.Visibility = Visibility.Visible;
            Logger.Log($"[STATUS] StatusOverlay.Visibility set to VISIBLE");
        });
    }

    /// <summary>
    /// Hides the status overlay.
    /// </summary>
    private void HideStatus()
    {
        Logger.Log("[STATUS] HIDING status overlay");
        Debug.WriteLine("[STATUS] HIDING status overlay");
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            Logger.Log("[STATUS] StatusOverlay.Visibility set to COLLAPSED");
        });
    }

    #endregion
}
