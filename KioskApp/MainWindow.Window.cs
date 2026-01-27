using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Window configuration, positioning, and monitor management.
/// </summary>
public sealed partial class MainWindow
{
    #region Window Fields

    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private Rect _normalWindowBounds;
    private int _currentMonitorIndex = 0;

    #endregion

    #region Window Configuration

    /// <summary>
    /// Configures the window as a kiosk (borderless fullscreen) window.
    /// Uses Win32 APIs via HWND obtained from WinUI 3 window.
    /// </summary>
    private void ConfigureAsKioskWindow()
    {
        Logger.Log("========== ConfigureAsKioskWindow START ==========");
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        Logger.Log($"Window HWND: {_hwnd}");
        Logger.Log($"Window ID: {windowId.Value}");
        _appWindow = AppWindow.GetFromWindowId(windowId);
        Logger.Log($"AppWindow retrieved: {_appWindow != null}");

        // Remove caption/system menu/min/max/resize using helper
        Win32Native.RemoveWindowChrome(_hwnd);

        // Make topmost
        Win32Native.MakeTopmost(_hwnd);

        // Size to full monitor bounds
        if (_appWindow != null)
        {
            ConfigureWindowForMonitor(windowId);

            // Prevent closing via shell close messages
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }

        // Ensure window style changes are applied and shown
        Win32Native.SetWindowPos(_hwnd, Win32Native.HWND_TOPMOST, 0, 0, 0, 0,
            Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOMOVE);

        Debug.WriteLine("ConfigureAsKioskWindow completed");
        Logger.Log("========== ConfigureAsKioskWindow COMPLETE ==========");
    }

    /// <summary>
    /// Configures the window to display on the target monitor.
    /// </summary>
    private void ConfigureWindowForMonitor(WindowId windowId)
    {
        // Get all available displays
        var allDisplays = DisplayArea.FindAll();
        Debug.WriteLine($"Found {allDisplays.Count} display(s)");
        Logger.Log($"========== DISPLAY DETECTION ==========");
        Logger.Log($"Found {allDisplays.Count} display(s)");

        // Log all displays for reference
        LogAvailableDisplays(allDisplays, windowId);

        // Select target display
        var targetDisplay = GetTargetDisplay(allDisplays, windowId);
        var bounds = targetDisplay.OuterBounds;
        Logger.Log($"========== WINDOW POSITIONING ==========");
        Logger.Log($"Target bounds: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");

        // Set size and position using Win32 API for reliable fullscreen sizing
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            // Store normal window bounds for debug mode
            _normalWindowBounds = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);

            // Position window at the display's origin and set its size
            Logger.Log($"Calling SetWindowPos with: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
            Win32Native.SetWindowPositionTopmost(_hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            Debug.WriteLine($"Window positioned at ({bounds.X}, {bounds.Y}) with size {bounds.Width}x{bounds.Height}");
            Logger.Log($"SetWindowPos called successfully");

            // Verify window position
            int targetMonitorIndex = _currentMonitorIndex > 0 ? _currentMonitorIndex : _config.Kiosk.TargetMonitorIndex;
            if (targetMonitorIndex > 0)
            {
                _ = VerifyWindowPositionAsync(windowId, targetDisplay, bounds);
            }
        }
        else
        {
            Debug.WriteLine("Warning: Invalid display bounds received");
            Logger.Log("Warning: Invalid display bounds received");
        }
    }

    /// <summary>
    /// Logs information about all available displays.
    /// </summary>
    private void LogAvailableDisplays(IReadOnlyList<DisplayArea> allDisplays, WindowId windowId)
    {
        for (int i = 0; i < allDisplays.Count; i++)
        {
            var display = allDisplays[i];
            var dispBounds = display.OuterBounds;
            var workArea = display.WorkArea;
            Debug.WriteLine($"  Display {i}: {dispBounds.Width}x{dispBounds.Height} at ({dispBounds.X}, {dispBounds.Y})");
            Logger.Log($"  Display {i}: {dispBounds.Width}x{dispBounds.Height} at ({dispBounds.X}, {dispBounds.Y})");
            Logger.Log($"    Display ID: {display.DisplayId.Value}");
            Logger.Log($"    Work Area: {workArea.Width}x{workArea.Height} at ({workArea.X}, {workArea.Y})");
            Logger.Log($"    Is Primary: {display.IsPrimary}");

            try
            {
                var currentWindowDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.None);
                if (currentWindowDisplay != null && currentWindowDisplay.DisplayId == display.DisplayId)
                {
                    Logger.Log($"    *** CURRENT WINDOW IS ON THIS DISPLAY ***");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Gets the target display based on configuration.
    /// </summary>
    private DisplayArea GetTargetDisplay(IReadOnlyList<DisplayArea> allDisplays, WindowId windowId)
    {
        int targetMonitorIndex = _currentMonitorIndex > 0 ? _currentMonitorIndex : _config.Kiosk.TargetMonitorIndex;
        Logger.Log($"========== MONITOR SELECTION ==========");
        Logger.Log($"Configured target monitor index: {targetMonitorIndex} (1-based)");

        // Convert from 1-based config index to 0-based display index
        int displayIndex = targetMonitorIndex > 0 ? targetMonitorIndex - 1 : 0;

        if (displayIndex >= 0 && displayIndex < allDisplays.Count)
        {
            var targetDisplay = allDisplays[displayIndex];
            Debug.WriteLine($"Using monitor index {targetMonitorIndex} (display array index {displayIndex})");
            Logger.Log($"Using monitor index {targetMonitorIndex} (display array index {displayIndex})");
            Logger.Log($"  Target display ID: {targetDisplay.DisplayId.Value}");
            Logger.Log($"  Target bounds: {targetDisplay.OuterBounds.X}, {targetDisplay.OuterBounds.Y}, {targetDisplay.OuterBounds.Width}x{targetDisplay.OuterBounds.Height}");
            return targetDisplay;
        }
        else
        {
            // Invalid index, fallback to primary
            var targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            Debug.WriteLine($"WARNING: Monitor index {targetMonitorIndex} is invalid (only {allDisplays.Count} displays found). Using primary.");
            Logger.Log($"WARNING: Monitor index {targetMonitorIndex} is INVALID (only {allDisplays.Count} displays found)");
            Logger.Log($"  Falling back to primary display");
            Logger.Log($"  Primary display ID: {targetDisplay.DisplayId.Value}");
            return targetDisplay;
        }
    }

    /// <summary>
    /// Verifies and corrects window position after initial placement.
    /// </summary>
    private async Task VerifyWindowPositionAsync(WindowId windowId, DisplayArea targetDisplay, RectInt32 bounds)
    {
        Logger.Log("Waiting 200ms before verifying window position...");
        await Task.Delay(200);

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Logger.Log("========== WINDOW POSITION VERIFICATION ==========");
                var currentDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.None);
                Logger.Log($"Current display: {(currentDisplay != null ? currentDisplay.DisplayId.Value.ToString() : "NULL")}");
                Logger.Log($"Target display: {targetDisplay.DisplayId.Value}");

                // Check actual window position
                if (Win32Native.GetWindowRect(_hwnd, out var windowRect))
                {
                    Logger.Log($"  Actual window position: X={windowRect.Left}, Y={windowRect.Top}, W={windowRect.Width}, H={windowRect.Height}");
                }

                if (currentDisplay == null || currentDisplay.DisplayId != targetDisplay.DisplayId)
                {
                    Logger.Log("Window is NOT on target display - repositioning...");
                    Win32Native.SetWindowPositionTopmost(_hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    Logger.Log("  SetWindowPos called for retry");
                }
                else
                {
                    Logger.Log("Window successfully positioned on target display");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ERROR during window position verification: {ex.Message}");
            }
        });
    }

    #endregion

    #region Monitor Switching

    /// <summary>
    /// Handles the switch monitor button click in debug mode.
    /// </summary>
    private async void SwitchMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var allDisplays = DisplayArea.FindAll();
            int displayCount = allDisplays.Count;

            if (displayCount <= 1)
            {
                Logger.Log("Only one display available, cannot switch monitors");
                var singleDisplayDialog = new ContentDialog
                {
                    Title = "Single Display",
                    Content = "Only one display is available. Cannot switch monitors.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await singleDisplayDialog.ShowAsync();
                return;
            }

            // In debug mode, show a selection dialog instead of auto-cycling
            int selectedMonitor;
            if (_isDebugMode)
            {
                selectedMonitor = await ShowMonitorSelectionDialogAsync(allDisplays);
                if (selectedMonitor < 0) return; // User cancelled
            }
            else
            {
                // Non-debug mode: cycle to next monitor (1-based indexing)
                selectedMonitor = _currentMonitorIndex + 1;
                if (selectedMonitor > displayCount)
                {
                    selectedMonitor = 1;
                }
            }

            await SwitchToMonitorAsync(selectedMonitor, allDisplays);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching monitors: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a dialog to select which monitor to switch to.
    /// </summary>
    private async Task<int> ShowMonitorSelectionDialogAsync(IReadOnlyList<DisplayArea> allDisplays)
    {
        var monitorListPanel = new StackPanel { Spacing = 8 };
        var monitorComboBox = new ComboBox { Width = 300 };

        for (int i = 0; i < allDisplays.Count; i++)
        {
            var display = allDisplays[i];
            var bounds = display.OuterBounds;
            string displayInfo = $"Monitor {i + 1}: {bounds.Width}x{bounds.Height} at ({bounds.X}, {bounds.Y})";
            if (i + 1 == _currentMonitorIndex)
            {
                displayInfo += " (current)";
            }
            monitorComboBox.Items.Add(displayInfo);
        }

        // Pre-select the next monitor in sequence as default
        int nextMonitor = _currentMonitorIndex % allDisplays.Count;
        monitorComboBox.SelectedIndex = nextMonitor;

        monitorListPanel.Children.Add(new TextBlock { Text = "Select which monitor to switch to:" });
        monitorListPanel.Children.Add(monitorComboBox);

        var selectDialog = new ContentDialog
        {
            Title = "Select Monitor",
            Content = monitorListPanel,
            PrimaryButtonText = "Switch",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot
        };

        var result = await selectDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            Logger.Log("Monitor switch cancelled by user");
            return -1;
        }

        int selectedMonitor = monitorComboBox.SelectedIndex + 1; // Convert to 1-based indexing
        Logger.Log($"User selected monitor {selectedMonitor} from dialog");
        return selectedMonitor;
    }

    /// <summary>
    /// Switches the window to the specified monitor.
    /// </summary>
    private async Task SwitchToMonitorAsync(int monitorIndex, IReadOnlyList<DisplayArea> allDisplays)
    {
        _currentMonitorIndex = monitorIndex;
        Logger.Log($"========== SWITCHING TO MONITOR {_currentMonitorIndex} ==========");

        // Update video controller if it exists - must recreate since monitor index is set in constructor
        if (_videoController != null)
        {
            bool wasInVideoMode = _isVideoMode;

            if (wasInVideoMode)
            {
                Logger.Log("Stopping video for monitor switch...");
                await _videoController.StopAsync();
            }

            // Dispose old controller and create new one with updated monitor index
            _videoController.Dispose();
            _videoController = new VideoController(_config.Kiosk.VideoMode!, _currentMonitorIndex);
            await _videoController.InitializeAsync();
            Logger.Log($"Video controller recreated for monitor {_currentMonitorIndex}");

            if (wasInVideoMode)
            {
                Logger.Log("Restarting video on new monitor...");
                await _videoController.ResumePlaybackAsync();
            }
        }

        // Get the target display
        int displayIndex = _currentMonitorIndex - 1;
        if (displayIndex >= 0 && displayIndex < allDisplays.Count)
        {
            var targetDisplay = allDisplays[displayIndex];
            var bounds = targetDisplay.OuterBounds;

            Logger.Log($"Moving window to display {displayIndex}: {bounds.Width}x{bounds.Height} at ({bounds.X}, {bounds.Y})");

            // Update stored bounds
            _normalWindowBounds = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);

            // Move and resize window
            Win32Native.SetWindowPositionTopmost(_hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            Logger.Log($"Window moved to monitor {_currentMonitorIndex}");
        }
    }

    #endregion

    #region Window Helpers

    /// <summary>
    /// Gets the current window handle.
    /// </summary>
    public IntPtr WindowHandle => _hwnd;

    /// <summary>
    /// Gets the current AppWindow instance.
    /// </summary>
    public AppWindow? ApplicationWindow => _appWindow;

    #endregion
}
