using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.Graphics;
using Windows.UI.Core;
using WinRT.Interop;

namespace KioskApp;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private KioskConfiguration _config = new();
    private bool _isDebugMode = false;
    private RectInt32 _normalWindowBounds;
    private IntPtr _hwnd;
    
    // Video mode fields
    private bool _isVideoMode = false;
    private VideoController? _videoController;

    // Monitor index is now configured via config.json instead of being hardcoded

    // Win32 styles for borderless + topmost
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOMOVE = 0x0002;

    public MainWindow()
    {
        InitializeComponent();

        // Load configuration
        _config = ConfigurationManager.Load();
        Logger.Log("Configuration loaded");

        // Ensure default password is set if not configured
        if (_config.Exit.RequirePassword && string.IsNullOrEmpty(_config.Exit.PasswordHash))
        {
            _config.Exit.PasswordHash = SecurityHelper.GetDefaultPasswordHash();
            ConfigurationManager.Save(_config);
            Logger.Log("Default exit password set (admin123)");
        }
        
        // Check if video mode is enabled
        if (_config.Kiosk.VideoMode?.Enabled == true)
        {
            _isVideoMode = true;
            Logger.Log($"Video mode enabled - Carescape: {_config.Kiosk.VideoMode.CarescapeVideoPath}");
            Logger.Log($"Demo: {_config.Kiosk.VideoMode.DemoVideoPath}");
            
            // Initialize VideoController for MPV integration
            _videoController = new VideoController(_config.Kiosk.VideoMode);
        }

        // Setup keyboard accelerators for WinUI 3
        SetupKeyboardAccelerators();
        
        // Also setup PreviewKeyDown as backup for keyboard handling
        this.Content.PreviewKeyDown += Window_PreviewKeyDown;
        
        // Hook Activated event - do all initialization there when window is fully ready
        this.Activated += MainWindow_Activated;
    }

    // Debug test button click handler
    private async void DebugTestButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("Debug test button clicked - keyboard accelerators working test");
        ShowStatus("TEST", "Button clicked! Press Ctrl+Shift+I for debug mode, Ctrl+Shift+Q to exit");
        await Task.Delay(3000);
        HideStatus();
    }

    private void SetupKeyboardAccelerators()
    {
        // Debug mode: Ctrl+Shift+I
        var debugAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.I,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
        };
        debugAccelerator.Invoked += async (s, e) =>
        {
            e.Handled = true;
            if (_config.Debug.Enabled)
            {
                Logger.LogSecurityEvent("DebugModeHotkeyPressed", "User pressed Ctrl+Shift+I");
                await ToggleDebugMode();
            }
        };
        ((FrameworkElement)this.Content).KeyboardAccelerators.Add(debugAccelerator);

        // Exit mode: Ctrl+Shift+Q
        var exitAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.Q,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
        };
        exitAccelerator.Invoked += async (s, e) =>
        {
            e.Handled = true;
            if (_config.Exit.Enabled)
            {
                Logger.LogSecurityEvent("ExitHotkeyPressed", "User pressed Ctrl+Shift+Q");
                await HandleExitRequest();
            }
        };
        ((FrameworkElement)this.Content).KeyboardAccelerators.Add(exitAccelerator);

        // Video mode accelerators
        if (_isVideoMode)
        {
            // Flic button: Ctrl+Alt+D
            var flicAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.D,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
            };
            flicAccelerator.Invoked += async (s, e) =>
            {
                e.Handled = true;
                if (_videoController != null)
                {
                    Logger.Log("Flic button pressed (Ctrl+Alt+D) - toggling video");
                    await _videoController.HandleFlicButtonPressAsync();
                }
            };
            ((FrameworkElement)this.Content).KeyboardAccelerators.Add(flicAccelerator);

            // Stop video: Ctrl+Alt+E
            var stopAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.E,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
            };
            stopAccelerator.Invoked += async (s, e) =>
            {
                e.Handled = true;
                if (_videoController != null)
                {
                    Logger.Log("Stop video pressed (Ctrl+Alt+E)");
                    await _videoController.StopAsync();
                }
            };
            ((FrameworkElement)this.Content).KeyboardAccelerators.Add(stopAccelerator);

            // Restart carescape: Ctrl+Alt+R
            var restartAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.R,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
            };
            restartAccelerator.Invoked += async (s, e) =>
            {
                e.Handled = true;
                if (_videoController != null)
                {
                    Logger.Log("Restart carescape pressed (Ctrl+Alt+R)");
                    await _videoController.RestartCarescapeAsync();
                }
            };
            ((FrameworkElement)this.Content).KeyboardAccelerators.Add(restartAccelerator);
        }

        Logger.Log($"Keyboard accelerators registered: {((FrameworkElement)this.Content).KeyboardAccelerators.Count} total");
    }

    // Alternative keyboard handling using PreviewKeyDown
    private async void Window_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            // Simple modifier detection for debugging
            var currentWindow = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            bool ctrlPressed = (currentWindow & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            
            currentWindow = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool shiftPressed = (currentWindow & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            
            currentWindow = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
            bool altPressed = (currentWindow & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            Logger.Log($"PreviewKeyDown: Key={e.Key}, Ctrl={ctrlPressed}, Shift={shiftPressed}, Alt={altPressed}");

        // Debug mode: Ctrl+Shift+I
        if (_config.Debug.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.I)
        {
            e.Handled = true;
            Logger.LogSecurityEvent("DebugModeHotkeyPressed", "User pressed Ctrl+Shift+I (via PreviewKeyDown)");
            await ToggleDebugMode();
        }
        // Exit: Ctrl+Shift+Q
        else if (_config.Exit.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.Q)
        {
            e.Handled = true;
            Logger.LogSecurityEvent("ExitHotkeyPressed", "User pressed Ctrl+Shift+Q (via PreviewKeyDown)");
            await HandleExitRequest();
        }
        // Video controls when in video mode
        else if (_isVideoMode && _videoController != null)
        {
            // Flic button: Ctrl+Alt+D
            if (ctrlPressed && altPressed && e.Key == VirtualKey.D)
            {
                e.Handled = true;
                Logger.Log("Flic button pressed (Ctrl+Alt+D) - toggling video (via PreviewKeyDown)");
                await _videoController.HandleFlicButtonPressAsync();
            }
            // Stop: Ctrl+Alt+E
            else if (ctrlPressed && altPressed && e.Key == VirtualKey.E)
            {
                e.Handled = true;
                Logger.Log("Stop video pressed (Ctrl+Alt+E) (via PreviewKeyDown)");
                await _videoController.StopAsync();
            }
            // Restart: Ctrl+Alt+R
            else if (ctrlPressed && altPressed && e.Key == VirtualKey.R)
            {
                e.Handled = true;
                Logger.Log("Restart carescape pressed (Ctrl+Alt+R) (via PreviewKeyDown)");
                await _videoController.RestartCarescapeAsync();
            }
        }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in PreviewKeyDown: {ex.Message}");
        }
    }

    private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        // Only initialize once on first activation
        if (e.WindowActivationState != WindowActivationState.Deactivated && _appWindow == null)
        {
            Debug.WriteLine("MainWindow_Activated event fired (first activation)");
            Logger.Log("MainWindow.Activated event fired");
            
            // Configure kiosk window after it's activated
            ConfigureAsKioskWindow();
            
            // Initialize WebView2 asynchronously without blocking the Activated event
            _ = InitializeWebViewAsync();
        }
    }

    /// <summary>
    /// Removes system chrome, forces fullscreen, and sets always-on-top.
    /// Uses Win32 APIs via HWND obtained from WinUI 3 window.
    /// </summary>
    private async void ConfigureAsKioskWindow()
    {
        try
        {
            // Add delay to ensure window is fully initialized
            await Task.Delay(100);
            
            _hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            Logger.Log($"ConfigureAsKioskWindow started - HWND: {_hwnd}, WindowId: {windowId}");

            // Get current window styles for debugging
            var originalStyle = GetWindowLong(_hwnd, GWL_STYLE);
            Logger.Log($"Original window style: 0x{originalStyle:X8}");

            // Remove caption/system menu/min/max/resize
            var style = originalStyle;
            style &= ~WS_CAPTION;
            style &= ~WS_THICKFRAME;
            style &= ~WS_MINIMIZEBOX;
            style &= ~WS_MAXIMIZEBOX;
            style &= ~WS_SYSMENU;
            
            var styleResult = SetWindowLong(_hwnd, GWL_STYLE, style);
            Logger.Log($"New window style: 0x{style:X8}, SetWindowLong result: 0x{styleResult:X8}");

            // Get current extended style for debugging
            var originalExStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            Logger.Log($"Original extended style: 0x{originalExStyle:X8}");

            // Make topmost
            var exStyle = originalExStyle;
            exStyle |= WS_EX_TOPMOST;
            
            var exStyleResult = SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            Logger.Log($"New extended style: 0x{exStyle:X8}, SetWindowLong result: 0x{exStyleResult:X8}");

        // Size to full monitor bounds
        if (_appWindow != null)
        {
            // Get all available displays
            var allDisplays = DisplayArea.FindAll();
            Debug.WriteLine($"Found {allDisplays.Count} display(s)");
            Logger.Log($"Found {allDisplays.Count} display(s)");
            
            // Log all displays for reference
            for (int i = 0; i < allDisplays.Count; i++)
            {
                var display = allDisplays[i];
                var dispBounds = display.OuterBounds;
                Debug.WriteLine($"  Display {i}: {dispBounds.Width}x{dispBounds.Height} at ({dispBounds.X}, {dispBounds.Y})");
                Logger.Log($"  Display {i}: {dispBounds.Width}x{dispBounds.Height} at ({dispBounds.X}, {dispBounds.Y})");
            }
            
            // Select target display
            DisplayArea targetDisplay;
            int targetMonitorIndex = _config.Kiosk.TargetMonitorIndex;
            
            Logger.Log($"Target monitor index from config: {targetMonitorIndex} (default is 1)");
            
            if (targetMonitorIndex >= 0 && targetMonitorIndex < allDisplays.Count)
            {
                targetDisplay = allDisplays[targetMonitorIndex];
                Debug.WriteLine($"Using monitor index {targetMonitorIndex}");
                Logger.Log($"Using monitor index {targetMonitorIndex}");
            }
            else
            {
                // Invalid index, fallback to primary
                targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                Debug.WriteLine($"WARNING: Monitor index {targetMonitorIndex} is invalid (only {allDisplays.Count} displays found). Using primary.");
                Logger.Log($"WARNING: Monitor index {targetMonitorIndex} is invalid (only {allDisplays.Count} displays found). Using primary display instead.");
            }
            
            var bounds = targetDisplay.OuterBounds; // Use OuterBounds for true fullscreen
            
            // Set size and position using Win32 API for reliable fullscreen sizing
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                // Store normal window bounds for debug mode
                _normalWindowBounds = bounds;
                
                Logger.Log($"Setting window position - X: {bounds.X}, Y: {bounds.Y}, Width: {bounds.Width}, Height: {bounds.Height}");

                // First, try to use AppWindow presenter for fullscreen
                if (_appWindow != null && _appWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
                {
                    Logger.Log($"Current presenter kind: {_appWindow.Presenter.Kind}, switching to FullScreen");
                    _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                    await Task.Delay(100); // Give time for presenter change
                }

                // Then position window at the display's origin and set its size
                bool posResult = SetWindowPos(_hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, 
                                            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
                
                Logger.Log($"SetWindowPos result: {posResult}, Last Win32 Error: {Marshal.GetLastWin32Error()}");
                
                // Verify window position
                await Task.Delay(100);
                if (GetWindowRect(_hwnd, out RECT actualRect))
                {
                    Logger.Log($"Actual window position after SetWindowPos: X={actualRect.Left}, Y={actualRect.Top}, " +
                              $"Width={actualRect.Right - actualRect.Left}, Height={actualRect.Bottom - actualRect.Top}");
                }
            }
            else
            {
                Debug.WriteLine($"ERROR: Display bounds are invalid: {bounds.Width}x{bounds.Height}");
                Logger.Log($"ERROR: Display bounds are invalid");
            }

            // Prevent closing via shell close messages
            if (_appWindow != null)
            {
                _appWindow.Closing += (_, e) => { e.Cancel = true; };
            }
        }
        else
        {
            Logger.Log("ERROR: _appWindow is null!");
        }

        Logger.Log("ConfigureAsKioskWindow completed");
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR in ConfigureAsKioskWindow: {ex.Message}");
            Logger.Log($"Stack trace: {ex.StackTrace}");
        }
    }

    // Add RECT struct for GetWindowRect
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


    private async Task InitializeWebViewAsync()
    {
        try
        {
            Debug.WriteLine("InitializeWebViewAsync started");
            ShowStatus("Loading kiosk...", "Initializing browser engine (WebView2)");
            
            // Phase 1: Using default WebView2 environment for maximum compatibility
            // Media permissions are handled via PermissionRequested event (most important)
            // Autoplay is handled via script injection after page load

            // Log any initialization exception via the control's event (if supported)
            KioskWebView.CoreWebView2Initialized += (s, e) =>
            {
                if (e.Exception != null)
                {
                    Debug.WriteLine($"CoreWebView2Initialized FAILED: {e.Exception.Message}");
                    Logger.Log($"CoreWebView2Initialized exception: {e.Exception.Message}");
                    ShowStatus("Browser failed to initialize", e.Exception.Message);
                }
                else
                {
                    Debug.WriteLine("CoreWebView2Initialized successfully");
                    Logger.Log("CoreWebView2Initialized successfully");
                }
            };

            Debug.WriteLine("Calling EnsureCoreWebView2Async with 30s timeout...");
            
            // Add timeout to prevent hanging forever
            var initTask = KioskWebView.EnsureCoreWebView2Async().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(initTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Debug.WriteLine("ERROR: WebView2 initialization TIMED OUT after 30 seconds");
                Logger.Log("WebView2 initialization timed out after 30 seconds");
                ShowStatus("Browser initialization timed out", "WebView2 failed to initialize within 30 seconds. Check if WebView2 Runtime is installed.");
                return;
            }
            
            await initTask; // Will throw if initialization failed
            Debug.WriteLine("EnsureCoreWebView2Async completed");
            
            if (KioskWebView.CoreWebView2 != null)
            {
                Debug.WriteLine("CoreWebView2 available, configuring settings...");
                
                var settings = KioskWebView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsStatusBarEnabled = false;
                
                // Phase 1: Additional media-related settings
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                
                Debug.WriteLine("Settings configured");
                Logger.Log("WebView2 settings configured (including media autoplay)");

                // Phase 1: Add automatic permission approval for media devices
                KioskWebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                Debug.WriteLine("PermissionRequested event handler registered");
                Logger.Log("Automatic media permission approval enabled");

                // Navigation event handlers for diagnostics
                KioskWebView.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    Debug.WriteLine($"NavigationStarting: {args.Uri}");
                    Logger.Log($"NavigationStarting: {args.Uri}");
                    ShowStatus("Loading...", args.Uri);
                };

                KioskWebView.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        Debug.WriteLine("NavigationCompleted: SUCCESS");
                        Logger.Log("NavigationCompleted: success");
                        
                        // Phase 1: Inject script to enable autoplay for media elements
                        try
                        {
                            await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
                                (function() {
                                    // Enable autoplay for all media elements
                                    document.querySelectorAll('video, audio').forEach(function(media) {
                                        media.setAttribute('autoplay', '');
                                        media.muted = false;
                                        media.play().catch(e => console.log('Autoplay attempted:', e));
                                    });
                                })();
                            ");
                            Debug.WriteLine("Autoplay script injected successfully");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to inject autoplay script: {ex.Message}");
                        }
                        
                        HideStatus();
                    }
                    else
                    {
                        Debug.WriteLine($"NavigationCompleted: FAILED - HTTP {args.HttpStatusCode}");
                        Logger.Log($"NavigationCompleted: failed - StatusCode={args.HttpStatusCode}");
                        ShowStatus("Failed to load page", $"HTTP status: {args.HttpStatusCode}");
                    }
                };

                // Navigate to default screensaver URL at startup
                // Initialize video mode if enabled, otherwise navigate to default URL
                if (_isVideoMode && _videoController != null)
                {
                    // Hide WebView when in video mode
                    KioskWebView.Visibility = Visibility.Collapsed;
                    
                    // Initialize MPV video controller
                    await _videoController.InitializeAsync();
                    Logger.Log("Video mode initialized with MPV");
                }
                else
                {
                    var defaultUrl = "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default";
                    Debug.WriteLine($"Navigating to: {defaultUrl}");
                    Logger.Log($"Navigating to default URL: {defaultUrl}");
                    
                    KioskWebView.CoreWebView2.Navigate(defaultUrl);
                }
                
                Debug.WriteLine("Navigate() call completed");
            }
            else
            {
                Debug.WriteLine("ERROR: CoreWebView2 is NULL after EnsureCoreWebView2Async!");
                Logger.Log("CoreWebView2 is null after EnsureCoreWebView2Async");
                ShowStatus("Browser not available", "WebView2 CoreWebView2 was not created");
            }
            
            Debug.WriteLine("InitializeWebViewAsync: End of try block");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EXCEPTION in InitializeWebViewAsync: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            Logger.Log($"WebView2 initialization error: {ex.Message}");
            Logger.Log($"Stack trace: {ex.StackTrace}");
            
            ShowStatus("Error initializing browser", $"{ex.GetType().Name}: {ex.Message}");
        }
        
        Debug.WriteLine("InitializeWebViewAsync: METHOD END");
    }

    /// <summary>
    /// Navigates the visible WebView2 to the specified URL on the UI thread.
    /// </summary>
    public void NavigateToUrl(string url)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (KioskWebView?.CoreWebView2 != null && !string.IsNullOrWhiteSpace(url))
            {
                Logger.Log($"NavigateToUrl called: {url}");
                ShowStatus("Loading...", url);
                KioskWebView.CoreWebView2.Navigate(url);
            }
        });
    }

    /// <summary>
    /// Phase 1: Handles permission requests from web content.
    /// Automatically approves critical media permissions (microphone, camera) for kiosk mode.
    /// This eliminates user prompts and enables seamless media functionality.
    /// </summary>
    private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Auto-approve critical media permissions for kiosk operation
        switch (e.PermissionKind)
        {
            case CoreWebView2PermissionKind.Microphone:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved MICROPHONE permission for: {e.Uri}");
                Logger.Log($"Auto-approved microphone access for: {e.Uri}");
                break;

            case CoreWebView2PermissionKind.Camera:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved CAMERA permission for: {e.Uri}");
                Logger.Log($"Auto-approved camera access for: {e.Uri}");
                break;

            case CoreWebView2PermissionKind.Geolocation:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved GEOLOCATION permission for: {e.Uri}");
                Logger.Log($"Auto-approved geolocation for: {e.Uri}");
                break;

            case CoreWebView2PermissionKind.Notifications:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved NOTIFICATIONS permission for: {e.Uri}");
                Logger.Log($"Auto-approved notifications for: {e.Uri}");
                break;

            case CoreWebView2PermissionKind.OtherSensors:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved OTHER SENSORS permission for: {e.Uri}");
                Logger.Log($"Auto-approved other sensors for: {e.Uri}");
                break;

            case CoreWebView2PermissionKind.ClipboardRead:
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved CLIPBOARD READ permission for: {e.Uri}");
                Logger.Log($"Auto-approved clipboard read for: {e.Uri}");
                break;

            // Deny potentially dangerous permissions
            case CoreWebView2PermissionKind.MultipleAutomaticDownloads:
                e.State = CoreWebView2PermissionState.Deny;
                Debug.WriteLine($"Denied MULTIPLE DOWNLOADS permission for: {e.Uri}");
                Logger.Log($"Denied multiple downloads for: {e.Uri}");
                break;

            default:
                // For any other permission types, allow them for maximum compatibility
                e.State = CoreWebView2PermissionState.Allow;
                Debug.WriteLine($"Auto-approved permission {e.PermissionKind} for: {e.Uri}");
                Logger.Log($"Auto-approved {e.PermissionKind} for: {e.Uri}");
                break;
        }

        // Persist the decision so the user isn't prompted again for the same site
        e.SavesInProfile = true;
    }

    private void ShowStatus(string title, string? detail = null)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusTitle.Text = title;
            StatusDetail.Text = detail ?? string.Empty;
            StatusOverlay.Visibility = Visibility.Visible;
        });
    }

    private void HideStatus()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
        });
    }

    #region Debug Mode and Exit Mechanism

    /// <summary>
    /// Toggles debug mode on/off.
    /// </summary>
    private async Task ToggleDebugMode()
    {
        _isDebugMode = !_isDebugMode;

        if (_isDebugMode)
        {
            await EnterDebugMode();
        }
        else
        {
            await ExitDebugMode();
        }
    }

    /// <summary>
    /// Enters debug mode: windows the application and enables developer tools.
    /// </summary>
    private async Task EnterDebugMode()
    {
        Logger.LogSecurityEvent("EnterDebugMode", "Entering debug mode");
        ShowStatus("DEBUG MODE", "Developer tools enabled. Press Ctrl+Shift+F12 to exit.");

        await Task.Run(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // 1. Enable WebView2 developer features
                    if (KioskWebView?.CoreWebView2?.Settings != null)
                    {
                        var settings = KioskWebView.CoreWebView2.Settings;
                        settings.AreDevToolsEnabled = true;
                        settings.AreDefaultContextMenusEnabled = true;
                        settings.AreBrowserAcceleratorKeysEnabled = true;
                        Logger.Log("WebView2 developer features enabled");

                        // Auto-open dev tools if configured
                        if (_config.Debug.AutoOpenDevTools)
                        {
                            KioskWebView.CoreWebView2.OpenDevToolsWindow();
                        }
                    }

                    // 2. Calculate debug window size (80% of screen by default)
                    var debugWidth = (int)(_normalWindowBounds.Width * (_config.Debug.WindowSizePercent / 100.0));
                    var debugHeight = (int)(_normalWindowBounds.Height * (_config.Debug.WindowSizePercent / 100.0));
                    var debugX = _normalWindowBounds.X + (_normalWindowBounds.Width - debugWidth) / 2;
                    var debugY = _normalWindowBounds.Y + (_normalWindowBounds.Height - debugHeight) / 2;

                    // 3. Remove window styles (make it resizable and not topmost)
                    var style = GetWindowLong(_hwnd, GWL_STYLE);
                    style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
                    SetWindowLong(_hwnd, GWL_STYLE, style);

                    var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_TOPMOST;
                    SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                    // 4. Resize and reposition window
                    SetWindowPos(_hwnd, IntPtr.Zero, debugX, debugY, debugWidth, debugHeight, SWP_SHOWWINDOW | SWP_FRAMECHANGED);

                    // 5. Update window title
                    this.Title = "[DEBUG] OneRoom Health Kiosk";

                    Logger.Log($"Debug mode active: Window resized to {debugWidth}x{debugHeight}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error entering debug mode: {ex.Message}");
                    ShowStatus("Debug Mode Error", ex.Message);
                }
            });
        });
    }

    /// <summary>
    /// Exits debug mode: returns to fullscreen kiosk mode.
    /// </summary>
    private async Task ExitDebugMode()
    {
        Logger.LogSecurityEvent("ExitDebugMode", "Exiting debug mode");

        await Task.Run(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // 1. Disable WebView2 developer features
                    if (KioskWebView?.CoreWebView2?.Settings != null)
                    {
                        var settings = KioskWebView.CoreWebView2.Settings;
                        settings.AreDevToolsEnabled = false;
                        settings.AreDefaultContextMenusEnabled = false;
                        settings.AreBrowserAcceleratorKeysEnabled = false;

                        // Close developer tools if open
                        try
                        {
                            _ = KioskWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Browser.close", "{}");
                        }
                        catch
                        {
                            // Ignore errors closing dev tools
                        }

                        Logger.Log("WebView2 developer features disabled");
                    }

                    // 2. Restore kiosk window configuration
                    ConfigureAsKioskWindow();

                    // 3. Update window title
                    this.Title = "OneRoom Health Kiosk";

                    Logger.Log("Debug mode exited, returned to kiosk mode");
                    HideStatus();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error exiting debug mode: {ex.Message}");
                }
            });
        });
    }

    /// <summary>
    /// Handles exit request by showing password dialog if required.
    /// </summary>
    private async Task HandleExitRequest()
    {
        Logger.LogSecurityEvent("ExitRequested", "Exit request initiated");

        try
        {
            if (_config.Exit.RequirePassword)
            {
                // Create password dialog
                var passwordBox = new PasswordBox
                {
                    PlaceholderText = "Enter administrator password",
                    Width = 300
                };

                var dialog = new ContentDialog
                {
                    Title = "Exit Kiosk Mode",
                    Content = passwordBox,
                    PrimaryButtonText = "Exit",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    var password = passwordBox.Password;

                    if (SecurityHelper.ValidatePassword(password, _config.Exit.PasswordHash))
                    {
                        Logger.LogSecurityEvent("ExitPasswordValid", "Correct password provided, exiting kiosk");
                        await PerformKioskExit();
                    }
                    else
                    {
                        Logger.LogSecurityEvent("ExitPasswordInvalid", "Invalid password attempt");

                        var errorDialog = new ContentDialog
                        {
                            Title = "Access Denied",
                            Content = "Invalid password. Exit request denied.",
                            CloseButtonText = "OK",
                            XamlRoot = this.Content.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                    }
                }
                else
                {
                    Logger.LogSecurityEvent("ExitCancelled", "Exit request cancelled by user");
                }
            }
            else
            {
                // No password required, exit immediately
                await PerformKioskExit();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling exit request: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs the actual kiosk exit process.
    /// </summary>
    private async Task PerformKioskExit()
    {
        Logger.LogSecurityEvent("KioskExiting", "Performing kiosk exit");

        try
        {
            // 1. Show exit message
            ShowStatus("EXITING", "Shutting down kiosk mode...");

            await Task.Delay(1000); // Brief delay to show message

            // 2. Clean up WebView2
            if (KioskWebView != null)
            {
                try
                {
                    KioskWebView.Close();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // 3. Stop HTTP server
            LocalCommandServer.Stop();

            // 4. Log exit
            Logger.Log("Kiosk application exiting normally");

            // 5. For Shell Launcher v2, start Explorer for the current user
            if (IsRunningInKioskMode())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                    Logger.Log("Explorer.exe started for user");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to start Explorer: {ex.Message}");
                }
            }

            // 6. Close application
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during exit: {ex.Message}");
            // Force exit
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Checks if the application is running as the Windows shell (Shell Launcher v2).
    /// </summary>
    private bool IsRunningInKioskMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
            var shell = key?.GetValue("Shell") as string;
            return shell?.Contains("OneRoomHealthKiosk", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

