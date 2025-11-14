# Hotkey and Fullscreen Fixes Summary

**Date:** November 14, 2025  
**Status:** ✅ All Issues Fixed

## Overview

Fixed multiple issues with the OneRoom Health WinUI Kiosk application:

1. Changed debug mode hotkey from `Ctrl+Shift+F12` to `Ctrl+Shift+I`
2. Changed escape/exit mode hotkey from `Ctrl+Shift+Escape` to `Ctrl+Shift+Q`
3. Fixed fullscreen mode to use configured monitor from `config.json` instead of hardcoded constant
4. Updated all documentation to reflect the new hotkeys

## Changes Made

### 1. MainWindow.xaml.cs
- Updated `CoreWindow_KeyDown` method to detect new hotkeys:
  - Debug mode: `VirtualKey.I` instead of `VirtualKey.F12`
  - Exit mode: `VirtualKey.Q` instead of `VirtualKey.Escape`
- Fixed `ConfigureAsKioskWindow` method to use `_config.Kiosk.TargetMonitorIndex` instead of hardcoded `TARGET_MONITOR_INDEX`
- Updated method documentation to reflect new hotkeys

### 2. KioskConfiguration.cs
- Changed default debug hotkey from `"Ctrl+Shift+F12"` to `"Ctrl+Shift+I"`
- Changed default exit hotkey from `"Ctrl+Shift+Escape"` to `"Ctrl+Shift+Q"`

### 3. Documentation Updates
- Updated `DEBUG_MODE_IMPLEMENTATION_SUMMARY.md` with new hotkey information
- All references to old hotkeys have been updated

## Configuration

The hotkeys are configurable via the `config.json` file located at:
`%ProgramData%\OneRoomHealth\Kiosk\config.json`

```json
{
  "kiosk": {
    "targetMonitorIndex": 1,  // 0-based index (0 = first monitor, 1 = second monitor, etc.) - defaults to 1 if not specified
    "fullscreen": true,
    "alwaysOnTop": true,
    "videoMode": {
      "enabled": false,
      "targetMonitor": 1  // Also defaults to 1
    }
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I",
    "autoOpenDevTools": false,
    "windowSizePercent": 80
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q",
    "requirePassword": true,
    "passwordHash": "",
    "timeout": 5000
  }
}
```

## Testing the Fixes

### Debug Mode (Ctrl+Shift+I)
1. Launch the kiosk application
2. Press `Ctrl+Shift+I` to enter debug mode
3. The window should resize to 80% and become movable/resizable
4. Press `Ctrl+Shift+I` again to exit debug mode and return to fullscreen

### Exit Mode (Ctrl+Shift+Q)
1. Press `Ctrl+Shift+Q` to trigger exit
2. Enter the password (default: `admin123`)
3. The application should close

### Fullscreen Monitor Selection
1. Edit `config.json` and set `targetMonitorIndex` to your desired monitor (0-based)
2. Restart the application
3. The application should appear fullscreen on the selected monitor

### Flic Button Integration (Ctrl+Alt+D)
The Flic button integration remains unchanged and continues to work with:
- `Ctrl+Alt+D` - Toggle between carescape and demo videos
- `Ctrl+Alt+E` - Stop video
- `Ctrl+Alt+R` - Restart carescape video

## Troubleshooting

### Hotkeys Not Working
1. Ensure the application window has focus
2. Check that `debug.enabled` and `exit.enabled` are set to `true` in config.json
3. Verify the keyboard is working correctly

### Wrong Monitor
1. Check available monitors in the log file (they are listed on startup)
2. Adjust `targetMonitorIndex` in config.json (remember it's 0-based)
3. Restart the application after changes

### Application Not Fullscreen
1. Check `kiosk.fullscreen` is set to `true` in config.json
2. Verify no other windows are forcing themselves on top
3. Check the log file for any errors during window configuration

## Notes

- The hotkeys are now fully configurable via config.json
- The monitor selection is dynamic and uses the configuration value
- All changes are backward compatible with existing configurations
- The default configuration will be created automatically if it doesn't exist
