using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics.Display;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;
using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml.Input;

namespace KioskApp;

/// <summary>
/// Main window for the OneRoom Health Kiosk application.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Core objects and references
    private readonly KioskConfiguration _config;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
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
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);


    // State management
    private bool _isDebugMode = false;
    private Rect _normalWindowBounds;
    private bool _isVideoMode = false;
    private string? _currentUrl = null;
    private bool _logsVisible = false;
    private int _currentMonitorIndex = 0;

    // Win32 API imports
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _hookID = IntPtr.Zero;

    // Media device selection for debug mode
    private List<MediaDeviceInfo> _cameras = new();
    private List<MediaDeviceInfo> _microphones = new();
    private string? _selectedCameraId = null;
    private string? _selectedMicrophoneId = null;

    /// <summary>
    /// Represents a media device (camera or microphone) for the selector dropdowns.
    /// </summary>
    private class MediaDeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string Label { get; set; } = "";
        public override string ToString() => Label;
    }

    public MainWindow(KioskConfiguration config)
    {
        this.InitializeComponent();
        _config = config;
        Logger.Log("MainWindow constructor called");
        Logger.Log($"Log file is being written to: {Logger.LogFilePath}");
        
        // Subscribe to log events for real-time updates
        Logger.LogAdded += OnLogAdded;
        
        // Always start in screensaver mode (WebView visible)
        _isVideoMode = false;
        Logger.Log("Starting in screensaver mode (default)");
        
        // Initialize current monitor index from config
        _currentMonitorIndex = _config.Kiosk.TargetMonitorIndex;
        
        // Initialize video controller if video configuration exists (even if not enabled)
        if (_config.Kiosk.VideoMode != null)
        {
            _videoController = new VideoController(_config.Kiosk.VideoMode, _currentMonitorIndex);
            Logger.Log("Video controller initialized (available for hotkey activation)");
        }

        // Hook Activated event - do all initialization there when window is fully ready
        this.Activated += MainWindow_Activated;
    }

    /// <summary>
    /// Navigates the WebView to the specified URL.
    /// </summary>
    public void NavigateToUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _currentUrl = url;
            DispatcherQueue.TryEnqueue(() =>
            {
                KioskWebView.Source = uri;
                Logger.Log($"Navigating to: {url}");
            });
        }
        else
        {
            Logger.Log($"Invalid URL: {url}");
        }
    }

    private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        // Only initialize once on first activation
        if (e.WindowActivationState != WindowActivationState.Deactivated && _appWindow == null)
        {
            Debug.WriteLine("==================== WINDOW ACTIVATION START ====================");
            Logger.Log("==================== WINDOW ACTIVATION START ====================");
            Debug.WriteLine("MainWindow_Activated event fired (first activation)");
            Logger.Log("MainWindow.Activated event fired");
            Logger.Log($"Video mode: {_isVideoMode}");
            Logger.Log($"Target monitor index: {_currentMonitorIndex} (1-based)");
            
            // Configure kiosk window after it's activated
            ConfigureAsKioskWindow();
            
            // Setup comprehensive keyboard handling
            SetupKeyboardHandling();
            
            // Initialize WebView2 asynchronously without blocking the Activated event
            _ = InitializeWebViewAsync();
            
            Logger.Log("==================== WINDOW ACTIVATION COMPLETE ====================");
        }
    }

    /// <summary>
    /// Sets up comprehensive keyboard handling to ensure hotkeys work reliably
    /// </summary>
    private void SetupKeyboardHandling()
    {
        try
        {
            // Method 1: Low-level keyboard hook (catches ALL keyboard input)
            SetupLowLevelKeyboardHook();

            // Method 2: Window-level PreviewKeyDown (catches keys before child controls)
            this.Content.PreviewKeyDown += Content_PreviewKeyDown;
            Logger.Log("Window PreviewKeyDown handler registered");

            // Method 3: Accelerator keys on window content (standard WinUI approach)
            SetupKeyboardAccelerators();
            
            // Method 4: WebView2-specific handling (prevent WebView from eating keys)
            // This will be set up after WebView2 is initialized
            
            Logger.Log("All keyboard handlers registered successfully");
            
            // Log enabled hotkeys for debugging
            Logger.Log("=== ENABLED HOTKEYS ===");
            if (_config.Debug.Enabled)
                Logger.Log($"  Debug Mode: {_config.Debug.Hotkey} (configured) / Ctrl+Shift+I (handled)");
            if (_config.Exit.Enabled)
                Logger.Log($"  Exit Kiosk: {_config.Exit.Hotkey} (configured) / Ctrl+Shift+Q (handled)");
            if (_videoController != null)
            {
                Logger.Log("  Mode Toggle Controls:");
                Logger.Log("    Ctrl+Alt+D: Switch to VIDEO MODE / Toggle between videos");
                Logger.Log("    Ctrl+Alt+E: Switch to SCREENSAVER MODE");
                Logger.Log("    Ctrl+Alt+R: Restart Carescape video (video mode only)");
            }
            Logger.Log("======================");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting up keyboard handling: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up a low-level keyboard hook to capture all keyboard input
    /// </summary>
    private void SetupLowLevelKeyboardHook()
    {
        try
        {
            _keyboardProc = HookCallback;
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule!)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule.ModuleName), 0);
            }
            Logger.Log("Low-level keyboard hook installed");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to install keyboard hook: {ex.Message}");
        }
    }

    /// <summary>
    /// Low-level keyboard hook callback
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            VirtualKey vkCode = (VirtualKey)hookStruct.vkCode;

            // Get modifier states
            bool ctrlPressed = (GetKeyState((int)VirtualKey.Control) & 0x8000) != 0;
            bool shiftPressed = (GetKeyState((int)VirtualKey.Shift) & 0x8000) != 0;
            bool altPressed = (GetKeyState((int)VirtualKey.Menu) & 0x8000) != 0;

            // Log the key press (only log our hotkey combinations to avoid log spam)
            if ((ctrlPressed && shiftPressed && (vkCode == VirtualKey.I || vkCode == VirtualKey.Q)) ||
                (ctrlPressed && altPressed && (vkCode == VirtualKey.D || vkCode == VirtualKey.E || vkCode == VirtualKey.R)))
            {
                Logger.Log($"[HOTKEY] LowLevelKeyboardHook: Key={vkCode}, Ctrl={ctrlPressed}, Shift={shiftPressed}, Alt={altPressed}");
            }

            // Handle our hotkeys
            bool handled = false;

            // Debug mode: Ctrl+Shift+I
            if (_config.Debug.Enabled && ctrlPressed && shiftPressed && vkCode == VirtualKey.I)
            {
                handled = true;
                Logger.LogSecurityEvent("DebugModeHotkeyPressed", "User pressed Ctrl+Shift+I (via keyboard hook)");
                DispatcherQueue.TryEnqueue(async () => await ToggleDebugMode());
            }
            // Exit: Ctrl+Shift+Q
            else if (_config.Exit.Enabled && ctrlPressed && shiftPressed && vkCode == VirtualKey.Q)
            {
                handled = true;
                Logger.LogSecurityEvent("ExitHotkeyPressed", "User pressed Ctrl+Shift+Q (via keyboard hook)");
                DispatcherQueue.TryEnqueue(async () => await HandleExitRequest());
            }
            // Video/Screensaver mode toggle controls
            else if (ctrlPressed && altPressed && (vkCode == VirtualKey.D || vkCode == VirtualKey.E || vkCode == VirtualKey.R))
            {
                // Add detailed logging for debugging
                Logger.Log($"[HOTKEY DEBUG] Mode toggle hotkey detected: Key={vkCode}, CurrentVideoMode={_isVideoMode}, Controller={_videoController != null}");
                
                // Check if video controller is available
                if (_videoController != null)
                {
                    switch (vkCode)
                    {
                        case VirtualKey.D:
                            handled = true;
                            if (_isVideoMode)
                                Logger.Log("Toggle video source (Ctrl+Alt+D) via keyboard hook");
                            else
                                Logger.Log("Switch to VIDEO MODE (Ctrl+Alt+D) via keyboard hook");
                            DispatcherQueue.TryEnqueue(async () => await SwitchToVideoMode());
                            break;
                        case VirtualKey.E:
                            handled = true;
                            Logger.Log("Switch to SCREENSAVER MODE (Ctrl+Alt+E) via keyboard hook");
                            DispatcherQueue.TryEnqueue(async () => await SwitchToScreensaverMode());
                            break;
                        case VirtualKey.R:
                            handled = true;
                            if (_isVideoMode)
                            {
                                Logger.Log("Restart carescape pressed (Ctrl+Alt+R) via keyboard hook");
                                DispatcherQueue.TryEnqueue(async () => await _videoController.RestartCarescapeAsync());
                            }
                            else
                            {
                                Logger.Log("[HOTKEY WARNING] Ctrl+Alt+R pressed but not in video mode");
                            }
                            break;
                    }
                }
                else
                {
                    Logger.Log($"[HOTKEY WARNING] Video hotkey pressed but no video controller available");
                }
            }

            // If we handled this key, consume it
            if (handled)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    /// <summary>
    /// Content PreviewKeyDown handler - backup method
    /// </summary>
    private async void Content_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            // Get modifier states using Win32 API
            bool ctrlPressed = (GetKeyState((int)VirtualKey.Control) & 0x8000) != 0;
            bool shiftPressed = (GetKeyState((int)VirtualKey.Shift) & 0x8000) != 0;
            bool altPressed = (GetKeyState((int)VirtualKey.Menu) & 0x8000) != 0;

            // Log for debugging (only log our hotkey combinations)
            if ((ctrlPressed && shiftPressed && (e.Key == VirtualKey.I || e.Key == VirtualKey.Q)) ||
                (ctrlPressed && altPressed && (e.Key == VirtualKey.D || e.Key == VirtualKey.E || e.Key == VirtualKey.R)))
            {
                Logger.Log($"[HOTKEY] Content_PreviewKeyDown: Key={e.Key}, Ctrl={ctrlPressed}, Shift={shiftPressed}, Alt={altPressed}");
            }

            // Handle hotkeys
            if (_config.Debug.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.I)
            {
                e.Handled = true;
                await ToggleDebugMode();
            }
            else if (_config.Exit.Enabled && ctrlPressed && shiftPressed && e.Key == VirtualKey.Q)
            {
                e.Handled = true;
                await HandleExitRequest();
            }
            else if (ctrlPressed && altPressed && (e.Key == VirtualKey.D || e.Key == VirtualKey.E || e.Key == VirtualKey.R))
            {
                // Add detailed logging for debugging
                Logger.Log($"[HOTKEY DEBUG] Mode toggle hotkey detected (PreviewKeyDown): Key={e.Key}, CurrentVideoMode={_isVideoMode}, Controller={_videoController != null}");
                
                // Check if video controller is available
                if (_videoController != null)
                {
                    if (e.Key == VirtualKey.D)
                    {
                        e.Handled = true;
                        if (_isVideoMode)
                            Logger.Log("Toggle video source (Ctrl+Alt+D) via PreviewKeyDown");
                        else
                            Logger.Log("Switch to VIDEO MODE (Ctrl+Alt+D) via PreviewKeyDown");
                        await SwitchToVideoMode();
                    }
                    else if (e.Key == VirtualKey.E)
                    {
                        e.Handled = true;
                        Logger.Log("Switch to SCREENSAVER MODE (Ctrl+Alt+E) via PreviewKeyDown");
                        await SwitchToScreensaverMode();
                    }
                    else if (e.Key == VirtualKey.R)
                    {
                        e.Handled = true;
                        if (_isVideoMode)
                        {
                            Logger.Log("Restart carescape pressed (Ctrl+Alt+R) via PreviewKeyDown");
                            await _videoController.RestartCarescapeAsync();
                        }
                        else
                        {
                            Logger.Log("[HOTKEY WARNING] Ctrl+Alt+R pressed but not in video mode");
                        }
                    }
                }
                else
                {
                    Logger.Log($"[HOTKEY WARNING] Video hotkey pressed but no video controller available (PreviewKeyDown)");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in Content_PreviewKeyDown: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up keyboard accelerators as an additional method
    /// </summary>
    private void SetupKeyboardAccelerators()
    {
        try
        {
            var content = this.Content as FrameworkElement;
            if (content == null) return;

            // Clear any existing accelerators
            content.KeyboardAccelerators.Clear();

            // Debug mode: Ctrl+Shift+I
            var debugAccel = new KeyboardAccelerator
            {
                Key = VirtualKey.I,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
            };
            debugAccel.Invoked += async (s, e) =>
            {
                e.Handled = true;
                if (_config.Debug.Enabled)
                {
                    Logger.Log("Debug accelerator invoked");
                    await ToggleDebugMode();
                }
            };
            content.KeyboardAccelerators.Add(debugAccel);

            // Exit: Ctrl+Shift+Q
            var exitAccel = new KeyboardAccelerator
            {
                Key = VirtualKey.Q,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
            };
            exitAccel.Invoked += async (s, e) =>
            {
                e.Handled = true;
                if (_config.Exit.Enabled)
                {
                    Logger.Log("Exit accelerator invoked");
                    await HandleExitRequest();
                }
            };
            content.KeyboardAccelerators.Add(exitAccel);

            // Video/Screensaver mode toggle accelerators
            if (_videoController != null)
            {
                // Switch to video mode: Ctrl+Alt+D
                var videoModeAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.D,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                videoModeAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    if (_isVideoMode)
                        Logger.Log("Toggle video source accelerator invoked");
                    else
                        Logger.Log("Switch to video mode accelerator invoked");
                    await SwitchToVideoMode();
                };
                content.KeyboardAccelerators.Add(videoModeAccel);

                // Switch to screensaver mode: Ctrl+Alt+E
                var screensaverModeAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.E,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                screensaverModeAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    Logger.Log("Screensaver mode accelerator invoked");
                    await SwitchToScreensaverMode();
                };
                content.KeyboardAccelerators.Add(screensaverModeAccel);

                // Restart: Ctrl+Alt+R
                var restartAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.R,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                restartAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    if (_isVideoMode)
                    {
                        Logger.Log("Restart accelerator invoked");
                        await _videoController.RestartCarescapeAsync();
                    }
                    else
                    {
                        Logger.Log("[HOTKEY WARNING] Ctrl+Alt+R pressed but not in video mode");
                    }
                };
                content.KeyboardAccelerators.Add(restartAccel);
            }

            Logger.Log($"Keyboard accelerators set up: {content.KeyboardAccelerators.Count} total");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting up keyboard accelerators: {ex.Message}");
        }
    }

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
            Logger.Log($"========== DISPLAY DETECTION ==========");
            Logger.Log($"Found {allDisplays.Count} display(s)");
            
            // Log all displays for reference
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
                
                // Try to get additional monitor info
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
            
            // Select target display
            DisplayArea targetDisplay;
            int targetMonitorIndex = _currentMonitorIndex > 0 ? _currentMonitorIndex : _config.Kiosk.TargetMonitorIndex;
            Logger.Log($"========== MONITOR SELECTION ==========");
            Logger.Log($"Configured target monitor index: {targetMonitorIndex} (1-based)");
            
            // Convert from 1-based config index to 0-based display index
            int displayIndex = targetMonitorIndex > 0 ? targetMonitorIndex - 1 : 0;
            
            if (displayIndex >= 0 && displayIndex < allDisplays.Count)
            {
                targetDisplay = allDisplays[displayIndex];
                Debug.WriteLine($"Using monitor index {targetMonitorIndex} (display array index {displayIndex})");
                Logger.Log($"✓ Using monitor index {targetMonitorIndex} (display array index {displayIndex})");
                Logger.Log($"  Target display ID: {targetDisplay.DisplayId.Value}");
                Logger.Log($"  Target bounds: {targetDisplay.OuterBounds.X}, {targetDisplay.OuterBounds.Y}, {targetDisplay.OuterBounds.Width}x{targetDisplay.OuterBounds.Height}");
            }
            else
            {
                // Invalid index, fallback to primary
                targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                Debug.WriteLine($"WARNING: Monitor index {targetMonitorIndex} is invalid (only {allDisplays.Count} displays found). Using primary.");
                Logger.Log($"✗ WARNING: Monitor index {targetMonitorIndex} is INVALID (only {allDisplays.Count} displays found)");
                Logger.Log($"  Falling back to primary display");
                Logger.Log($"  Primary display ID: {targetDisplay.DisplayId.Value}");
            }
            
            var bounds = targetDisplay.OuterBounds; // Use OuterBounds for true fullscreen
            Logger.Log($"========== WINDOW POSITIONING ==========");
            Logger.Log($"Target bounds: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
            
            // Set size and position using Win32 API for reliable fullscreen sizing
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                // Store normal window bounds for debug mode
                _normalWindowBounds = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);

                // Position window at the display's origin and set its size
                Logger.Log($"Calling SetWindowPos with: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
                SetWindowPos(_hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
                Debug.WriteLine($"Window positioned at ({bounds.X}, {bounds.Y}) with size {bounds.Width}x{bounds.Height}");
                Logger.Log($"✓ SetWindowPos called successfully");
                
                // Add verification step to ensure window moved to correct monitor
                // This helps fix issues where window doesn't initially appear on the target screen
                Logger.Log($"Target monitor index: {targetMonitorIndex}, starting verification task: {targetMonitorIndex > 0}");
                if (targetMonitorIndex > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        Logger.Log("Waiting 200ms before verifying window position...");
                        await Task.Delay(200);  // Give window time to move
                        
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                Logger.Log("========== WINDOW POSITION VERIFICATION ==========");
                                // Double-check window position and force move again if needed
                                var currentDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.None);
                                Logger.Log($"Current display: {(currentDisplay != null ? currentDisplay.DisplayId.Value.ToString() : "NULL")}");
                                Logger.Log($"Target display: {targetDisplay.DisplayId.Value}");
                                
                                // Also check actual window position
                                RECT windowRect;
                                if (GetWindowRect(_hwnd, out windowRect))
                                {
                                    Logger.Log($"  Actual window position: X={windowRect.Left}, Y={windowRect.Top}, W={windowRect.Right - windowRect.Left}, H={windowRect.Bottom - windowRect.Top}");
                                }
                                
                                if (currentDisplay == null || currentDisplay.DisplayId != targetDisplay.DisplayId)
                                {
                                    Logger.Log("✗ Window is NOT on target display!");
                                    Logger.Log($"  Expected display ID: {targetDisplay.DisplayId.Value}");
                                    Logger.Log($"  Expected position: X={bounds.X}, Y={bounds.Y}");
                                    Logger.Log($"  Actual display ID: {(currentDisplay != null ? currentDisplay.DisplayId.Value.ToString() : "NULL")}");
                                    Logger.Log("  Attempting to reposition window...");
                                    
                                    SetWindowPos(_hwnd, IntPtr.Zero, 
                                        bounds.X, bounds.Y, bounds.Width, bounds.Height,
                                        SWP_SHOWWINDOW | SWP_NOZORDER);
                                    Logger.Log("  SetWindowPos called for retry");
                                    
                                    // Verify again after retry
                                    _ = Task.Delay(100).ContinueWith(_ =>
                                    {
                                        DispatcherQueue.TryEnqueue(() =>
                                        {
                                            var finalDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.None);
                                            Logger.Log($"  Final position check - Display ID: {(finalDisplay != null ? finalDisplay.DisplayId.Value.ToString() : "NULL")}");
                                            if (finalDisplay != null && finalDisplay.DisplayId == targetDisplay.DisplayId)
                                            {
                                                Logger.Log("  ✓ Window successfully moved on retry!");
                                            }
                                            else
                                            {
                                                Logger.Log("  ✗ Window STILL not on correct display after retry!");
                                            }
                                        });
                                    });
                                }
                                else
                                {
                                    Logger.Log("✓ Window successfully positioned on target display");
                                    if (currentDisplay != null)
                                    {
                                        var currentBounds = currentDisplay.OuterBounds;
                                        Logger.Log($"  Current bounds: X={currentBounds.X}, Y={currentBounds.Y}, W={currentBounds.Width}x{currentBounds.Height}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"✗ ERROR during window position verification: {ex.Message}");
                                Logger.Log($"  Stack trace: {ex.StackTrace}");
                            }
                        });
                    });
                }
            }
            else
            {
                Debug.WriteLine("Warning: Invalid display bounds received");
                Logger.Log("Warning: Invalid display bounds received");
            }

            // Prevent closing via shell close messages
            _appWindow.Closing += (_, e) => { e.Cancel = true; };
        }

        // Ensure window style changes are applied and shown (using SWP_NOSIZE to keep current size)
        const uint SWP_NOSIZE = 0x0001;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOSIZE | SWP_NOMOVE);
        
        Debug.WriteLine("ConfigureAsKioskWindow completed");
        Logger.Log("========== ConfigureAsKioskWindow COMPLETE ==========");
    }

    /// <summary>
    /// Initializes WebView2 and navigates to the configured URL. Also prepares video controller if available.
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        Logger.Log("========== InitializeWebViewAsync START ==========");
        try
        {
            // TODO: Implement API server for remote navigation
            // if (_config.HttpApi.Enabled)
            // {
            //     _apiServer = new ApiServer(_config, KioskWebView, DispatcherQueue);
            //     await _apiServer.StartAsync();
            // }
            
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
                // Note: In WinUI 3, WebView2 uses default environment. Autoplay is typically 
                // allowed for muted videos by default. For unmuted autoplay, the web content
                // should handle user interaction requirements.
                var environment = await CoreWebView2Environment.CreateAsync();
                
                Logger.Log("Ensuring CoreWebView2 is ready...");
                await KioskWebView.EnsureCoreWebView2Async(environment);
                Logger.Log("CoreWebView2 is ready, setting up WebView...");
                
                SetupWebView();
                
                // Navigate to the configured URL
                _currentUrl = _config.Kiosk.DefaultUrl;
                Logger.Log($"Setting WebView source to: {_config.Kiosk.DefaultUrl}");
                KioskWebView.Source = new Uri(_config.Kiosk.DefaultUrl);
                Logger.Log($"✓ Navigation initiated to: {_config.Kiosk.DefaultUrl}");
                
                // Add a fallback to hide status after a timeout in case navigation doesn't complete
                Logger.Log("Starting 3-second timeout fallback for status overlay");
                _ = Task.Delay(3000).ContinueWith(_ => 
                {
                    DispatcherQueue.TryEnqueue(() => 
                    {
                        // Only hide if still showing initialization status
                        Logger.Log($"Timeout reached. Current status title: '{StatusTitle.Text}'");
                        if (StatusTitle.Text == "Initializing")
                        {
                            Logger.Log("✓ Forcing status overlay to hide after timeout");
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
                Logger.Log($"✗ WebView2 initialization error: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            Logger.Log($"✗ InitializeWebViewAsync error: {ex.Message}");
            Logger.Log($"Stack trace: {ex.StackTrace}");
            ShowStatus("Initialization Error", ex.Message);
        }
        Logger.Log("========== InitializeWebViewAsync COMPLETE ==========");
    }

    /// <summary>
    /// Configures WebView2 settings, including developer tools restrictions.
    /// </summary>
    private void SetupWebView()
    {
        var settings = KioskWebView.CoreWebView2.Settings;

        // Kiosk mode settings
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;
        
        // Auto-allow all permissions for kiosk mode (camera, microphone, autoplay, etc.)
        // This ensures the kiosk application works seamlessly without permission prompts
        KioskWebView.CoreWebView2.PermissionRequested += (sender, args) =>
        {
            // Auto-allow all permission types for kiosk mode
            args.State = CoreWebView2PermissionState.Allow;
            Logger.Log($"Auto-allowed permission: {args.PermissionKind}");
        };

        // Developer tools are initially disabled (unless debug mode is active)
        settings.AreDevToolsEnabled = _isDebugMode;
        settings.AreDefaultContextMenusEnabled = _isDebugMode;
        settings.AreDefaultScriptDialogsEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = false; // Disable F5, Ctrl+R, etc.

        // Navigation event handlers
        KioskWebView.NavigationCompleted += OnNavigationCompleted;
        
        // Ensure status overlay is hidden when WebView is ready
        Logger.Log("WebView2 setup complete, ensuring status overlay is hidden");
        DispatcherQueue.TryEnqueue(() => HideStatus());
        
        // Disable new window requests
        KioskWebView.CoreWebView2.NewWindowRequested += (sender, args) =>
        {
            args.Handled = true; // Block popups and new windows
        };

        // Prevent WebView from capturing all keyboard input
        // This is crucial for hotkeys to work
        KioskWebView.CoreWebView2.DocumentTitleChanged += (sender, args) =>
        {
            // Periodically ensure our window has proper focus handling
            _ = EnsureFocusHandling();
        };

        // Additional WebView keyboard handling
        KioskWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
        KioskWebView.CoreWebView2.DOMContentLoaded += async (sender, args) =>
        {
            try
            {
                // Inject JavaScript to:
                // 1. Prevent WebView from consuming our hotkeys
                // 2. Enable autoplay for all media elements
                string script = @"
                    // Keyboard hotkey handling
                    document.addEventListener('keydown', function(e) {
                        // Check if this is one of our hotkeys
                        if ((e.ctrlKey && e.shiftKey && (e.key === 'i' || e.key === 'I' || e.key === 'q' || e.key === 'Q')) ||
                            (e.ctrlKey && e.altKey && (e.key === 'd' || e.key === 'D' || e.key === 'e' || e.key === 'E' || e.key === 'r' || e.key === 'R'))) {
                            // Prevent the webpage from handling these keys
                            e.preventDefault();
                            // IMPORTANT: Do NOT call stopPropagation() - we want the event to bubble up!
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
                await KioskWebView.CoreWebView2.ExecuteScriptAsync(script);
                Logger.Log("Injected kiosk scripts (hotkeys + autoplay)");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error injecting kiosk scripts: {ex.Message}");
            }
        };

        Logger.Log("WebView2 setup completed");
    }

    /// <summary>
    /// Ensures proper focus handling for keyboard input
    /// </summary>
    private async Task EnsureFocusHandling()
    {
        await Task.Delay(100);
        
        // Ensure window is active but don't steal focus from WebView unnecessarily
        if (!_isVideoMode)
        {
            // In web mode, we want WebView to have focus for normal interaction
            // But we still need our keyboard handlers to work
            Logger.Log("Focus handling check completed");
        }
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        Logger.Log($"========== NAVIGATION COMPLETED ==========");
        Logger.Log($"Success: {args.IsSuccess}, Error: {args.WebErrorStatus}");
        
        DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsSuccess)
            {
                var uri = sender.Source.ToString();
                Logger.Log($"✓ Navigation successful to: {uri}");
                // Don't show status overlay for successful navigation - it blocks the content
                // ShowStatus("Navigation Complete", uri);
                
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
                }
                else
                {
                    // If navigating to about:blank, keep the previous URL
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
                Logger.Log($"✗ Navigation FAILED: {args.WebErrorStatus}");
                ShowStatus("Navigation Failed", $"Error: {args.WebErrorStatus}");
            }
        });
    }

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

    #region Debug Mode and Exit Mechanism

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
                    SetupWebView();
                    
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
                    
                    // Load available cameras and microphones for the dropdowns
                    _ = LoadAllMediaDevicesAsync();

                    // Window the application
                    if (_appWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                    {
                        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    }

                    // Restore window frame
                    var style = GetWindowLong(_hwnd, GWL_STYLE);
                    style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
                    SetWindowLong(_hwnd, GWL_STYLE, style);

                    // Remove topmost
                    var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_TOPMOST;
                    SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                    // Apply normal window size
                    const uint SWP_NOSIZE = 0x0001;
                    SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                        SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOSIZE | SWP_NOMOVE);

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

        await Task.Run(async () =>
        {
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

                        // Close developer tools if open
                        try
                        {
                            _ = KioskWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Browser.close", "{}");
                        }
                        catch { /* Ignore if already closed */ }
                    }

                    // Hide log viewer if visible
                    if (_logsVisible)
                    {
                        LogViewerPanel.Visibility = Visibility.Collapsed;
                        _logsVisible = false;
                    }
                    
                    // Hide debug panel and reset margin FIRST
                    DebugPanel.Visibility = Visibility.Collapsed;
                    if (KioskWebView != null)
                        KioskWebView.Margin = new Thickness(0);

                    // Update window title
                    this.Title = "OneRoom Health Kiosk";

                    // Set presenter to Overlapped (standard window) instead of FullScreen
                    // This ensures our manual kiosk styling/sizing in ConfigureAsKioskWindow works correctly
                    // (FullScreen presenter can conflict with manual SetWindowPos sizing)
                    if (_appWindow != null)
                    {
                        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                        Logger.Log("Set presenter to Overlapped before configuration");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error in ExitDebugMode UI cleanup: {ex.Message}");
                }
            });
        });

        // Wait longer for presenter change to take effect
        await Task.Delay(200);

        // Now configure window and explicitly size WebView
        // Wait a moment for presenter change to take effect
        await Task.Delay(100);

        // Now restore kiosk window configuration (this will properly size it)
        await DispatcherQueue.EnqueueAsync(() =>
            {
                try
                {
                    ConfigureAsKioskWindow();

                    // Get the actual window size and reset WebView to fill window
                    if (_appWindow != null && KioskWebView != null)
                    {
                        // Don't hardcode size, use Stretch alignment to ensure it adapts to window
                        KioskWebView.Width = double.NaN;
                        KioskWebView.Height = double.NaN;
                        KioskWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
                        KioskWebView.VerticalAlignment = VerticalAlignment.Stretch;
                        KioskWebView.Margin = new Thickness(0);
                        Logger.Log("Reset WebView size to Auto/Stretch");
                    }
                    
                    // Force layout update to ensure WebView resizes properly
                    if (this.Content is UIElement content)
                    {
                        content.UpdateLayout();
                    }
                    
                    // Ensure WebView fills the window
                    if (KioskWebView != null)
                    {
                        KioskWebView.Margin = new Thickness(0);
                        KioskWebView.UpdateLayout();
                        Logger.Log("Forced WebView layout update");
                    }
                    
                    // Wait a bit longer, then reconfigure to ensure proper sizing
                    _ = Task.Delay(200).ContinueWith(async _ => // Made this async to await DispatcherQueue.EnqueueAsync
                    {
                        await DispatcherQueue.EnqueueAsync(() => // Use EnqueueAsync for awaitable DispatcherQueue operations
                        {
                            // Re-run ConfigureAsKioskWindow to ensure window is properly sized
                            ConfigureAsKioskWindow();
                            Logger.Log("Re-ran ConfigureAsKioskWindow after delay to ensure proper sizing");
                            
                            // Force another layout update
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
                    else if (!string.IsNullOrEmpty(_currentUrl))
                    {
                        // Wait a moment for window configuration to complete, then navigate
                        // Use explicit navigation instead of Reload() to ensure proper page load
                        _ = Task.Delay(100).ContinueWith(async _ => // Made this async to await DispatcherQueue.EnqueueAsync
                        {
                            await DispatcherQueue.EnqueueAsync(() => // Use EnqueueAsync for awaitable DispatcherQueue operations
                            {
                                if (KioskWebView?.CoreWebView2 != null && Uri.TryCreate(_currentUrl, UriKind.Absolute, out var uri))
                                {
                                    KioskWebView.Source = uri;
                                    Logger.Log($"Navigating to URL after debug mode: {_currentUrl}");
                                    
                                    // Force a refresh to ensure content fills the WebView
                                    _ = Task.Delay(500).ContinueWith(_ =>
                                    {
                                        DispatcherQueue.TryEnqueue(() =>
                                        {
                                            if (KioskWebView?.CoreWebView2 != null)
                                            {
                                                _ = KioskWebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('resize'));").AsTask();
                                                Logger.Log("Triggered resize event in web content");
                                            }
                                        });
                                    });
                                }
                                else if (!string.IsNullOrEmpty(_currentUrl))
                                {
                                    NavigateToUrl(_currentUrl);
                                    Logger.Log($"Navigating to URL after debug mode (WebView2 not ready): {_currentUrl}");
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

            // Set presenter to Overlapped (standard window) before configuring
            // This matches startup behavior and avoids FullScreen presenter conflicts
            if (_appWindow != null)
            {
                if (_appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped)
                {
                    _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    Logger.Log("Set presenter to Overlapped mode");
                }
                
                // Wait a moment for presenter change to take effect
                await Task.Delay(100);
            }

            // Now configure the window (this will properly size it for fullscreen)
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
                        
                        // Re-run ConfigureAsKioskWindow after delay to ensure proper sizing
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

                        // Force a refresh to ensure content fills the WebView
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
            
            // Stop video controller if active
            if (_videoController != null)
            {
                Logger.Log("Stopping video controller...");
                await _videoController.StopAsync();
                _videoController.Dispose();
                Logger.Log("Video controller stopped");
            }
            
            // TODO: Stop API server when implemented
            // _apiServer?.Dispose();
            
            // Stop command server if running
            Logger.Log("Stopping command server...");
            LocalCommandServer.Stop();
            
            // Unhook keyboard hook
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                Logger.Log("Keyboard hook removed");
            }
            
            Logger.Log("Closing application window...");
            
            // Use dispatcher to ensure we're on the UI thread
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    // First close the window
                    this.Close();
                    
                    // Then exit the application - use Environment.Exit for reliable shutdown
                    Logger.Log("Exiting application process...");
                    Environment.Exit(0);
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during cleanup: {ex.Message}");
            // Force exit even if cleanup fails
            Environment.Exit(1);
        }
    }

    #endregion

    #region Navigation Handlers

    /// <summary>
    /// Handles URL textbox Enter key press
    /// </summary>
    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            GoButton_Click(sender, null!);
        }
    }

    /// <summary>
    /// Navigates to the URL in the textbox
    /// </summary>
    private void GoButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot navigate: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            return;
        }

        if (!string.IsNullOrWhiteSpace(UrlTextBox?.Text))
        {
            var url = UrlTextBox.Text;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }
            NavigateToUrl(url);
        }
    }

    /// <summary>
    /// Navigate back
    /// </summary>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null && KioskWebView.CanGoBack == true)
        {
            KioskWebView.GoBack();
        }
        else
        {
            Logger.Log("Cannot go back: WebView2 not initialized or no history");
        }
    }

    /// <summary>
    /// Navigate forward
    /// </summary>
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null && KioskWebView.CanGoForward == true)
        {
            KioskWebView.GoForward();
        }
        else
        {
            Logger.Log("Cannot go forward: WebView2 not initialized or no history");
        }
    }

    /// <summary>
    /// Refresh the current page
    /// </summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null)
        {
            // Check if current source is valid before reloading
            var currentSource = KioskWebView.Source?.ToString();
            
            // If source is about:blank or invalid, navigate to stored URL instead
            if (string.IsNullOrEmpty(currentSource) || 
                currentSource.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(currentSource, UriKind.Absolute, out _))
            {
                // Use stored URL if available
                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    Logger.Log($"Reload: Current source is invalid ({currentSource}), navigating to stored URL: {_currentUrl}");
                    NavigateToUrl(_currentUrl);
                }
                else
                {
                    // Fallback to default URL
                    var defaultUrl = _config.Kiosk.DefaultUrl;
                    Logger.Log($"Reload: No valid URL, navigating to default: {defaultUrl}");
                    NavigateToUrl(defaultUrl);
                }
            }
            else
            {
                // Valid URL, safe to reload
                KioskWebView.Reload();
                Logger.Log($"Reloading current page: {currentSource}");
            }
        }
        else
        {
            Logger.Log("Cannot reload: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    /// <summary>
    /// Open developer tools
    /// </summary>
    private void DevToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null)
        {
            KioskWebView.CoreWebView2.OpenDevToolsWindow();
        }
        else
        {
            Logger.Log("Cannot open DevTools: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #region Media Device Selection (Camera & Microphone)

    /// <summary>
    /// Refresh the camera list from WebView2
    /// </summary>
    private async void RefreshCamerasButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadCamerasAsync();
    }

    /// <summary>
    /// Refresh the microphone list from WebView2
    /// </summary>
    private async void RefreshMicrophonesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadMicrophonesAsync();
    }

    /// <summary>
    /// Load all media devices (cameras and microphones)
    /// </summary>
    private async Task LoadAllMediaDevicesAsync()
    {
        await LoadCamerasAsync();
        await LoadMicrophonesAsync();
    }

    /// <summary>
    /// Load available cameras via JavaScript
    /// </summary>
    private async Task LoadCamerasAsync()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot load cameras: WebView2 not initialized");
            return;
        }

        try
        {
            Logger.Log("Loading available cameras...");
            
            // JavaScript to get camera list (requests permission if needed)
            var script = @"
                (async () => {
                    try {
                        // Request permission first
                        const tempStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true });
                        tempStream.getTracks().forEach(track => track.stop());
                        
                        // Get devices
                        const devices = await navigator.mediaDevices.enumerateDevices();
                        const cameras = devices
                            .filter(d => d.kind === 'videoinput')
                            .map(d => ({ deviceId: d.deviceId, label: d.label || 'Camera ' + d.deviceId.substring(0, 8) }));
                        return JSON.stringify(cameras);
                    } catch (e) {
                        console.error('Failed to enumerate cameras:', e);
                        return JSON.stringify([]);
                    }
                })();
            ";

            var result = await KioskWebView.CoreWebView2.ExecuteScriptAsync(script);
            
            // Parse the JSON result (it comes wrapped in quotes from ExecuteScriptAsync)
            var json = JsonSerializer.Deserialize<string>(result);
            if (!string.IsNullOrEmpty(json))
            {
                var cameras = JsonSerializer.Deserialize<List<MediaDeviceInfo>>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                if (cameras != null)
                {
                    _cameras = cameras;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CameraSelector.Items.Clear();
                        foreach (var cam in _cameras)
                        {
                            CameraSelector.Items.Add(cam);
                        }
                        Logger.Log($"Loaded {_cameras.Count} camera(s)");
                        
                        // Restore previous selection if available
                        if (_selectedCameraId != null)
                        {
                            var selected = _cameras.FirstOrDefault(c => c.DeviceId == _selectedCameraId);
                            if (selected != null)
                            {
                                CameraSelector.SelectedItem = selected;
                            }
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load cameras: {ex.Message}");
        }
    }

    /// <summary>
    /// Load available microphones via JavaScript
    /// </summary>
    private async Task LoadMicrophonesAsync()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot load microphones: WebView2 not initialized");
            return;
        }

        try
        {
            Logger.Log("Loading available microphones...");
            
            // JavaScript to get microphone list (requests permission if needed)
            var script = @"
                (async () => {
                    try {
                        // Request permission first
                        const tempStream = await navigator.mediaDevices.getUserMedia({ audio: true });
                        tempStream.getTracks().forEach(track => track.stop());
                        
                        // Get devices
                        const devices = await navigator.mediaDevices.enumerateDevices();
                        const microphones = devices
                            .filter(d => d.kind === 'audioinput')
                            .map(d => ({ deviceId: d.deviceId, label: d.label || 'Microphone ' + d.deviceId.substring(0, 8) }));
                        return JSON.stringify(microphones);
                    } catch (e) {
                        console.error('Failed to enumerate microphones:', e);
                        return JSON.stringify([]);
                    }
                })();
            ";

            var result = await KioskWebView.CoreWebView2.ExecuteScriptAsync(script);
            
            // Parse the JSON result (it comes wrapped in quotes from ExecuteScriptAsync)
            var json = JsonSerializer.Deserialize<string>(result);
            if (!string.IsNullOrEmpty(json))
            {
                var microphones = JsonSerializer.Deserialize<List<MediaDeviceInfo>>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                if (microphones != null)
                {
                    _microphones = microphones;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        MicrophoneSelector.Items.Clear();
                        foreach (var mic in _microphones)
                        {
                            MicrophoneSelector.Items.Add(mic);
                        }
                        Logger.Log($"Loaded {_microphones.Count} microphone(s)");
                        
                        // Restore previous selection if available
                        if (_selectedMicrophoneId != null)
                        {
                            var selected = _microphones.FirstOrDefault(m => m.DeviceId == _selectedMicrophoneId);
                            if (selected != null)
                            {
                                MicrophoneSelector.SelectedItem = selected;
                            }
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load microphones: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle camera selection change
    /// </summary>
    private async void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraSelector.SelectedItem is MediaDeviceInfo camera)
        {
            _selectedCameraId = camera.DeviceId;
            Logger.Log($"Selected camera: {camera.Label}");
            await ApplyMediaDeviceOverrideAsync();
        }
    }

    /// <summary>
    /// Handle microphone selection change
    /// </summary>
    private async void MicrophoneSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneSelector.SelectedItem is MediaDeviceInfo microphone)
        {
            _selectedMicrophoneId = microphone.DeviceId;
            Logger.Log($"Selected microphone: {microphone.Label}");
            await ApplyMediaDeviceOverrideAsync();
        }
    }

    /// <summary>
    /// Inject JavaScript to override camera and microphone selection for all future getUserMedia calls
    /// </summary>
    private async Task ApplyMediaDeviceOverrideAsync()
    {
        if (KioskWebView?.CoreWebView2 == null) return;

        // Escape the deviceIds for use in JavaScript
        var escapedCameraId = _selectedCameraId?.Replace("'", "\\'") ?? "";
        var escapedMicrophoneId = _selectedMicrophoneId?.Replace("'", "\\'") ?? "";
        
        var script = $@"
            // Store the preferred device IDs
            window.__preferredCameraId = '{escapedCameraId}' || window.__preferredCameraId || null;
            window.__preferredMicrophoneId = '{escapedMicrophoneId}' || window.__preferredMicrophoneId || null;
            
            // Override getUserMedia if not already done
            if (!window.__mediaDeviceOverrideApplied) {{
                const originalGetUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
                
                navigator.mediaDevices.getUserMedia = async (constraints) => {{
                    // Apply camera override
                    if (window.__preferredCameraId && constraints.video) {{
                        if (constraints.video === true) {{
                            constraints.video = {{ deviceId: {{ exact: window.__preferredCameraId }} }};
                        }} else if (typeof constraints.video === 'object') {{
                            constraints.video.deviceId = {{ exact: window.__preferredCameraId }};
                        }}
                        console.log('Camera override applied:', window.__preferredCameraId);
                    }}
                    
                    // Apply microphone override
                    if (window.__preferredMicrophoneId && constraints.audio) {{
                        if (constraints.audio === true) {{
                            constraints.audio = {{ deviceId: {{ exact: window.__preferredMicrophoneId }} }};
                        }} else if (typeof constraints.audio === 'object') {{
                            constraints.audio.deviceId = {{ exact: window.__preferredMicrophoneId }};
                        }}
                        console.log('Microphone override applied:', window.__preferredMicrophoneId);
                    }}
                    
                    return originalGetUserMedia(constraints);
                }};
                
                window.__mediaDeviceOverrideApplied = true;
                console.log('Media device override installed');
            }}
            
            'Devices set - Camera: ' + (window.__preferredCameraId ? 'Yes' : 'No') + ', Mic: ' + (window.__preferredMicrophoneId ? 'Yes' : 'No');
        ";

        try
        {
            var result = await KioskWebView.CoreWebView2.ExecuteScriptAsync(script);
            Logger.Log($"Media device override applied: {result}");
            
            // Build status message
            var cameraName = _cameras.FirstOrDefault(c => c.DeviceId == _selectedCameraId)?.Label;
            var micName = _microphones.FirstOrDefault(m => m.DeviceId == _selectedMicrophoneId)?.Label;
            var statusParts = new List<string>();
            if (cameraName != null) statusParts.Add($"📷 {cameraName}");
            if (micName != null) statusParts.Add($"🎤 {micName}");
            
            if (statusParts.Count > 0)
            {
                ShowStatus("Devices Selected", string.Join(" | ", statusParts));
                _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to apply media device override: {ex.Message}");
        }
    }

    #endregion

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLogViewer();
    }

    private async void SwitchMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Get available displays - use IReadOnlyList directly to avoid cast issues
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
                // Build monitor list with display info
                var monitorListPanel = new StackPanel { Spacing = 8 };
                var monitorComboBox = new ComboBox { Width = 300 };
                
                for (int i = 0; i < displayCount; i++)
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
                int nextMonitor = _currentMonitorIndex % displayCount;
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
                    return;
                }

                selectedMonitor = monitorComboBox.SelectedIndex + 1; // Convert to 1-based indexing
                Logger.Log($"User selected monitor {selectedMonitor} from dialog");
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

            _currentMonitorIndex = selectedMonitor;
            Logger.Log($"========== SWITCHING TO MONITOR {_currentMonitorIndex} ==========");
            
            // Update video controller if it exists
            if (_videoController != null)
            {
                // Store current video state
                bool wasInVideoMode = _isVideoMode;
                
                // Stop current video if playing
                if (wasInVideoMode)
                {
                    await _videoController.StopAsync();
                }
                
                // Dispose old controller
                _videoController.Dispose();
                
                // Create new video controller with updated monitor index
                _videoController = new VideoController(_config.Kiosk.VideoMode, _currentMonitorIndex);
                await _videoController.InitializeAsync();
                Logger.Log($"Video controller recreated for monitor {_currentMonitorIndex}");
                
                // If was in video mode, restart video on new monitor
                if (wasInVideoMode)
                {
                    await _videoController.HandleFlicButtonPressAsync();
                    Logger.Log("Restarted video on new monitor");
                }
            }
            
            // Move window to new monitor
            ConfigureAsKioskWindow();

            // If in debug mode, we need to restore the window frame and remove topmost
            // because ConfigureAsKioskWindow forces kiosk styling (borderless topmost)
            if (_isDebugMode)
            {
                // Restore window frame
                var style = GetWindowLong(_hwnd, GWL_STYLE);
                style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
                SetWindowLong(_hwnd, GWL_STYLE, style);

                // Remove topmost
                var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_TOPMOST;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                // Apply changes
                const uint SWP_NOSIZE = 0x0001;
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOSIZE | SWP_NOMOVE);
                    
                Logger.Log("Restored debug mode window styling after monitor switch");
            }
            
            // Show confirmation
            var dialog = new ContentDialog
            {
                Title = "Monitor Switched",
                Content = $"Moved to monitor {_currentMonitorIndex} of {displayCount}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching monitors: {ex.Message}");
            var errorDialog = new ContentDialog
            {
                Title = "Error",
                Content = $"Failed to switch monitors: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private void ToggleLogViewer()
    {
        _logsVisible = !_logsVisible;
        
        if (_logsVisible)
        {
            LogViewerPanel.Visibility = Visibility.Visible;
            RefreshLogDisplay();
            
            // Adjust WebView margin to make room for log viewer
            if (KioskWebView != null)
            {
                KioskWebView.Margin = new Thickness(0, 80, 0, 300);
            }
        }
        else
        {
            LogViewerPanel.Visibility = Visibility.Collapsed;
            
            // Restore WebView margin (accounting for debug panel)
            if (KioskWebView != null)
            {
                KioskWebView.Margin = new Thickness(0, 80, 0, 0);
            }
        }
    }

    private void RefreshLogDisplay()
    {
        try
        {
            var logs = Logger.GetRecentLogs(500); // Get last 500 entries
            var logText = string.Join(Environment.NewLine, logs);
            
            LogContentTextBlock.Text = logText;
            LogCountTextBlock.Text = $"{logs.Count} log entries (showing last 500 of {Logger.GetLogs().Count} total)";
            
            // Auto-scroll to bottom
            if (LogViewerPanel.Visibility == Visibility.Visible)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ScrollToBottom();
                });
            }
        }
        catch (Exception ex)
        {
            LogContentTextBlock.Text = $"Error loading logs: {ex.Message}";
            Logger.Log($"Error in RefreshLogDisplay: {ex.Message}");
        }
    }

    private void ScrollToBottom()
    {
        try
        {
            if (LogScrollViewer != null)
            {
                LogScrollViewer.ChangeView(null, double.MaxValue, null);
            }
        }
        catch { }
    }

    private void OnLogAdded(string logEntry)
    {
        // Update log display in real-time if viewer is visible
        if (_logsVisible && LogViewerPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Append new log entry
                    if (!string.IsNullOrEmpty(LogContentTextBlock.Text))
                    {
                        LogContentTextBlock.Text += Environment.NewLine;
                    }
                    LogContentTextBlock.Text += logEntry;
                    
                    // Update count
                    var totalLogs = Logger.GetLogs().Count;
                    var displayedLogs = LogContentTextBlock.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length;
                    LogCountTextBlock.Text = $"{displayedLogs} log entries (showing last 500 of {totalLogs} total)";
                    
                    // Auto-scroll to bottom
                    ScrollToBottom();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error in OnLogAdded: {ex.Message}");
                }
            });
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        LogContentTextBlock.Text = "";
        LogCountTextBlock.Text = "0 log entries";
        Logger.Log("Log viewer cleared by user");
    }

    private void CloseLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLogViewer();
    }

    #endregion
}