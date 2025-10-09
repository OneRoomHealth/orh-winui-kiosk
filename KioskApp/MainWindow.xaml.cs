using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.System;
using WinRT.Interop;

namespace KioskApp;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private readonly List<DateTime> _tapTimestamps = new();
    private const int REQUIRED_TAPS = 5;
    private const int TAP_WINDOW_SECONDS = 3;

    // P/Invoke for blocking keyboard shortcuts
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    public MainWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        InitializeWebView();
    }

    private void InitializeWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            // Hide title bar
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Get display area for full screen
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    workArea.X, workArea.Y, workArea.Width, workArea.Height));
            }

            // Set to fullscreen presenter (kiosk mode)
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        // Block common keyboard shortcuts
        BlockKeyboardShortcuts(hwnd);

        // Prevent window closing via keyboard
        this.Closed += MainWindow_Closed;
    }

    private void BlockKeyboardShortcuts(IntPtr hwnd)
    {
        // Block Ctrl+N (New Window)
        RegisterHotKey(hwnd, 1, MOD_CONTROL, 0x4E);
        // Block Ctrl+T (New Tab)
        RegisterHotKey(hwnd, 2, MOD_CONTROL, 0x54);
        // Block Ctrl+W (Close Tab/Window)
        RegisterHotKey(hwnd, 3, MOD_CONTROL, 0x57);
        // Block Alt+F4 (Close Window)
        RegisterHotKey(hwnd, 4, MOD_ALT, 0x73);
        // Block Ctrl+Shift+N (Incognito)
        RegisterHotKey(hwnd, 5, MOD_CONTROL | MOD_SHIFT, 0x4E);
        // Block F11 (Fullscreen toggle)
        RegisterHotKey(hwnd, 6, 0, 0x7A);
        // Block Windows Key
        RegisterHotKey(hwnd, 7, MOD_WIN, 0x00);
        // Block Alt+Tab (disabled in kiosk mode typically)
        RegisterHotKey(hwnd, 8, MOD_ALT, 0x09);
        // Block Ctrl+Alt+Delete (system handles this, but we try)
        RegisterHotKey(hwnd, 9, MOD_CONTROL | MOD_ALT, 0x2E);
        // Block F12 (DevTools)
        RegisterHotKey(hwnd, 10, 0, 0x7B);
        // Block Ctrl+Shift+I (DevTools)
        RegisterHotKey(hwnd, 11, MOD_CONTROL | MOD_SHIFT, 0x49);
        // Block Ctrl+L (Address bar)
        RegisterHotKey(hwnd, 12, MOD_CONTROL, 0x4C);
        // Block Ctrl+O (Open file)
        RegisterHotKey(hwnd, 13, MOD_CONTROL, 0x4F);
        // Block Ctrl+P (Print)
        RegisterHotKey(hwnd, 14, MOD_CONTROL, 0x50);
        // Block Ctrl+S (Save)
        RegisterHotKey(hwnd, 15, MOD_CONTROL, 0x53);
    }

    private async void InitializeWebView()
    {
        try
        {
            await KioskWebView.EnsureCoreWebView2Async();

            if (KioskWebView.CoreWebView2 != null)
            {
                // Disable context menu (right-click)
                KioskWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                
                // Disable DevTools
                KioskWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                
                // Disable default script dialogs (alert, confirm, etc.)
                KioskWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                
                // Disable browser accelerator keys
                KioskWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                
                // Disable zoom
                KioskWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                
                // Disable status bar
                KioskWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Prevent navigation away from the kiosk URL
                KioskWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

                // Disable all keyboard input that might open new windows
                KioskWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                // Handle script dialogs if needed
                KioskWebView.CoreWebView2.ScriptDialogOpening += CoreWebView2_ScriptDialogOpening;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
        }
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Allow navigation within the same domain, block everything else
        var kioskDomain = "orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io";
        if (!args.Uri.Contains(kioskDomain))
        {
            args.Cancel = true;
        }
    }

    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Block all new window requests
        args.Handled = true;
    }

    private void CoreWebView2_ScriptDialogOpening(CoreWebView2 sender, CoreWebView2ScriptDialogOpeningEventArgs args)
    {
        // Allow script dialogs (alerts, confirms) if needed by the web app
        // You can customize this behavior
    }

    private void ExitOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var now = DateTime.Now;
        
        // Remove taps older than the window
        _tapTimestamps.RemoveAll(t => (now - t).TotalSeconds > TAP_WINDOW_SECONDS);
        
        // Add current tap
        _tapTimestamps.Add(now);
        
        // Check if we have enough taps
        if (_tapTimestamps.Count >= REQUIRED_TAPS)
        {
            _tapTimestamps.Clear();
            ShowPinDialog();
        }
    }

    private async void ShowPinDialog()
    {
        var pinDialog = new PinDialog
        {
            XamlRoot = this.Content.XamlRoot
        };

        var result = await pinDialog.ShowAsync();

        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            // PIN was correct, exit the application or logoff
            await ExitKioskMode();
        }
    }

    private async System.Threading.Tasks.Task ExitKioskMode()
    {
        // Option 1: Close the application
        // Application.Current.Exit();

        // Option 2: Log off Windows user (recommended for kiosk)
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/l",  // Logoff current user
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Logoff error: {ex.Message}");
            // Fallback: just close the app
            Application.Current.Exit();
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Unregister hotkeys
        var hwnd = WindowNative.GetWindowHandle(this);
        for (int i = 1; i <= 15; i++)
        {
            UnregisterHotKey(hwnd, i);
        }
    }
}

