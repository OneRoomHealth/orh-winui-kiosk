# Troubleshooting Guide for Kiosk Application Issues

## Current Issues Fixed

### 1. Keyboard Hotkeys Not Working

**Problem**: CoreWindow.GetForCurrentThread() doesn't work in WinUI 3

**Solutions Implemented**:
1. **KeyboardAccelerators** - Primary method using WinUI 3's built-in keyboard accelerator system
2. **PreviewKeyDown Event** - Backup method that captures all keyboard input on the window

**New Hotkeys**:
- Debug Mode: `Ctrl+Shift+I` (was Ctrl+Shift+F12)
- Exit Kiosk: `Ctrl+Shift+Q` (was Ctrl+Shift+Escape)
- Flic Button: `Ctrl+Alt+D` (unchanged)

### 2. Fullscreen Not Working

**Problem**: Window not going fullscreen on the correct monitor

**Solutions Implemented**:
1. Uses configuration value `targetMonitorIndex` from config.json
2. Added AppWindow presenter fullscreen mode
3. Enhanced debugging to log window positioning
4. Added timing delays to ensure window is ready

## How to Debug

### 1. Check the Log File

The application logs detailed information to:
`%LocalAppData%\OneRoomHealthKiosk\kiosk.log`

Look for these key messages:
- "Keyboard accelerators registered: X total"
- "ConfigureAsKioskWindow started"
- "Setting window position"
- "SetWindowPos result"
- "PreviewKeyDown: Key=..."

### 2. Test Keyboard Input

1. Click the "Test" button in the top-right corner
   - This verifies the window is receiving input
   - Shows a message with the hotkey instructions

2. Check the log after pressing keys:
   - You should see "PreviewKeyDown: Key=X, Ctrl=true/false..." entries
   - This confirms keyboard input is being received

### 3. Verify Configuration

Check your config file at:
`%ProgramData%\OneRoomHealth\Kiosk\config.json`

Ensure these settings:
```json
{
  "kiosk": {
    "targetMonitorIndex": 1,  // 0 = first monitor, 1 = second monitor (default is 1)
    "fullscreen": true,
    "alwaysOnTop": true
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I"
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q"
  }
}
```

**Note**: If `targetMonitorIndex` is not specified in config.json, it defaults to 1 (second monitor).

### 4. Window Focus Issues

If hotkeys still don't work:
1. Click anywhere in the window to ensure it has focus
2. Click the Test button to verify input is working
3. Try the hotkeys again

### 5. Monitor Configuration

The log file will show available monitors:
```
Found 2 display(s)
  Display 0: 1920x1080 at (0, 0)
  Display 1: 3840x2160 at (1920, 0)
Using configured monitor index 1
```

If the wrong monitor is used:
1. Check the monitor count and indices in the log
2. Adjust `targetMonitorIndex` in config.json
3. Restart the application

## Common Solutions

### Hotkeys Not Working

1. **Ensure Window Has Focus**
   - Click in the window
   - Press Alt+Tab to switch to it

2. **Check if Another App is Intercepting Keys**
   - Close apps like AutoHotkey, keyboard managers
   - Disable gaming software overlays

3. **Run as Administrator**
   - Some keyboard hooks require elevated permissions

### Fullscreen Issues

1. **Wrong Monitor**
   - Check `targetMonitorIndex` in config
   - Monitor indices start at 0

2. **Window Not Fullscreen**
   - Check the log for SetWindowPos errors
   - Try running as administrator
   - Ensure no other always-on-top windows are active

3. **Window Has Borders**
   - The log should show window style changes
   - If styles aren't applying, try running as admin

## Testing Procedure

1. **Start the application**
2. **Check the log file** immediately for initialization messages
3. **Click the Test button** to verify UI responsiveness
4. **Press any key** and check log for PreviewKeyDown entries
5. **Try Ctrl+Shift+I** for debug mode
6. **Try Ctrl+Shift+Q** to exit

## If All Else Fails

1. **Delete config.json** and let it recreate with defaults
2. **Run as Administrator**
3. **Check Windows Event Viewer** for application errors
4. **Ensure .NET 6.0+ and WebView2 Runtime** are installed
5. **Try on a different user account** to rule out profile issues

## Debug Information to Collect

When reporting issues, include:
1. The full contents of `kiosk.log`
2. Your `config.json` file
3. Windows version and build number
4. Number of monitors and their configuration
5. Whether running as admin helps
6. Any error messages or dialogs
