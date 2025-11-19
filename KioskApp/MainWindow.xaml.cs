using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
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

    public MainWindow(KioskConfiguration config)
    {
        this.InitializeComponent();
        _config = config;
        Logger.Log("MainWindow constructor called");
        Logger.Log($"Log file is being written to: {Logger.LogFilePath}");
        
        // Subscribe to log events for real-time updates
        Logger.LogAdded += OnLogAdded;
        
        // Determine if we're in video mode based on config
        _isVideoMode = _config.Kiosk.VideoMode?.Enabled ?? false;
        Logger.Log($"Video mode enabled: {_isVideoMode}");
        
        // Initialize video controller if video mode is enabled
        if (_isVideoMode && _config.Kiosk.VideoMode != null)
        {
            _videoController = new VideoController(_config.Kiosk.VideoMode, _config.Kiosk.TargetMonitorIndex);
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
            Logger.Log($"Target monitor index: {_config.Kiosk.TargetMonitorIndex}");
            
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
            if (_isVideoMode)
            {
                Logger.Log("  Video Controls:");
                Logger.Log("    Toggle Video: Ctrl+Alt+D");
                Logger.Log("    Stop Video: Ctrl+Alt+E");
                Logger.Log("    Restart Carescape: Ctrl+Alt+R");
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
            // Video controls when in video mode
            else if (ctrlPressed && altPressed && (vkCode == VirtualKey.D || vkCode == VirtualKey.E || vkCode == VirtualKey.R))
            {
                // Add detailed logging for debugging
                Logger.Log($"[HOTKEY DEBUG] Video hotkey detected: Key={vkCode}, VideoMode={_isVideoMode}, Controller={_videoController != null}, ConfigEnabled={_config.Kiosk.VideoMode?.Enabled ?? false}");
                
                // Ensure video mode is enabled in config
                bool configVideoModeEnabled = _config.Kiosk.VideoMode?.Enabled ?? false;
                
                // Re-check video mode state if config says it should be enabled but our flag says it's not
                if (configVideoModeEnabled && !_isVideoMode)
                {
                    Logger.Log("[HOTKEY DEBUG] Config says video mode enabled but _isVideoMode is false, re-initializing...");
                    _isVideoMode = true;
                    if (_videoController == null && _config.Kiosk.VideoMode != null)
                    {
                        _videoController = new VideoController(_config.Kiosk.VideoMode, _config.Kiosk.TargetMonitorIndex);
                        Logger.Log("Video controller created on-demand for hotkey");
                    }
                }
                
                if (_isVideoMode && _videoController != null)
                {
                    switch (vkCode)
                    {
                        case VirtualKey.D:
                            handled = true;
                            Logger.Log("Flic button pressed (Ctrl+Alt+D) via keyboard hook");
                            DispatcherQueue.TryEnqueue(async () => await _videoController.HandleFlicButtonPressAsync());
                            break;
                        case VirtualKey.E:
                            handled = true;
                            Logger.Log("Stop video pressed (Ctrl+Alt+E) via keyboard hook");
                            DispatcherQueue.TryEnqueue(async () => await _videoController.StopAsync());
                            break;
                        case VirtualKey.R:
                            handled = true;
                            Logger.Log("Restart carescape pressed (Ctrl+Alt+R) via keyboard hook");
                            DispatcherQueue.TryEnqueue(async () => await _videoController.RestartCarescapeAsync());
                            break;
                    }
                }
                else
                {
                    Logger.Log($"[HOTKEY WARNING] Video hotkey pressed but video mode not active. VideoMode={_isVideoMode}, Controller={_videoController != null}, ConfigEnabled={configVideoModeEnabled}");
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
                Logger.Log($"[HOTKEY DEBUG] Video hotkey detected (PreviewKeyDown): Key={e.Key}, VideoMode={_isVideoMode}, Controller={_videoController != null}, ConfigEnabled={_config.Kiosk.VideoMode?.Enabled ?? false}");
                
                // Ensure video mode is enabled in config
                bool configVideoModeEnabled = _config.Kiosk.VideoMode?.Enabled ?? false;
                
                // Re-check video mode state if config says it should be enabled but our flag says it's not
                if (configVideoModeEnabled && !_isVideoMode)
                {
                    Logger.Log("[HOTKEY DEBUG] Config says video mode enabled but _isVideoMode is false, re-initializing...");
                    _isVideoMode = true;
                    if (_videoController == null && _config.Kiosk.VideoMode != null)
                    {
                        _videoController = new VideoController(_config.Kiosk.VideoMode, _config.Kiosk.TargetMonitorIndex);
                        Logger.Log("Video controller created on-demand for hotkey");
                    }
                }
                
                if (_isVideoMode && _videoController != null)
                {
                    if (e.Key == VirtualKey.D)
                    {
                        e.Handled = true;
                        Logger.Log("Flic button pressed (Ctrl+Alt+D) via PreviewKeyDown");
                        await _videoController.HandleFlicButtonPressAsync();
                    }
                    else if (e.Key == VirtualKey.E)
                    {
                        e.Handled = true;
                        Logger.Log("Stop video pressed (Ctrl+Alt+E) via PreviewKeyDown");
                        await _videoController.StopAsync();
                    }
                    else if (e.Key == VirtualKey.R)
                    {
                        e.Handled = true;
                        Logger.Log("Restart carescape pressed (Ctrl+Alt+R) via PreviewKeyDown");
                        await _videoController.RestartCarescapeAsync();
                    }
                }
                else
                {
                    Logger.Log($"[HOTKEY WARNING] Video hotkey pressed but video mode not active (PreviewKeyDown). VideoMode={_isVideoMode}, Controller={_videoController != null}, ConfigEnabled={configVideoModeEnabled}");
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

            // Video mode accelerators
            if (_isVideoMode)
            {
                // Flic button: Ctrl+Alt+D
                var flicAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.D,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                flicAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    if (_videoController != null)
                    {
                        Logger.Log("Flic accelerator invoked");
                        await _videoController.HandleFlicButtonPressAsync();
                    }
                };
                content.KeyboardAccelerators.Add(flicAccel);

                // Stop: Ctrl+Alt+E
                var stopAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.E,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                stopAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    if (_videoController != null)
                    {
                        Logger.Log("Stop accelerator invoked");
                        await _videoController.StopAsync();
                    }
                };
                content.KeyboardAccelerators.Add(stopAccel);

                // Restart: Ctrl+Alt+R
                var restartAccel = new KeyboardAccelerator
                {
                    Key = VirtualKey.R,
                    Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                };
                restartAccel.Invoked += async (s, e) =>
                {
                    e.Handled = true;
                    if (_videoController != null)
                    {
                        Logger.Log("Restart accelerator invoked");
                        await _videoController.RestartCarescapeAsync();
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
            int targetMonitorIndex = _config.Kiosk.TargetMonitorIndex;
            Logger.Log($"========== MONITOR SELECTION ==========");
            Logger.Log($"Configured target monitor index: {targetMonitorIndex}");
            
            if (targetMonitorIndex >= 0 && targetMonitorIndex < allDisplays.Count)
            {
                targetDisplay = allDisplays[targetMonitorIndex];
                Debug.WriteLine($"Using configured monitor index {targetMonitorIndex}");
                Logger.Log($"✓ Using configured monitor index {targetMonitorIndex}");
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
    /// Initializes WebView2 and navigates to the configured URL or starts video mode.
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
            
            // Handle video mode vs web mode
            Logger.Log($"Video mode: {_isVideoMode}");
            if (_isVideoMode)
            {
                // Hide the WebView in video mode
                Logger.Log("VIDEO MODE: Hiding WebView");
                KioskWebView.Visibility = Visibility.Collapsed;
                Logger.Log("WebView hidden for video mode");
                
                // Initialize video controller (validates paths, doesn't start video)
                if (_videoController != null)
                {
                    await _videoController.InitializeAsync();
                    Logger.Log("Video controller ready (waiting for Flic button or trigger)");
                }
            }
            else
            {
                // Web mode - ensure WebView2 runtime is available
                Logger.Log("WEB MODE: Initializing WebView2");
                try
                {
                    var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    Logger.Log($"WebView2 Runtime version: {version}");
                    ShowStatus("Initializing", "Loading WebView2...");
                    
                    Logger.Log("Ensuring CoreWebView2 is ready...");
                    await KioskWebView.EnsureCoreWebView2Async();
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
                // Inject JavaScript to prevent WebView from consuming our hotkeys
                string script = @"
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
                ";
                await KioskWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error injecting keyboard handling script: {ex.Message}");
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

        await Task.Run(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
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
                    
                    // Hide debug navigation panel first
                    DebugPanel.Visibility = Visibility.Collapsed;
                    
                    // Reset WebView margin
                    if (KioskWebView != null)
                        KioskWebView.Margin = new Thickness(0);

                    // Update window title
                    this.Title = "OneRoom Health Kiosk";

                    // Restore kiosk window configuration (this sets up the window properly)
                    ConfigureAsKioskWindow();

                    // Ensure we're in fullscreen mode after configuration
                    if (_appWindow != null && _appWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
                    {
                        _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                        Logger.Log("Set presenter to FullScreen after ConfigureAsKioskWindow");
                    }
                    
                    // Wait a moment then ensure fullscreen is properly applied
                    _ = Task.Delay(100).ContinueWith(_ =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            // Re-run ConfigureAsKioskWindow to ensure proper sizing after presenter change
                            if (_appWindow != null && _appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                ConfigureAsKioskWindow();
                                Logger.Log("Re-ran ConfigureAsKioskWindow to ensure fullscreen sizing");
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
                        _ = Task.Delay(100).ContinueWith(_ =>
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (KioskWebView?.CoreWebView2 != null && Uri.TryCreate(_currentUrl, UriKind.Absolute, out var uri))
                                {
                                    KioskWebView.Source = uri;
                                    Logger.Log($"Navigating to URL after debug mode: {_currentUrl}");
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
        });
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
            // Stop video controller if active
            if (_videoController != null)
            {
                await _videoController.StopAsync();
            }
            
            // TODO: Stop API server when implemented
            // _apiServer?.Dispose();
            
            // Stop command server if running
            LocalCommandServer.Stop();
            
            // Unhook keyboard hook
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                Logger.Log("Keyboard hook removed");
            }
            
            // Close the window
            this.Close();
            
            // Exit the application
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during cleanup: {ex.Message}");
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

    /// <summary>
    /// Navigate to camera test page
    /// </summary>
    private void CameraTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot navigate to camera test: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            return;
        }

        // Navigate to WebRTC test pages for camera testing
        NavigateToUrl("https://webrtc.github.io/test-pages/");
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLogViewer();
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