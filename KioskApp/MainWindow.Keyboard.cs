using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Keyboard handling (hooks, hotkeys, accelerators).
/// </summary>
public sealed partial class MainWindow
{
    #region Keyboard Hook Fields

    private Win32Native.LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _hookID = IntPtr.Zero;

    #endregion

    #region Keyboard Setup

    /// <summary>
    /// Sets up comprehensive keyboard handling to ensure hotkeys work reliably.
    /// Uses multiple methods for redundancy.
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
    /// Sets up a low-level keyboard hook to capture all keyboard input.
    /// </summary>
    private void SetupLowLevelKeyboardHook()
    {
        try
        {
            _keyboardProc = HookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookID = Win32Native.SetWindowsHookEx(
                Win32Native.WH_KEYBOARD_LL,
                _keyboardProc,
                Win32Native.GetModuleHandle(curModule.ModuleName),
                0);
            Logger.Log("Low-level keyboard hook installed");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to install keyboard hook: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the low-level keyboard hook.
    /// </summary>
    private void RemoveKeyboardHook()
    {
        if (_hookID != IntPtr.Zero)
        {
            Win32Native.UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
            Logger.Log("Low-level keyboard hook removed");
        }
    }

    #endregion

    #region Keyboard Hook Callback

    /// <summary>
    /// Low-level keyboard hook callback.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Win32Native.WM_KEYDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<Win32Native.KBDLLHOOKSTRUCT>(lParam);
            VirtualKey vkCode = (VirtualKey)hookStruct.vkCode;

            // Get modifier states
            bool ctrlPressed = Win32Native.IsKeyPressed((int)VirtualKey.Control);
            bool shiftPressed = Win32Native.IsKeyPressed((int)VirtualKey.Shift);
            bool altPressed = Win32Native.IsKeyPressed((int)VirtualKey.Menu);

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
                handled = HandleVideoModeHotkey(vkCode, "keyboard hook");
            }

            // If we handled this key, consume it
            if (handled)
            {
                return (IntPtr)1;
            }
        }

        return Win32Native.CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    #endregion

    #region PreviewKeyDown Handler

    /// <summary>
    /// Content PreviewKeyDown handler - backup method for hotkeys.
    /// </summary>
    private async void Content_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            // Get modifier states using Win32 API
            bool ctrlPressed = Win32Native.IsKeyPressed((int)VirtualKey.Control);
            bool shiftPressed = Win32Native.IsKeyPressed((int)VirtualKey.Shift);
            bool altPressed = Win32Native.IsKeyPressed((int)VirtualKey.Menu);

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
                if (HandleVideoModeHotkey(e.Key, "PreviewKeyDown"))
                {
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in Content_PreviewKeyDown: {ex.Message}");
        }
    }

    #endregion

    #region Keyboard Accelerators

    /// <summary>
    /// Sets up keyboard accelerators as an additional method.
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
                AddVideoModeAccelerators(content);
            }

            Logger.Log($"Keyboard accelerators set up: {content.KeyboardAccelerators.Count} total");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting up keyboard accelerators: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds video mode accelerators to the content element.
    /// </summary>
    private void AddVideoModeAccelerators(FrameworkElement content)
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
            if (_isVideoMode && _videoController != null)
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

    #endregion

    #region Hotkey Helpers

    /// <summary>
    /// Handles video mode hotkeys (Ctrl+Alt+D/E/R).
    /// </summary>
    /// <returns>True if the hotkey was handled.</returns>
    private bool HandleVideoModeHotkey(VirtualKey key, string source)
    {
        Logger.Log($"[HOTKEY DEBUG] Mode toggle hotkey detected: Key={key}, CurrentVideoMode={_isVideoMode}, Controller={_videoController != null}");

        if (_videoController == null)
        {
            Logger.Log($"[HOTKEY WARNING] Video hotkey pressed but no video controller available ({source})");
            return false;
        }

        switch (key)
        {
            case VirtualKey.D:
                if (_isVideoMode)
                    Logger.Log($"Toggle video source (Ctrl+Alt+D) via {source}");
                else
                    Logger.Log($"Switch to VIDEO MODE (Ctrl+Alt+D) via {source}");
                DispatcherQueue.TryEnqueue(async () => await SwitchToVideoMode());
                return true;

            case VirtualKey.E:
                Logger.Log($"Switch to SCREENSAVER MODE (Ctrl+Alt+E) via {source}");
                DispatcherQueue.TryEnqueue(async () => await SwitchToScreensaverMode());
                return true;

            case VirtualKey.R:
                if (_isVideoMode)
                {
                    Logger.Log($"Restart carescape pressed (Ctrl+Alt+R) via {source}");
                    DispatcherQueue.TryEnqueue(async () => await _videoController.RestartCarescapeAsync());
                    return true;
                }
                else
                {
                    Logger.Log("[HOTKEY WARNING] Ctrl+Alt+R pressed but not in video mode");
                    return false;
                }

            default:
                return false;
        }
    }

    #endregion
}
