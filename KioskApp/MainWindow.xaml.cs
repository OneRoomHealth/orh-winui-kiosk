using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace KioskApp;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, int uType);

    public MainWindow()
    {
        MessageBoxW(IntPtr.Zero, "MainWindow constructor START", "Debug", 0);
        InitializeComponent();
        MessageBoxW(IntPtr.Zero, "InitializeComponent done", "Debug", 0);
        
        // Hook Activated event - do all initialization there when window is fully ready
        this.Activated += MainWindow_Activated;
        MessageBoxW(IntPtr.Zero, "MainWindow constructor END", "Debug", 0);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        // Only initialize once on first activation
        if (e.WindowActivationState != WindowActivationState.Deactivated && _appWindow == null)
        {
            Debug.WriteLine("MainWindow_Activated event fired (first activation)");
            MessageBoxW(IntPtr.Zero, "Activated event fired", "Debug", 0);
            Logger.Log("MainWindow.Activated event fired");
            
            // Configure kiosk window after it's activated
            MessageBoxW(IntPtr.Zero, "About to call ConfigureAsKioskWindow", "Debug", 0);
            ConfigureAsKioskWindow();
            MessageBoxW(IntPtr.Zero, "ConfigureAsKioskWindow completed", "Debug", 0);
            
            // Initialize WebView2 asynchronously without blocking the Activated event
            MessageBoxW(IntPtr.Zero, "About to initialize WebView2", "Debug", 0);
            _ = InitializeWebViewAsync();
        }
    }

    /// <summary>
    /// Removes system chrome, forces fullscreen, and sets always-on-top.
    /// Uses Win32 APIs via HWND obtained from WinUI 3 window.
    /// </summary>
    private void ConfigureAsKioskWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove caption/system menu/min/max/resize
        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style &= ~WS_MINIMIZEBOX;
        style &= ~WS_MAXIMIZEBOX;
        style &= ~WS_SYSMENU;
        SetWindowLong(hwnd, GWL_STYLE, style);

        // Make topmost
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        // Size to full monitor bounds
        if (_appWindow != null)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var bounds = displayArea.WorkArea;
            
            // Debug: Check what bounds we're getting
            MessageBoxW(IntPtr.Zero, 
                $"Display bounds detected:\n" +
                $"X: {bounds.X}, Y: {bounds.Y}\n" +
                $"Width: {bounds.Width}, Height: {bounds.Height}", 
                "Display Bounds", 0);
            
            // Set size FIRST using Win32 API before setting presenter
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                // Use Win32 SetWindowPos for more reliable sizing
                int width = bounds.Width;
                int height = bounds.Height;
                
                // Position at origin and set size
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_SHOWWINDOW);
                
                // Check size immediately after SetWindowPos
                var sizeAfterSetPos = _appWindow.Size;
                MessageBoxW(IntPtr.Zero, 
                    $"Called SetWindowPos({width}x{height})\n" +
                    $"AppWindow.Size immediately after: {sizeAfterSetPos.Width}x{sizeAfterSetPos.Height}", 
                    "After SetWindowPos", 0);
            }
            else
            {
                // Fallback to primary display's full bounds
                var outerBounds = displayArea.OuterBounds;
                MessageBoxW(IntPtr.Zero, 
                    $"WorkArea was empty, using OuterBounds:\n" +
                    $"Width: {outerBounds.Width}, Height: {outerBounds.Height}", 
                    "Fallback Bounds", 0);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, outerBounds.Width, outerBounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
            }
            
            // DON'T set FullScreen presenter - it's overriding our size!
            // Instead, just maximize the window
            // _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

            // Prevent closing via shell close messages
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }

        // Ensure window style changes are applied and shown (using SWP_NOSIZE to keep current size)
        const uint SWP_NOSIZE = 0x0001;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOSIZE | SWP_NOMOVE);
        
        // Verify window configuration
        if (_appWindow != null)
        {
            var size = _appWindow.Size;
            var position = _appWindow.Position;
            var presenter = _appWindow.Presenter;
            string windowInfo = $"Window configured:\n" +
                              $"Size: {size.Width}x{size.Height}\n" +
                              $"Position: ({position.X}, {position.Y})\n" +
                              $"Presenter: {presenter?.Kind}";
            MessageBoxW(IntPtr.Zero, windowInfo, "Window Config", 0);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Debug.WriteLine("InitializeWebViewAsync started");
            MessageBoxW(IntPtr.Zero, "InitializeWebViewAsync started", "Debug", 0);
            ShowStatus("Loading kiosk...", "Initializing browser engine (WebView2)");
            MessageBoxW(IntPtr.Zero, "ShowStatus called", "Debug", 0);

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
            MessageBoxW(IntPtr.Zero, "About to call EnsureCoreWebView2Async", "Debug", 0);
            
            // Add timeout to prevent hanging forever
            var initTask = KioskWebView.EnsureCoreWebView2Async().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(initTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Debug.WriteLine("ERROR: WebView2 initialization TIMED OUT after 30 seconds");
                Logger.Log("WebView2 initialization timed out after 30 seconds");
                MessageBoxW(IntPtr.Zero, "WebView2 TIMEOUT after 30s!\nCheck WebView2 Runtime", "ERROR", 0);
                ShowStatus("Browser initialization timed out", "WebView2 failed to initialize within 30 seconds. Check if WebView2 Runtime is installed.");
                return;
            }
            
            await initTask; // Will throw if initialization failed
            Debug.WriteLine("EnsureCoreWebView2Async completed");
            MessageBoxW(IntPtr.Zero, "EnsureCoreWebView2Async completed", "Debug", 0);

            MessageBoxW(IntPtr.Zero, "About to check if CoreWebView2 != null", "Debug", 0);
            Debug.WriteLine("About to check CoreWebView2 != null");
            
            if (KioskWebView.CoreWebView2 != null)
            {
                Debug.WriteLine("CoreWebView2 available, configuring settings...");
                MessageBoxW(IntPtr.Zero, "CoreWebView2 != null, configuring settings", "Debug", 0);
                
                var settings = KioskWebView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsStatusBarEnabled = false;
                Debug.WriteLine("Settings configured");
                MessageBoxW(IntPtr.Zero, "Settings configured", "Debug", 0);

                // Navigation event handlers for diagnostics
                Debug.WriteLine("Adding NavigationStarting handler");
                MessageBoxW(IntPtr.Zero, "About to add NavigationStarting handler", "Debug", 0);
                
                KioskWebView.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    MessageBoxW(IntPtr.Zero, $"NavigationStarting EVENT FIRED!\n{args.Uri}", "Navigation Event", 0);
                    Debug.WriteLine($"NavigationStarting: {args.Uri}");
                    Logger.Log($"NavigationStarting: {args.Uri}");
                    ShowStatus("Loading...", args.Uri);
                };
                
                MessageBoxW(IntPtr.Zero, "NavigationStarting handler added", "Debug", 0);
                Debug.WriteLine("Adding NavigationCompleted handler");

                KioskWebView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        MessageBoxW(IntPtr.Zero, "NavigationCompleted: SUCCESS!", "Navigation Event", 0);
                        Debug.WriteLine("NavigationCompleted: SUCCESS");
                        Logger.Log("NavigationCompleted: success");
                        HideStatus();
                        
                        // Check if overlay is actually hidden
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            var overlayAfterHide = StatusOverlay.Visibility;
                            MessageBoxW(IntPtr.Zero, $"HideStatus() called.\nStatusOverlay now: {overlayAfterHide}", "After HideStatus", 0);
                        });
                    }
                    else
                    {
                        MessageBoxW(IntPtr.Zero, $"NavigationCompleted: FAILED\nHTTP {args.HttpStatusCode}", "Navigation Event", 0x00000010);
                        Debug.WriteLine($"NavigationCompleted: FAILED - HTTP {args.HttpStatusCode}");
                        Logger.Log($"NavigationCompleted: failed - StatusCode={args.HttpStatusCode}");
                        ShowStatus("Failed to load page", $"HTTP status: {args.HttpStatusCode}");
                    }
                };
                
                MessageBoxW(IntPtr.Zero, "NavigationCompleted handler added", "Debug", 0);

                // Navigate to default screensaver URL at startup
                var defaultUrl = "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default";
                Debug.WriteLine($"Navigating to: {defaultUrl}");
                Logger.Log($"Navigating to default URL: {defaultUrl}");
                MessageBoxW(IntPtr.Zero, $"About to Navigate to:\n{defaultUrl}", "Debug", 0);
                
                KioskWebView.CoreWebView2.Navigate(defaultUrl);
                
                MessageBoxW(IntPtr.Zero, "Navigate() call completed", "Debug", 0);
                Debug.WriteLine("Navigate() call completed");
                
                // Check visibility and size
                var webViewVisibility = KioskWebView.Visibility;
                var overlayVisibility = StatusOverlay.Visibility;
                var webViewWidth = KioskWebView.ActualWidth;
                var webViewHeight = KioskWebView.ActualHeight;
                
                string diagnostics = $"WebView2 Visibility: {webViewVisibility}\n" +
                                   $"WebView2 Size: {webViewWidth}x{webViewHeight}\n" +
                                   $"StatusOverlay: {overlayVisibility}\n" +
                                   $"Window AppWindow: {(_appWindow != null ? "Created" : "NULL")}";
                
                MessageBoxW(IntPtr.Zero, diagnostics, "Visibility & Size Check", 0);
            }
            else
            {
                Debug.WriteLine("ERROR: CoreWebView2 is NULL after EnsureCoreWebView2Async!");
                Logger.Log("CoreWebView2 is null after EnsureCoreWebView2Async");
                MessageBoxW(IntPtr.Zero, "ERROR: CoreWebView2 is NULL!", "Error", 0);
                ShowStatus("Browser not available", "WebView2 CoreWebView2 was not created");
            }
            
            MessageBoxW(IntPtr.Zero, "InitializeWebViewAsync: End of try block", "Debug", 0);
            Debug.WriteLine("InitializeWebViewAsync: End of try block");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EXCEPTION in InitializeWebViewAsync: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            Logger.Log($"WebView2 initialization error: {ex.Message}");
            Logger.Log($"Stack trace: {ex.StackTrace}");
            
            string errorMsg = $"EXCEPTION in InitializeWebViewAsync:\n\n{ex.GetType().Name}\n{ex.Message}\n\nStack:\n{ex.StackTrace}";
            MessageBoxW(IntPtr.Zero, errorMsg, "WebView2 Error", 0x00000010);
            
            ShowStatus("Error initializing browser", $"{ex.GetType().Name}: {ex.Message}");
        }
        
        MessageBoxW(IntPtr.Zero, "InitializeWebViewAsync: METHOD END", "Debug", 0);
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
}

