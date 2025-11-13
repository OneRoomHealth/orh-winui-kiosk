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
    private bool _isDemoPlaying = false;
    private string _videoPlayerHtml = string.Empty;

    // Configuration: Set target monitor index (0 = first monitor, 1 = second monitor, etc.)
    // Set to -1 to use primary monitor automatically
    private const int TARGET_MONITOR_INDEX = 1; // Use second monitor (index 1)

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

    [DllImport("user32.dll")]
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
            
            // Load video player HTML
            LoadVideoPlayerHtml();
        }

        // Register keyboard event handler for hotkeys
        this.KeyDown += OnKeyDown;

        // Hook Activated event - do all initialization there when window is fully ready
        this.Activated += MainWindow_Activated;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
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
    private void ConfigureAsKioskWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove caption/system menu/min/max/resize
        var style = GetWindowLong(_hwnd, GWL_STYLE);
        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style &= ~WS_MINIMIZEBOX;
        style &= ~WS_MAXIMIZEBOX;
        style &= ~WS_SYSMENU;
        SetWindowLong(_hwnd, GWL_STYLE, style);

        // Make topmost
        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOPMOST;
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

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
            DisplayArea? targetDisplay = null;
            if (TARGET_MONITOR_INDEX >= 0 && TARGET_MONITOR_INDEX < allDisplays.Count)
            {
                targetDisplay = allDisplays[TARGET_MONITOR_INDEX];
                Debug.WriteLine($"Using configured monitor index {TARGET_MONITOR_INDEX}");
                Logger.Log($"Using configured monitor index {TARGET_MONITOR_INDEX}");
            }
            else if (TARGET_MONITOR_INDEX == -1)
            {
                // Use primary display
                targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                Debug.WriteLine("Using primary monitor");
                Logger.Log("Using primary monitor");
            }
            else
            {
                // Invalid index, fallback to primary
                targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                Debug.WriteLine($"WARNING: Monitor index {TARGET_MONITOR_INDEX} is invalid (only {allDisplays.Count} displays found). Using primary.");
                Logger.Log($"WARNING: Monitor index {TARGET_MONITOR_INDEX} is invalid. Using primary.");
            }
            
            var bounds = targetDisplay.OuterBounds; // Use OuterBounds for true fullscreen
            
            // Set size and position using Win32 API for reliable fullscreen sizing
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                // Store normal window bounds for debug mode
                _normalWindowBounds = bounds;

                // Position window at the display's origin and set its size
                SetWindowPos(_hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
                Debug.WriteLine($"Window positioned at ({bounds.X}, {bounds.Y}) with size {bounds.Width}x{bounds.Height}");
                Logger.Log($"Window positioned at ({bounds.X}, {bounds.Y}) with size {bounds.Width}x{bounds.Height}");
            }
            else
            {
                Debug.WriteLine($"ERROR: Display bounds are invalid: {bounds.Width}x{bounds.Height}");
                Logger.Log($"ERROR: Display bounds are invalid");
            }

            // Prevent closing via shell close messages
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }

        // Ensure window style changes are applied and shown (using SWP_NOSIZE to keep current size)
        const uint SWP_NOSIZE = 0x0001;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOSIZE | SWP_NOMOVE);
        
        Debug.WriteLine("ConfigureAsKioskWindow completed");
        Logger.Log("ConfigureAsKioskWindow completed");
    }


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
                if (_isVideoMode)
                {
                    await InitializeVideoMode();
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
    /// Keyboard event handler for hotkey detection.
    /// Ctrl+Shift+F12: Toggle debug mode
    /// Ctrl+Shift+Escape: Exit kiosk mode
    /// </summary>
    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Get modifier key states
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);

        bool ctrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        bool shiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        // Debug mode hotkey: Ctrl+Shift+F12
        if (_config.Debug.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.F12)
        {
            Logger.LogSecurityEvent("DebugModeHotkeyPressed", "User pressed Ctrl+Shift+F12");
            await ToggleDebugMode();
            e.Handled = true;
        }

        // Exit hotkey: Ctrl+Shift+Escape
        if (_config.Exit.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.Escape)
        {
            Logger.LogSecurityEvent("ExitHotkeyPressed", "User pressed Ctrl+Shift+Escape");
            await HandleExitRequest();
            e.Handled = true;
        }
        
        // Video control hotkeys (when video mode is enabled)
        if (_isVideoMode)
        {
            var altState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
            bool altPressed = (altState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            
            // Ctrl+Alt+D - Toggle video (Flic button)
            if (ctrlPressed && altPressed && e.Key == VirtualKey.D)
            {
                Logger.Log("Flic button pressed (Ctrl+Alt+D) - toggling video");
                await ToggleVideoAsync();
                e.Handled = true;
            }
            // Ctrl+Alt+E - Stop video
            else if (ctrlPressed && altPressed && e.Key == VirtualKey.E)
            {
                Logger.Log("Stop video pressed (Ctrl+Alt+E)");
                await StopVideoAsync();
                e.Handled = true;
            }
            // Ctrl+Alt+R - Restart carescape video
            else if (ctrlPressed && altPressed && e.Key == VirtualKey.R)
            {
                Logger.Log("Restart carescape pressed (Ctrl+Alt+R)");
                await RestartCarescapeVideoAsync();
                e.Handled = true;
            }
        }
    }

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
                            KioskWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Browser.close", "{}");
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
    
    #region Video Mode Methods
    
    /// <summary>
    /// Loads the video player HTML from embedded resources
    /// </summary>
    private void LoadVideoPlayerHtml()
    {
        try
        {
            var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "simple-video-player.html");
            if (File.Exists(resourcePath))
            {
                _videoPlayerHtml = File.ReadAllText(resourcePath);
                Logger.Log("Video player HTML loaded from file");
            }
            else
            {
                // Use embedded HTML as fallback
                _videoPlayerHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Kiosk Video Player</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { background: #000; overflow: hidden; font-family: Arial, sans-serif; }
        video { width: 100vw; height: 100vh; object-fit: fill; }
        #status { position: fixed; top: 20px; left: 50%; transform: translateX(-50%); 
                  background: rgba(0,0,0,0.8); color: white; padding: 10px 20px; 
                  border-radius: 5px; font-size: 18px; display: none; z-index: 1000; }
    </style>
</head>
<body>
    <video id=""videoPlayer"" autoplay></video>
    <div id=""status""></div>
    <script>
        class FlicVideoPlayer {
            constructor() {
                this.player = document.getElementById('videoPlayer');
                this.statusEl = document.getElementById('status');
                this.videos = { carescape: { path: '', volume: 0.5, loop: true },
                               demo: { path: '', volume: 0.75, loop: false } };
                this.currentVideo = 'carescape';
                
                this.player.addEventListener('ended', () => {
                    if (this.currentVideo === 'demo') {
                        this.showStatus('Demo finished, returning to Carescape');
                        setTimeout(() => { this.playVideo('carescape'); }, 1000);
                    }
                });
                
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.addEventListener('message', (event) => {
                        this.handleCommand(event.data);
                    });
                }
            }
            
            handleCommand(cmd) {
                switch(cmd.action) {
                    case 'init': this.initialize(cmd); break;
                    case 'toggle': this.toggle(); break;
                    case 'stop': this.stop(); break;
                    case 'restart': this.playVideo('carescape'); break;
                }
            }
            
            initialize(config) {
                if (config.carescapeVideo) this.videos.carescape.path = config.carescapeVideo;
                if (config.demoVideo) this.videos.demo.path = config.demoVideo;
                if (config.carescapeVolume !== undefined) this.videos.carescape.volume = config.carescapeVolume / 100;
                if (config.demoVolume !== undefined) this.videos.demo.volume = config.demoVolume / 100;
                this.playVideo('carescape');
            }
            
            playVideo(videoName) {
                const video = this.videos[videoName];
                if (!video || !video.path) return;
                this.currentVideo = videoName;
                this.player.src = video.path;
                this.player.volume = video.volume;
                this.player.loop = video.loop;
                this.player.play().then(() => {
                    this.showStatus('Playing: ' + videoName);
                }).catch(error => {
                    console.error('Playback failed:', error);
                });
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({
                        event: 'videoChanged', video: videoName
                    });
                }
            }
            
            toggle() {
                const nextVideo = this.currentVideo === 'carescape' ? 'demo' : 'carescape';
                this.playVideo(nextVideo);
            }
            
            stop() {
                this.player.pause();
                this.player.src = '';
                this.showStatus('Stopped');
            }
            
            showStatus(message) {
                this.statusEl.textContent = message;
                this.statusEl.style.display = 'block';
                clearTimeout(this.statusTimeout);
                this.statusTimeout = setTimeout(() => {
                    this.statusEl.style.display = 'none';
                }, 3000);
            }
        }
        const player = new FlicVideoPlayer();
    </script>
</body>
</html>";
                Logger.Log("Using embedded video player HTML");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading video player HTML: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Initialize video mode if enabled
    /// </summary>
    private async Task InitializeVideoMode()
    {
        if (!_isVideoMode || string.IsNullOrEmpty(_videoPlayerHtml))
        {
            return;
        }
        
        try
        {
            Logger.Log("Initializing video mode...");
            
            // Navigate to video player HTML
            var dataUri = $"data:text/html;charset=utf-8,{Uri.EscapeDataString(_videoPlayerHtml)}";
            KioskWebView.CoreWebView2.Navigate(dataUri);
            
            // Wait for navigation to complete
            await Task.Delay(1000);
            
            // Initialize video player with configuration
            var initCommand = new
            {
                action = "init",
                carescapeVideo = ConvertToFileUrl(_config.Kiosk.VideoMode.CarescapeVideoPath),
                demoVideo = ConvertToFileUrl(_config.Kiosk.VideoMode.DemoVideoPath),
                carescapeVolume = _config.Kiosk.VideoMode.CarescapeVolume,
                demoVolume = _config.Kiosk.VideoMode.DemoVolume
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(initCommand);
            await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(json);
            
            // Listen for messages from the video player
            KioskWebView.CoreWebView2.WebMessageReceived += VideoPlayer_WebMessageReceived;
            
            Logger.Log("Video mode initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error initializing video mode: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Convert file path to file:// URL
    /// </summary>
    private string ConvertToFileUrl(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;
            
        // Ensure absolute path
        if (!Path.IsPathRooted(filePath))
            filePath = Path.GetFullPath(filePath);
            
        // Convert to file URL
        return new Uri(filePath).AbsoluteUri;
    }
    
    /// <summary>
    /// Handle messages from the video player
    /// </summary>
    private void VideoPlayer_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            Logger.Log($"Video player message: {message}");
            
            // Parse and handle video player events if needed
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            if (data != null && data.ContainsKey("event"))
            {
                var eventName = data["event"].ToString();
                switch (eventName)
                {
                    case "videoChanged":
                        var videoName = data.ContainsKey("video") ? data["video"].ToString() : "unknown";
                        _isDemoPlaying = videoName == "demo";
                        Logger.Log($"Video changed to: {videoName}");
                        break;
                        
                    case "videoStopped":
                        Logger.Log("Video playback stopped");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling video player message: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Toggle between carescape and demo videos
    /// </summary>
    private async Task ToggleVideoAsync()
    {
        try
        {
            var command = new { action = "toggle" };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error toggling video: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Stop video playback
    /// </summary>
    private async Task StopVideoAsync()
    {
        try
        {
            var command = new { action = "stop" };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error stopping video: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Restart carescape video
    /// </summary>
    private async Task RestartCarescapeVideoAsync()
    {
        try
        {
            var command = new { action = "restart" };
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(json);
            _isDemoPlaying = false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error restarting carescape video: {ex.Message}");
        }
    }
    
    #endregion

    #endregion
}

