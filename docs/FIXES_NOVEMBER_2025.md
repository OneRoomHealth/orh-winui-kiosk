# Critical Fixes - November 18, 2025

## Overview
This document describes critical fixes applied to resolve three major issues with the OneRoom Health Kiosk application.

## Issues Fixed

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

