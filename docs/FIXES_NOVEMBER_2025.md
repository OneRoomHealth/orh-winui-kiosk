# Critical Fixes - November 18-19, 2025

## Overview
This document describes critical fixes applied to resolve major issues with the OneRoom Health Kiosk application.

## Latest Fix - Window Sizing Issues (v1.0.46)

### Issues
1. When pressing Ctrl+Alt+E to return to screensaver mode, the window was not properly entering fullscreen - taskbar was visible
2. After exiting debug mode, the screensaver was displaying at quarter screen size in the upper left corner

### Root Cause
The window presenter needs to be set to FullScreen BEFORE calling `ConfigureAsKioskWindow()`. When configuring the window while it's still in Overlapped mode, the sizing doesn't apply correctly.

### Solution
1. Modified `SwitchToScreensaverMode()` to:
   - Set AppWindow presenter to FullScreen FIRST
   - Wait 100ms for presenter change to take effect
   - Then call `ConfigureAsKioskWindow()` to properly size the window
   - Re-verify fullscreen after configuration
2. Modified `ExitDebugMode()` to:
   - Restructure to set fullscreen presenter FIRST
   - Wait 100ms before configuring
   - Then call `ConfigureAsKioskWindow()` to properly size the window
   - Re-verify fullscreen after configuration

### Changes Made
1. Modified `SwitchToScreensaverMode()` to set fullscreen before configuring
2. Restructured `ExitDebugMode()` to properly sequence fullscreen setting and window configuration
3. Added proper async/await handling for the delayed operations

## Previous Fix - Video Stop Cancellation Error (v1.0.44)

### Issue
- When pressing Ctrl+Alt+E to switch from video mode to screensaver mode, getting "A task was canceled" error
- The monitoring task for demo videos was throwing OperationCanceledException when stopped
- This prevented successful switching back to screensaver mode

### Solution
- Added try-catch blocks in `StopAsync()` to handle expected OperationCanceledException
- Added try-catch block in `MonitorDemoCompletionAsync()` to handle cancellation gracefully
- Added explicit `_isDemoPlaying = false` reset in StopAsync
- Added logging for expected cancellation scenarios

### Changes Made
1. Modified `VideoController.StopAsync()` to:
   - Catch and handle OperationCanceledException when awaiting monitoring task
   - Reset `_isDemoPlaying` flag
   - Add completion logging
2. Modified `MonitorDemoCompletionAsync()` to:
   - Wrap monitoring loop in try-catch
   - Handle OperationCanceledException gracefully

## Previous Fix - Application Exit (v1.0.43)

### Issue
- After entering the correct password with Ctrl+Shift+Q, the application was not properly exiting
- The window close and application exit calls were not working reliably in WinUI 3

### Solution
- Replaced `Application.Current.Exit()` with `Environment.Exit(0)` for reliable process termination
- Added proper disposal of video controller
- Enhanced logging to track the exit process
- Added fallback exit in error handler

### Changes Made
1. Modified `CleanupAndExit()` method to:
   - Add comprehensive logging of exit steps
   - Properly dispose video controller
   - Use dispatcher for UI thread operations
   - Replace unreliable WinUI exit with `Environment.Exit(0)`
   - Add fallback `Environment.Exit(1)` in catch block

## Previous Fix - Screensaver Navigation and Tooltip (v1.0.42)

### Issues
1. When pressing Ctrl+Alt+E to return to screensaver mode, the WebView was shown but not navigated to any URL, resulting in a blank screen
2. The window wasn't ensuring fullscreen mode when returning to screensaver
3. A "Ctrl+Shift+I" tooltip was appearing on startup

### Solution
1. Modified `SwitchToScreensaverMode` to navigate back to the screensaver URL when switching modes
2. Added `ConfigureAsKioskWindow()` call to ensure fullscreen when returning to screensaver
3. Added `KeyboardAcceleratorPlacementMode="Hidden"` to the main Grid to disable accelerator tooltips

### Changes Made
1. Updated `SwitchToScreensaverMode` method to:
   - Call `ConfigureAsKioskWindow()` to ensure fullscreen
   - Navigate WebView to current or default URL
2. Modified `MainWindow.xaml` to hide keyboard accelerator tooltips

## Previous Fix - Monitor Indexing and Switching (v1.0.41)

### Issue
- Monitor indexing mismatch: The main window used 1-based indexing directly while MPV video player converted to 0-based
- This caused the screensaver and video to appear on different monitors
- No way to switch monitors at runtime without restarting the app

### Solution
- Fixed monitor indexing to properly convert from 1-based config values to 0-based display array indices
- Added "Switch Monitor" button in debug mode to cycle through available displays
- Video controller is recreated when switching monitors to ensure proper playback

### Changes Made
1. Added `_currentMonitorIndex` field to track the active monitor
2. Updated `ConfigureAsKioskWindow` to convert from 1-based to 0-based indexing
3. Added `SwitchMonitorButton` to debug panel
4. Implemented `SwitchMonitorButton_Click` handler that:
   - Cycles through available monitors
   - Recreates video controller with new monitor index
   - Moves window to selected monitor
   - Restarts video if it was playing

## Previous Fix - Dynamic Mode Switching (v1.0.40)

### Issue
- The app was starting based on config.videoMode.enabled, but user wanted it to always start in screensaver mode
- User needed the ability to switch between screensaver and video modes dynamically using hotkeys

### Solution
- App now **always** starts in screensaver mode (WebView visible) regardless of config
- Video controller is initialized if video configuration is present
- New hotkey functionality:
  - **Ctrl+Alt+D**: 
    - From screensaver mode: Switches to video mode (hides WebView, starts carescape video)
    - From video mode: Toggles between carescape and demo videos
  - **Ctrl+Alt+E**: Switch to screensaver mode (stops video, shows WebView)
  - **Ctrl+Alt+R**: Restart carescape video (only works in video mode)

### Changes Made
1. Modified constructor to always start with `_isVideoMode = false`
2. Updated `InitializeWebViewAsync` to always show WebView and initialize video controller if available
3. Added `SwitchToVideoMode()` and `SwitchToScreensaverMode()` methods
4. Updated all hotkey handlers to use the new mode switching methods
5. Updated logging to reflect the new "Mode Toggle Controls"
6. Modified `SwitchToVideoMode()` to handle dual functionality:
   - When not in video mode: switches to video mode and starts carescape
   - When already in video mode: toggles between carescape and demo videos

## Previous Issues Fixed

### 1. Blank White Screen on Startup

**Problem**: When opening the application in web mode (not video mode), users would see a blank white screen instead of the screensaver. The content only appeared after opening developer tools via "inspect".

**Root Cause**: The `StatusOverlay` UI element was remaining visible after initialization, blocking the WebView2 content. The overlay was supposed to hide automatically when navigation completed, but due to timing issues or navigation events not firing properly, it stayed visible.

**Solution Implemented**:
1. Added a 3-second timeout fallback in `InitializeWebViewAsync()` that automatically hides the status overlay if navigation doesn't complete
2. Added explicit `HideStatus()` call in `SetupWebView()` to ensure overlay is hidden when WebView2 is ready
3. Added logging to track when the status overlay is being hidden

**Files Modified**: `KioskApp/MainWindow.xaml.cs` (lines 658-670, 713-715)

**Testing**: Start the application in web mode and verify that the screensaver URL loads and displays immediately without showing a persistent overlay.

---

### 2. Password Configuration Complexity

**Problem**: The exit password was required to be stored as a SHA256 hash in the configuration file, making it difficult to quickly set or change passwords. Users had to manually generate hashes or use helper utilities.

**Root Cause**: The password validation logic only supported SHA256 hashes via the `SecurityHelper.ValidatePassword()` method.

**Solution Implemented**:
1. Updated `HandleExitRequest()` to support three password modes:
   - **Empty/null password**: Defaults to "admin123"
   - **Plain text password**: Any string less than 64 characters (treated as plain text)
   - **SHA256 hash**: 64-character hex strings (treated as hash for backward compatibility)
2. Added logic to auto-detect password type based on length and format
3. Updated sample configuration to use plain text password

**Files Modified**: 
- `KioskApp/MainWindow.xaml.cs` (lines 1103-1162)
- `sample-video-config.json` (line 26)

**Configuration Example**:
```json
{
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Escape",
    "requirePassword": true,
    "passwordHash": "admin123",
    "timeout": 5000
  }
}
```

**Security Note**: Plain text passwords are stored in the configuration file. For production deployments requiring higher security, continue using SHA256 hashes (64-character hex strings).

---

### 3. Window Appearing on Wrong Monitor

**Problem**: The application window was starting on monitor index 0 (primary display) even though `targetMonitorIndex` was configured to 1 (second monitor). Videos were correctly playing on the second monitor, but the main window stayed on the first.

**Root Cause**: Race condition during window initialization - the window positioning code executed correctly, but due to WinUI timing issues, the window didn't always move to the target display before being shown.

**Solution Implemented**:
1. Added asynchronous verification step after initial window positioning
2. After a 200ms delay, code re-checks if window is on the correct display
3. If window is on wrong display, forces another `SetWindowPos()` call
4. Added detailed logging to track window positioning success/failure
5. Verification only runs when `targetMonitorIndex > 0` to avoid unnecessary checks on single-monitor setups

**Files Modified**: `KioskApp/MainWindow.xaml.cs` (lines 595-628)

**Testing**: 
1. Configure application with `targetMonitorIndex: 1` (or higher)
2. Start application
3. Verify window appears on the correct monitor
4. Check logs to confirm "Window successfully positioned on target display" message

---

## Additional Notes

### Logging Enhancements
All fixes include enhanced logging to help diagnose issues:
- Status overlay hide events are logged
- Password validation mode (empty/plain/hash) is logged
- Window positioning verification results are logged

### Backward Compatibility
- SHA256 password hashes continue to work (64-character hex strings)
- Single-monitor setups are unaffected by positioning verification logic
- Existing configurations will continue to function

### Testing Checklist
- [ ] Application starts and shows screensaver immediately (no blank screen)
- [ ] Exit password "admin123" works correctly
- [ ] Window appears on configured monitor index
- [ ] Video playback appears on configured monitor (if video mode enabled)
- [ ] Debug mode toggle still works (Ctrl+Shift+I or Ctrl+Shift+F12)
- [ ] Exit mechanism works (Ctrl+Shift+Escape)

---

## Configuration File Location
`%ProgramData%\OneRoomHealth\Kiosk\config.json`

For most systems, this resolves to:
`C:\ProgramData\OneRoomHealth\Kiosk\config.json`

## Deployment Notes
After deploying these fixes:
1. Update the configuration file to use plain text password if desired
2. Restart the kiosk application
3. Verify all functionality works as expected
4. Check log files for any warnings or errors

## Questions or Issues?
If you encounter any problems after applying these fixes, check:
1. Log file: `%LocalAppData%\OneRoomHealthKiosk\logs\kiosk.log`
2. Windows Event Viewer for security events
3. Verify configuration file syntax is valid JSON

