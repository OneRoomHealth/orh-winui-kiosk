using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
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
        }

        // Ensure window style changes are applied and shown
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER);

        // Optional: prevent user close; kiosk is closed via Ctrl+Alt+Del
        if (_appWindow != null)
        {
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }
    }

    private async void InitializeWebView()
    {
        try
        {
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

                // Navigate to default screensaver URL at startup
                KioskWebView.CoreWebView2.Navigate("https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
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
                KioskWebView.CoreWebView2.Navigate(url);
            }
        });
    }
}

