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

    public MainWindow()
    {
        InitializeComponent();
        ConfigureAsKioskWindow();
        InitializeWebView();
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
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

            // Prevent closing via shell close messages
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }

        // Ensure window style changes are applied and shown
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER);
    }

    private async void InitializeWebView()
    {
        try
        {
            ShowStatus("Loading kiosk...", "Initializing browser engine (WebView2)");

            // Log any initialization exception via the control's event (if supported)
            KioskWebView.CoreWebView2Initialized += (s, e) =>
            {
                if (e.Exception != null)
                {
                    Logger.Log($"CoreWebView2Initialized exception: {e.Exception.Message}");
                    ShowStatus("Browser failed to initialize", e.Exception.Message);
                }
                else
                {
                    Logger.Log("CoreWebView2Initialized successfully");
                }
            };

            // Use the simplest initialization - no custom environment needed for packaged apps
            await KioskWebView.EnsureCoreWebView2Async();

            if (KioskWebView.CoreWebView2 != null)
            {
                var settings = KioskWebView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsStatusBarEnabled = false;

                // Navigation event handlers for diagnostics
                KioskWebView.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    Logger.Log($"NavigationStarting: {args.Uri}");
                    ShowStatus("Loading...", args.Uri);
                };

                KioskWebView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        Logger.Log("NavigationCompleted: success");
                        HideStatus();
                    }
                    else
                    {
                        Logger.Log($"NavigationCompleted: failed - StatusCode={args.HttpStatusCode}");
                        ShowStatus("Failed to load page", $"HTTP status: {args.HttpStatusCode}");
                    }
                };

                // Navigate to default screensaver URL at startup
                var defaultUrl = "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default";
                Logger.Log($"Navigating to default URL: {defaultUrl}");
                KioskWebView.CoreWebView2.Navigate(defaultUrl);
            }
            else
            {
                Logger.Log("CoreWebView2 is null after EnsureCoreWebView2Async");
                ShowStatus("Browser not available", "WebView2 CoreWebView2 was not created");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"WebView2 initialization error: {ex.Message}");
            ShowStatus("Error initializing browser", ex.Message);
        }
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
        StatusTitle.Text = title;
        StatusDetail.Text = detail ?? string.Empty;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusOverlay.Visibility = Visibility.Collapsed;
    }
}

